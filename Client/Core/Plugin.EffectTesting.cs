using System;
using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using UnityEngine;

namespace TraumaCore
{
    public sealed partial class Plugin
    {
        private const float TestShotDamage = 50f;

        private void BindEffectTestButtons()
        {
            BindEffectTestButton("Apply Chest Bleed", 100,
                "Adds one light chest wound.",
                (trauma, player) => trauma.AddTreatableWound(
                    EBodyPart.Chest, BleedType.Light, 1f));
            BindEffectTestButton("Apply Heavy Chest Bleed", 95,
                "Adds one heavy chest wound for testing heavy-bleed treatment.",
                (trauma, player) => trauma.AddTreatableWound(
                    EBodyPart.Chest, BleedType.Heavy, 1f));
            BindEffectTestButton("Apply Heart Bleed", 90,
                "Adds one permanent heavy heart wound.",
                (trauma, player) => trauma.AddHeartWound(1f));
            BindEffectTestButton("Apply Face Bleed", 80,
                "Adds one light treatable face wound.",
                (trauma, player) => trauma.AddTreatableWound(
                    EBodyPart.Head, BleedType.Light, 1f));
            BindEffectTestButton("Apply Stomach Bleed", 70,
                "Adds one light stomach wound.",
                (trauma, player) => trauma.AddTreatableWound(EBodyPart.Stomach,
                    BleedType.Light, 1f));
            BindEffectTestButton("Apply Arm Bleed", 60,
                "Adds one light left-arm wound.",
                (trauma, player) => trauma.AddTreatableWound(EBodyPart.LeftArm,
                    BleedType.Light, 1f));
            BindEffectTestButton("Apply Leg Bleed", 50,
                "Adds one light left-leg wound.",
                (trauma, player) => trauma.AddTreatableWound(EBodyPart.LeftLeg,
                    BleedType.Light, 1f));
            BindEffectTestButton("Apply Bruised", 40,
                "Adds a 50-damage-equivalent armor bruise for 15 seconds.",
                (trauma, player) => trauma.AddBruise(GetTestDamage(player)));
            BindEffectTestButton("Apply Spine Fracture", 30,
                "Applies the native fracture effect to the chest.", ApplySpineFracture);
            BindEffectTestButton("Apply Hit Pressure", 25,
                "Adds one hit-pressure stack for visual testing.", ApplyHitPressure);
        }

        private static float GetTestDamage(Player player)
        { return TestShotDamage * OrganSystem.GetTargetRules(player).DamageMultiplier; }

        private static void ApplySpineFracture(TraumaController trauma, Player player)
        {
            ActiveHealthController health = player.ActiveHealthController;
            if (health.FindExistingEffect<IFracture>(EBodyPart.Chest) == null)
                health.AddEffect<ActiveHealthController.Fracture>(
                    EBodyPart.Chest, 0f, null, null, null);
        }

        private static void ApplyHitPressure(TraumaController trauma, Player player)
        {
            HitPressureApplication application = HitPressureResponse.Apply(
                player.ActiveHealthController,
                EBodyPart.Chest);
            TraumaLog.Info(
                $"[EffectTest] Hit pressure strength={application.Strength:P0}, " +
                $"healthEffect={(application.IsHealthEffectApplied ? "applied" : "failed")}");
        }

        private void BindEffectTestButton(string name, int order, string description,
            Action<TraumaController, Player> action)
        {
            ConfigurationManagerAttributes attributes = new ConfigurationManagerAttributes
            {
                Category = "10 - Effect Testing",
                DispName = name,
                Order = order,
                HideDefaultButton = true,
                HideSettingName = true,
                CustomDrawer = ignored => DrawEffectTestButton(name, action)
            };
            Config.Bind("Effect Testing", name, false,
                new ConfigDescription(description + " Available only while alive in a raid.",
                    null, attributes));
        }

        private void DrawEffectTestButton(string name, Action<TraumaController, Player> action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = IsLocalPlayerAlive();
            if (GUILayout.Button(name, GUILayout.ExpandWidth(true)))
                ApplyTestEffect(name, action);
            GUI.enabled = previousEnabled;
        }

        private bool IsLocalPlayerAlive()
        {
            return _localPlayer != null && _localPlayer.ActiveHealthController != null &&
                   _localPlayer.ActiveHealthController.IsAlive;
        }

        private void ApplyTestEffect(string name, Action<TraumaController, Player> action)
        {
            if (!IsLocalPlayerAlive()) return;
            TraumaController trauma = _localPlayer.GetComponent<TraumaController>();
            if (trauma == null) trauma = _localPlayer.gameObject.AddComponent<TraumaController>();
            trauma.InitializeForPlayer(_localPlayer);
            action(trauma, _localPlayer);
            TraumaLog.Info("[EffectTest] " + name + " applied to local player");
        }
    }
}
