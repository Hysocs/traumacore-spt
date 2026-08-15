using BepInEx.Configuration;
using EFT;
using UnityEngine;

namespace TraumaCore
{
    internal static partial class OrganSystem
    {
        internal static ConfigEntry<bool> Enabled, DebugEsp, DebugLogging, BloodEffects;
        internal static ConfigEntry<float> DebugEspRange, DirectDamagePercent,
            NonHeartDecayDuration;
        internal static ConfigEntry<float> NormalBleedDivisor, HeadBleedDivisor,
            HeartBleedDivisor;
        internal static ConfigEntry<float> OneBlackedRetention, TwoBlackedRetention,
            ThreePlusBlackedRetention;
        internal static ConfigEntry<float> ArmLinkageMultiplier, LegLinkageMultiplier,
            StomachLinkageMultiplier;
        internal static ConfigEntry<float> PlayerDamageMultiplier, ScavDamageMultiplier;
        internal static ConfigEntry<bool> PlayerBrainHitbox, PlayerHeartHitbox,
            PlayerCervicalSpineHitbox, PlayerThoracicSpineHitbox;
        internal static ConfigEntry<bool> ScavBrainHitbox, ScavHeartHitbox,
            ScavCervicalSpineHitbox, ScavThoracicSpineHitbox;
        internal static ConfigEntry<bool> PlayerBodyTrauma, PlayerArmorPenetration,
            ScavBodyTrauma, ScavArmorPenetration;

