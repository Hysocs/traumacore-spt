using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal static partial class OrganSystem
    {
        internal static ConfigEntry<bool> Enabled, DebugEsp, DebugLogging,
            ForceFragmentation, BloodEffects, ShowCalibrationMannequin,
            IsEntireHeadFatal;
        internal static ConfigEntry<float> DebugEspRange, DirectDamagePercent,
            FullWoundTotalDamageMultiplier, NonHeartDecayDuration,
            LightBleedDamageMultiplier, HeavyBleedDamageMultiplier,
            HeadFullDamageDepth, HeadDamageFloor, ChestFullDamageDepth,
            ChestDamageFloor;
        internal static ConfigEntry<float> BoneEspOpacity, HeartEspOpacity,
            BrainEspOpacity, RibcageEspOpacity;
        internal static ConfigEntry<float> OneBlackedRetention, TwoBlackedRetention,
            ThreePlusBlackedRetention;
        internal static ConfigEntry<float> ArmLinkageMultiplier, LegLinkageMultiplier,
            StomachLinkageMultiplier;
        internal static ConfigEntry<float> PlayerDamageMultiplier, ScavDamageMultiplier;
        internal static ConfigEntry<bool> PlayerBrainHitbox, PlayerHeartHitbox,
            PlayerCervicalSpineHitbox, PlayerThoracicSpineHitbox;
        internal static ConfigEntry<bool> ScavBrainHitbox, ScavHeartHitbox,
            ScavCervicalSpineHitbox, ScavThoracicSpineHitbox;
        internal static ConfigEntry<bool> PlayerBodyTrauma, ScavBodyTrauma;

        internal const float RapidClotDuration = 3f;
        internal const float RapidClotStrength = 0.15f;
        internal const float LightBleedDamagePerSecond = 1f;
        internal const float HeartBaseBleedDamagePerSecond = 10f;
        internal const float HeavyBloodEffectStrength = 6f;
        internal static OrganDefinition Heart { get; private set; }
        internal static OrganDefinition Brain { get; private set; }
        internal static OrganDefinition LowerBrain { get; private set; }
        internal static readonly Vector3 CervicalBrainEndOffset =
            new Vector3(78.87f, 42.25f, 9.39f) / 1000f;
        internal static readonly Vector3 CervicalChestEndOffset =
            new Vector3(2.35f, -42.25f, -32.86f) / 1000f;
        internal static readonly Vector3 SpineChestEndOffset =
            new Vector3(0f, -39.91f, -25.82f) / 1000f;
        internal static readonly Vector3 SpinePelvisEndOffset =
            new Vector3(-21.13f, -79.81f, -9.39f) / 1000f;

        internal static void InitializeOrganSettings(ConfigFile config)
        {
            BindGeneral(config);
            BindTargetRules(config);
            BindBleedBalance(config);
            BindLinkage(config);
            CreateOrganDefinitions();
        }

        private static void BindGeneral(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                Ui("Enable anatomical damage, wounds, bleeding, linkage, and fractures",
                    "01 - Feature Toggles", "Anatomical Trauma / Bleeding", 100));
            DirectDamagePercent = config.Bind("Damage", "DirectDamagePercentV2", 0.7537557f,
                Ui("Immediate bullet damage fraction before trauma", "02 - Global Damage",
                    "Direct Bullet Damage", 100, new AcceptableValueRange<float>(0f, 1f)));
            FullWoundTotalDamageMultiplier = config.Bind("Damage",
                "FullWoundTotalDamageMultiplier", 1.15f,
                Ui("Target total damage from a full-depth bullet wound after its natural bleed completes, relative to EFT post-armor damage",
                    "02 - Global Damage", "Full Wound Total Damage", 95,
                    new AcceptableValueRange<float>(1f, 1.5f)));
            IsEntireHeadFatal = config.Bind("Damage", "EntireHeadFatal", false,
                Ui("Treat every penetrating hit to the head as a fatal brain hit",
                    "02 - Global Damage", "Entire Head Is Fatal", 90));
            HeadFullDamageDepth = config.Bind("Damage", "HeadFullDamageDepth",
                0.15f, Ui("Fraction of head depth needed for full wound damage",
                    "02 - Global Damage", "Head Full-Damage Depth", 85,
                    new AcceptableValueRange<float>(0.01f, 1f)));
            HeadDamageFloor = config.Bind("Damage", "HeadDamageFloor", 0.75f,
                Ui("Minimum direct-damage fraction for a grazing head wound",
                    "02 - Global Damage", "Head Damage Floor", 84,
                    new AcceptableValueRange<float>(0f, 1f)));
            ChestFullDamageDepth = config.Bind("Damage", "ChestFullDamageDepth",
                0.35f, Ui("Fraction of chest depth needed for full wound damage",
                    "02 - Global Damage", "Chest Full-Damage Depth", 83,
                    new AcceptableValueRange<float>(0.01f, 1f)));
            ChestDamageFloor = config.Bind("Damage", "ChestDamageFloor", 0.50f,
                Ui("Minimum direct-damage fraction for a grazing chest wound",
                    "02 - Global Damage", "Chest Damage Floor", 82,
                    new AcceptableValueRange<float>(0f, 1f)));
            BloodEffects = config.Bind("Visuals", "WorldBloodEffects", true,
                Ui("Render procedural world-space blood particles from trauma wounds",
                    "01 - Feature Toggles", "Procedural World Blood", 20));
            ForceFragmentation = config.Bind("Debug", "ForceFragmentation", false,
                Ui("Force every eligible bullet wound to fragment for testing",
                    "09 - Debugging", "Force Fragmentation (100%)", 100));
            DebugEsp = config.Bind("Debug", "OrganESP", false,
                Ui("Render debug organ outlines", "09 - Debugging",
                    "Organ Hitbox ESP", 90));
            ShowCalibrationMannequin = config.Bind("Debug",
                "ShowCalibrationMannequin", false,
                Ui("Build a diagnostic mannequin from the closest entity's real skinned renderers and bind skeleton",
                    "09 - Debugging", "Show Calibration Mannequin", 89));
            DebugEspRange = config.Bind("Debug", "OrganESPRange", 100f,
                Ui("Maximum debug ESP rendering distance in metres", "09 - Debugging",
                    "ESP Culling Range", 80, new AcceptableValueRange<float>(5f, 500f)));
            HeartEspOpacity = BindEspOpacity(config, "HeartOpacity", 0.500939f,
                "Heart Opacity", 79);
            BrainEspOpacity = BindEspOpacity(config, "BrainOpacity", 0.400939f,
                "Brain Opacity", 78);
            BoneEspOpacity = BindEspOpacity(config, "BoneOpacity", 0.09624413f,
                "Bone Opacity", 77);
            RibcageEspOpacity = BindEspOpacity(config, "RibcageOpacity", 0.09647886f,
                "Ribcage Opacity", 76);
            DebugLogging = config.Bind("Debug", "HitLogging", false,
                Ui("Write TraumaCore diagnostic, warning, and error messages to the log",
                    "09 - Debugging", "Logging", 70));
        }

        private static void BindTargetRules(ConfigFile config)
        {
            PlayerDamageMultiplier = config.Bind("Target Balance", "PlayerDamageMultiplier", 1f,
                Ui("Global multiplier for all trauma damage applied to human players",
                    "02 - Global Damage", "Player Damage Multiplier", 90,
                    new AcceptableValueRange<float>(0f, 5f)));
            ScavDamageMultiplier = config.Bind("Target Balance", "ScavDamageMultiplier", 1f,
                Ui("Global multiplier for all trauma damage applied to AI/scavs",
                    "02 - Global Damage", "Scav / AI Damage Multiplier", 80,
                    new AcceptableValueRange<float>(0f, 5f)));

            PlayerBodyTrauma = BindTargetSystem(config, false, "CustomBodyTrauma", true,
                "Use Custom Body Trauma", "Use custom wounds, organs, linkage and fractures on players", 120);
            ScavBodyTrauma = BindTargetSystem(config, true, "CustomBodyTrauma", true,
                "Use Custom Body Trauma", "Use custom wounds, organs, linkage and fractures on AI/scavs", 120);

            PlayerBrainHitbox = BindHitbox(config, false, "Player Hitboxes", "Brain", false,
                "Brain (Fatal)", "Enable fatal brain hits on players", 100);
            PlayerHeartHitbox = BindHitbox(config, false, "Player Hitboxes", "Heart", false,
                "Heart (Delayed Fatal)", "Enable heart wounds on players", 90);
            PlayerCervicalSpineHitbox = BindHitbox(config, false, "Player Hitboxes",
                "CervicalSpine", false, "Cervical Spine (Fatal)",
                "Enable fatal upper-spine hits on players", 80);
            PlayerThoracicSpineHitbox = BindHitbox(config, false, "Player Hitboxes",
                "ThoracicSpine", true, "Thoracic Spine (Fracture)",
                "Enable chest-fracturing spine hits on players", 70);

            ScavBrainHitbox = BindHitbox(config, true, "Scav Hitboxes", "Brain", true,
                "Brain (Fatal)", "Enable fatal brain hits on AI/scavs", 100);
            ScavHeartHitbox = BindHitbox(config, true, "Scav Hitboxes", "Heart", true,
                "Heart (Delayed Fatal)", "Enable heart wounds on AI/scavs", 90);
            ScavCervicalSpineHitbox = BindHitbox(config, true, "Scav Hitboxes",
                "CervicalSpine", true, "Cervical Spine (Fatal)",
                "Enable fatal upper-spine hits on AI/scavs", 80);
            ScavThoracicSpineHitbox = BindHitbox(config, true, "Scav Hitboxes",
                "ThoracicSpine", true, "Thoracic Spine (Fracture)",
                "Enable chest-fracturing spine hits on AI/scavs", 70);
        }

        private static ConfigEntry<bool> BindTargetSystem(ConfigFile config, bool scav,
            string configKey, bool defaultValue, string displayName,
            string description, int order)
        {
            string configSection = scav ? "Scav Systems" : "Player Systems";
            string uiSection = scav
                ? "04 - Scav / AI Systems" : "03 - Player Systems";
            return config.Bind(configSection, configKey, defaultValue,
                Ui(description, uiSection, displayName, order));
        }

        private static ConfigEntry<bool> BindHitbox(ConfigFile config, bool scav,
            string configSection, string configKey, bool defaultValue,
            string displayName, string description, int order)
        {
            string uiSection = scav
                ? "04 - Scav / AI Systems" : "03 - Player Systems";
            return config.Bind(configSection, configKey, defaultValue,
                Ui(description, uiSection, displayName, order));
        }

        private static void BindBleedBalance(ConfigFile config)
        {
            NonHeartDecayDuration = config.Bind("Bleeding", "NaturalClotDurationV2", 30f,
                Ui("Seconds after the last non-heart hit until the bleed clots completely",
                    "05 - Bleed Balance", "Non-Heart Clot Time", 70,
                    new AcceptableValueRange<float>(3f, 120f)));
            LightBleedDamageMultiplier = config.Bind("Bleeding",
                "LightBleedDamageMultiplier", 1f,
                Ui("Multiplier applied to all light-bleed damage",
                    "05 - Bleed Balance", "Light Bleed Damage Multiplier", 60,
                    new AcceptableValueRange<float>(0f, 2f)));
            HeavyBleedDamageMultiplier = config.Bind("Bleeding",
                "HeavyBleedDamageMultiplier", 1f,
                Ui("Multiplier applied to all heavy-bleed damage, including heart hemorrhage",
                    "05 - Bleed Balance", "Heavy Bleed Damage Multiplier", 50,
                    new AcceptableValueRange<float>(0f, 2f)));
        }

        private static ConfigEntry<float> BindEspOpacity(ConfigFile config,
            string key, float defaultValue, string displayName, int order)
        {
            return config.Bind("Visuals", key, defaultValue,
                Ui("Opacity used by the organ ESP renderer",
                    "09 - Debugging", displayName, order,
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        internal static float GetBleedDecayArea(float duration)
        {
            duration = Mathf.Max(0.01f, duration);
            float rapidDuration = Mathf.Min(RapidClotDuration, duration);
            float rapidArea = rapidDuration * (1f + RapidClotStrength) * 0.5f;
            float slowArea = (duration - rapidDuration) *
                RapidClotStrength * 0.5f;
            return Mathf.Max(0.01f, rapidArea + slowArea);
        }

        private static void BindLinkage(ConfigFile config)
        {
            OneBlackedRetention = BindLinkageValue(config, "OneBlackedRetention", 0.9532864f,
                "1 Blacked Part Retention", "Damage retained after crossing one blacked part", 100, 1f);
            TwoBlackedRetention = BindLinkageValue(config, "TwoBlackedRetention", 0.801878f,
                "2 Blacked Parts Retention", "Damage retained after crossing two blacked parts", 90, 1f);
            ThreePlusBlackedRetention = BindLinkageValue(config, "ThreePlusBlackedRetention", 0.501878f,
                "3+ Blacked Parts Retention", "Damage retained after crossing three or more blacked parts", 80, 1f);
            ArmLinkageMultiplier = BindLinkageValue(config, "ArmLinkageMultiplierV2", 0.20f,
                "Arm Linkage", "Multiplier for damage shared or bypassed outward from an arm", 70, 2f);
            LegLinkageMultiplier = BindLinkageValue(config, "LegLinkageMultiplier", 1f,
                "Leg Linkage", "Multiplier for damage shared or bypassed outward from a leg", 60, 2f);
            StomachLinkageMultiplier = BindLinkageValue(config, "StomachLinkageMultiplier", 0.75f,
                "Stomach Linkage", "Multiplier for damage shared or bypassed outward from the stomach", 50, 2f);
        }

        private static ConfigEntry<float> BindLinkageValue(ConfigFile config, string key,
            float defaultValue, string displayName, string description, int order, float maximum)
        {
            return config.Bind("Damage Linkage", key, defaultValue,
                Ui(description, "06 - Damage Linkage", displayName, order,
                    new AcceptableValueRange<float>(0f, maximum)));
        }

        private static void CreateOrganDefinitions()
        {
            Heart = new OrganDefinition("HEART", OrganAnchor.Chest,
                OrganShape.Ellipsoid,
                HeartOffset + Vector3.forward * HeartSize.z *
                    (HeartForwardOffsetFraction + HeartSizeScale * 0.1f),
                HeartSize * HeartSizeScale, HeartRotation,
                new Color(1f, 0.1f, 0.15f, 0.95f));
            CreateBrainDefinitions();
        }

        private static void CreateBrainDefinitions()
        {
            Brain = new OrganDefinition("BRAIN 1", OrganAnchor.Head, OrganShape.Ellipsoid,
                AnatomyPoseSystem.Skull1CenterOffset,
                AnatomyPoseSystem.Skull1Size * 0.95f,
                Vector3.zero,
                new Color(1f, 0.2f, 0.8f, 0.95f));
            LowerBrain = new OrganDefinition("BRAIN 2", OrganAnchor.Head, OrganShape.Ellipsoid,
                Skull2Offset, Skull2Size * 0.9f,
                new Vector3(0f, 0f, -90f),
                new Color(0.75f, 0.12f, 1f, 0.95f));
        }

        private const float HeartSizeScale = 1.025f;
        private const float HeartForwardOffsetFraction = 0.025f;
        private static readonly Vector3 HeartOffset =
            new Vector3(-0.0136177f, 0.04238122f, 0.05963286f);
        private static readonly Vector3 HeartSize =
            new Vector3(0.09861502f, 0.1299765f, 0.09767605f);
        private static readonly Vector3 HeartRotation =
            new Vector3(0f, 0f, -25f);

        // Skull 2 is rotated -90 degrees around its local Z axis. Its head-bone
        // coordinates are up/down (X), forward/back (Y), and side-to-side (Z).
        internal static readonly Vector3 Skull2Offset =
            new Vector3(-52.01f, 22.28f, -1.9f) / 1000f;
        internal static readonly Vector3 Skull2Size =
            new Vector3(185f, 116.46f, 117.1f) / 1000f;

        private static ConfigDescription Ui(string description, string category,
            string displayName, int order, AcceptableValueBase acceptable = null)
        {
            return new ConfigDescription(description, acceptable,
                new ConfigurationManagerAttributes
                { Category = category, DispName = displayName, Order = order });
        }

        internal static TargetRules GetTargetRules(Player player)
        {
            if (player == null) return default;
            return player.IsAI
                ? new TargetRules(ScavDamageMultiplier.Value, ScavBodyTrauma.Value,
                    ScavBrainHitbox.Value, ScavHeartHitbox.Value,
                    ScavCervicalSpineHitbox.Value, ScavThoracicSpineHitbox.Value)
                : new TargetRules(PlayerDamageMultiplier.Value, PlayerBodyTrauma.Value,
                    PlayerBrainHitbox.Value, PlayerHeartHitbox.Value,
                    PlayerCervicalSpineHitbox.Value, PlayerThoracicSpineHitbox.Value);
        }
    }
}
