using System;
using BepInEx.Configuration;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using UnityEngine;

namespace TraumaCore
{
    public sealed partial class Plugin
    {
        private const float TestShotDamage = 50f;
        private const float TestHitDamage = 5f;
        private static int _nextTestFireIndex = 100000;

        private void BindEffectTestButtons()
        {
            BindEffectTestButton("Apply Chest Bleed", 100,
                "Adds one 50-damage-equivalent chest wound.",
                (trauma, player) => trauma.AddChestWound(GetTestDamage(player)));
            BindEffectTestButton("Apply Heart Bleed", 90,
                "Adds one permanent 50-damage-equivalent heart wound.",
                (trauma, player) => trauma.AddHeartWound(GetTestDamage(player)));
            BindEffectTestButton("Apply Face Bleed", 80,
                "Adds one 50-damage-equivalent treatable face wound.",
                (trauma, player) => trauma.AddFaceWound(GetTestDamage(player)));
            BindEffectTestButton("Apply Stomach Bleed", 70,
                "Adds one 50-damage-equivalent stomach wound.",
                (trauma, player) => trauma.AddBodyWound(EBodyPart.Stomach, GetTestDamage(player)));
            BindEffectTestButton("Apply Arm Bleed", 60,
                "Adds one 50-damage-equivalent left-arm wound.",
                (trauma, player) => trauma.AddBodyWound(EBodyPart.LeftArm, GetTestDamage(player)));
            BindEffectTestButton("Apply Leg Bleed", 50,
                "Adds one 50-damage-equivalent left-leg wound.",
                (trauma, player) => trauma.AddBodyWound(EBodyPart.LeftLeg, GetTestDamage(player)));
            BindEffectTestButton("Apply Bruised", 40,
                "Adds a 50-damage-equivalent armor bruise for 15 seconds.",
                (trauma, player) => trauma.AddBruise(GetTestDamage(player)));
            BindEffectTestButton("Apply Spine Fracture", 30,
                "Applies the native fracture effect to the chest.", ApplySpineFracture);
            BindEffectTestButton("Apply Hit Pressure", 25,
                "Adds one hit-pressure stack for visual testing.", ApplyHitPressure);
            BindHitboxTestButtons();
        }

        private void BindHitboxTestButtons()
        {
            BindEffectTestButton("Inflict Head Hit", 20,
                "Inflicts and records a small test hit on the head.",
                (trauma, player) => InflictTestHit(player, EBodyPart.Head));
            BindEffectTestButton("Inflict Chest Hit", 19,
                "Inflicts and records a small test hit on the chest.",
                (trauma, player) => InflictTestHit(player, EBodyPart.Chest));
            BindEffectTestButton("Inflict Stomach Hit", 18,
                "Inflicts and records a small test hit on the stomach.",
                (trauma, player) => InflictTestHit(player, EBodyPart.Stomach));
            BindEffectTestButton("Inflict Left Arm Hit", 17,
                "Inflicts and records a small test hit on the left arm.",
                (trauma, player) => InflictTestHit(player, EBodyPart.LeftArm));
            BindEffectTestButton("Inflict Right Arm Hit", 16,
                "Inflicts and records a small test hit on the right arm.",
                (trauma, player) => InflictTestHit(player, EBodyPart.RightArm));
            BindEffectTestButton("Inflict Left Leg Hit", 15,
                "Inflicts and records a small test hit on the left leg.",
                (trauma, player) => InflictTestHit(player, EBodyPart.LeftLeg));
            BindEffectTestButton("Inflict Right Leg Hit", 14,
                "Inflicts and records a small test hit on the right leg.",
                (trauma, player) => InflictTestHit(player, EBodyPart.RightLeg));
        }

        private static void InflictTestHit(Player player, EBodyPart bodyPart)
        {
            ActiveHealthController healthController = player.ActiveHealthController;
            if (healthController == null || !healthController.IsAlive)
                return;

            Transform bodyPartAnchor =
                Features.DeathScreen.HitMarkers.BodyPartAnchorResolver.Find(
                    player.gameObject.transform,
                    bodyPart);
            if (bodyPartAnchor == null)
            {
                Log?.LogWarning(
                    $"[EffectTest] Cannot inflict {bodyPart} hit: bone anchor missing");
                return;
            }

            Vector3 localOffset = new Vector3(0.025f, 0f, 0.015f);
            DamageInfo testHit = new DamageInfo
            {
                DamageType = EDamageType.Bullet,
                Damage = TestHitDamage,
                HitPoint = bodyPartAnchor.TransformPoint(localOffset),
                HitNormal = Vector3.back,
                Direction = Vector3.forward,
                FireIndex = _nextTestFireIndex++
            };
            healthController.ApplyDamage(bodyPart, TestHitDamage, testHit);
            HitPressureResponse.Apply(healthController, bodyPart);
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
            Log?.LogInfo(
                $"[EffectTest] Hit pressure strength={application.Strength:P0}, " +
                $"healthEffect={(application.IsHealthEffectApplied ? "applied" : "failed")}");
        }

        private void BindEffectTestButton(string name, int order, string description,
            Action<TraumaController, Player> action)
        {
            ConfigurationManagerAttributes attributes = new ConfigurationManagerAttributes
            {
                Category = "08 - Effect Testing",
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
            Logger.LogInfo("[EffectTest] " + name + " applied to local player");
        }
    }
}