        internal const float NonHeartMinimumStrength = 0.10f;
        internal const float BloodLossBlockerDamageMultiplier = 0.50f;
        internal static OrganDefinition Heart { get; private set; }
        internal static OrganDefinition Brain { get; private set; }
        internal static OrganDefinition LowerBrain { get; private set; }
        internal static readonly Vector3 CervicalBrainEndOffset =
            new Vector3(0.07887324f, 0.04225352f, 0.009389671f);
        internal static readonly Vector3 CervicalChestEndOffset =
            new Vector3(0.002347419f, -0.04225352f, -0.03286385f);
        internal static readonly Vector3 SpineChestEndOffset =
            new Vector3(-6.77723E-11f, -0.03990611f, -0.0258216f);
        internal static readonly Vector3 SpinePelvisEndOffset =
            new Vector3(-0.02112676f, -0.07981221f, -0.009389671f);

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
                Ui("Enable organ damage zones", "01 - General", "Enable Overhaul", 100));
            DirectDamagePercent = config.Bind("Damage", "DirectDamagePercentV2", 0.15f,
                Ui("Immediate bullet damage fraction before trauma", "02 - Global Damage",
                    "Direct Bullet Damage", 100, new AcceptableValueRange<float>(0f, 0.25f)));
            BloodEffects = config.Bind("Visuals", "WorldBloodEffects", true,
                Ui("Render procedural world-space blood particles from trauma wounds",
                    "07 - Debug & Visuals", "World Blood Effects", 100));
            DebugEsp = config.Bind("Debug", "OrganESP", false,
                Ui("Render debug organ outlines", "07 - Debug & Visuals", "Organ Hitbox ESP", 90));
            DebugEspRange = config.Bind("Debug", "OrganESPRange", 100f,
                Ui("Maximum debug ESP rendering distance in metres", "07 - Debug & Visuals",
                    "ESP Culling Range", 80, new AcceptableValueRange<float>(5f, 500f)));
            DebugLogging = config.Bind("Debug", "HitLogging", true,
                Ui("Log chest and organ hits", "07 - Debug & Visuals", "Hit Logging", 70));
        }

        private static void BindTargetRules(ConfigFile config)
        {
            PlayerDamageMultiplier = config.Bind("Target Balance", "PlayerDamageMultiplier", 0.25f,
                Ui("Global multiplier for all trauma damage applied to human players",
                    "02 - Global Damage", "Player Damage Multiplier", 90,
                    new AcceptableValueRange<float>(0f, 5f)));
            ScavDamageMultiplier = config.Bind("Target Balance", "ScavDamageMultiplier", 1f,
                Ui("Global multiplier for all trauma damage applied to AI/scavs",
                    "02 - Global Damage", "Scav / AI Damage Multiplier", 80,
                    new AcceptableValueRange<float>(0f, 5f)));

            PlayerBodyTrauma = BindTargetSystem(config, false, "CustomBodyTrauma", true,
                "Use Custom Body Trauma", "Use custom wounds, organs, linkage and fractures on players", 120);
            PlayerArmorPenetration = BindTargetSystem(config, false, "CustomArmorPenetration", true,
                "Use Custom Armor Penetration", "Use the custom binary penetration calculation on player armor", 110);
            ScavBodyTrauma = BindTargetSystem(config, true, "CustomBodyTrauma", true,
                "Use Custom Body Trauma", "Use custom wounds, organs, linkage and fractures on AI/scavs", 120);
            ScavArmorPenetration = BindTargetSystem(config, true, "CustomArmorPenetration", true,
                "Use Custom Armor Penetration", "Use the custom binary penetration calculation on AI/scav armor", 110);

            PlayerBrainHitbox = BindHitbox(config, false, "Player Hitboxes", "Brain", true,
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
            string uiSection = scav ? "04 - Scav / AI Hitboxes" : "03 - Player Hitboxes";
            return config.Bind(configSection, configKey, defaultValue,
                Ui(description, uiSection, displayName, order));
        }

        private static ConfigEntry<bool> BindHitbox(ConfigFile config, bool scav,
            string configSection, string configKey, bool defaultValue,
            string displayName, string description, int order)
        {
            string uiSection = scav ? "04 - Scav / AI Hitboxes" : "03 - Player Hitboxes";
            return config.Bind(configSection, configKey, defaultValue,
                Ui(description, uiSection, displayName, order));
        }

        private static void BindBleedBalance(ConfigFile config)
        {
            NormalBleedDivisor = config.Bind("Damage", "NormalBleedDamageDivisor", 6.5f,
                Ui("Bullet damage divisor for chest, stomach and limb bleed DPS",
                    "05 - Bleed Balance", "Body Bleed Divisor", 100,
                    new AcceptableValueRange<float>(1f, 30f)));
            HeadBleedDivisor = config.Bind("Damage", "HeadBleedDamageDivisor", 30f,
                Ui("Bullet damage divisor for non-brain head bleed DPS",
                    "05 - Bleed Balance", "Face Bleed Divisor", 90,
                    new AcceptableValueRange<float>(1f, 30f)));
            HeartBleedDivisor = config.Bind("Damage", "HeartBleedDamageDivisorV2", 1f,
                Ui("Bullet damage divisor for permanent heart bleed DPS",
                    "05 - Bleed Balance", "Heart Bleed Divisor", 80,
                    new AcceptableValueRange<float>(0.5f, 20f)));
            NonHeartDecayDuration = config.Bind("Bleeding", "NonHeartDecayDuration", 5f,
                Ui("Seconds after the last non-heart hit to decay to minimum strength",
                    "05 - Bleed Balance", "Non-Heart Bleed Decay", 70,
                    new AcceptableValueRange<float>(0.5f, 30f)));
        }

        private static void BindLinkage(ConfigFile config)
        {
            OneBlackedRetention = BindLinkageValue(config, "OneBlackedRetention", 0.85f,
                "1 Blacked Part Retention", "Damage retained after crossing one blacked part", 100, 1f);
            TwoBlackedRetention = BindLinkageValue(config, "TwoBlackedRetention", 0.60f,
                "2 Blacked Parts Retention", "Damage retained after crossing two blacked parts", 90, 1f);
            ThreePlusBlackedRetention = BindLinkageValue(config, "ThreePlusBlackedRetention", 0.30f,
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
            Heart = new OrganDefinition("HEART", OrganAnchor.Chest, OrganShape.Box,
                new Vector3(-0.0656f, -0.0014f, 0.05211f),
                new Vector3(0.099f, 0.121f, 0.088f), 0.71079f,
                new Color(1f, 0.1f, 0.15f, 0.95f));
            Brain = new OrganDefinition("BRAIN 1", OrganAnchor.Head, OrganShape.Ellipsoid,
                new Vector3(-0.1013986f, 0.0267507f, -0.0019f),
                new Vector3(0.114989f, 0.1787899f, 0.12747f),
                Vector3.zero,
                new Color(1f, 0.2f, 0.8f, 0.95f));
            LowerBrain = new OrganDefinition("BRAIN 2", OrganAnchor.Head, OrganShape.Ellipsoid,
                new Vector3(-0.07322957f, -0.001197184f, -0.0019f),
                new Vector3(0.124507f, 0.1056338f, 0.1098591f),
                new Vector3(0f, 0f, -90f),
                new Color(0.75f, 0.12f, 1f, 0.95f));
        }

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
                    ScavArmorPenetration.Value, ScavBrainHitbox.Value, ScavHeartHitbox.Value,
                    ScavCervicalSpineHitbox.Value, ScavThoracicSpineHitbox.Value)
                : new TargetRules(PlayerDamageMultiplier.Value, PlayerBodyTrauma.Value,
                    PlayerArmorPenetration.Value, PlayerBrainHitbox.Value, PlayerHeartHitbox.Value,
                    PlayerCervicalSpineHitbox.Value, PlayerThoracicSpineHitbox.Value);
        }
    }
}
