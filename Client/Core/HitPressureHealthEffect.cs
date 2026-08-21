using System;
using System.Collections.Generic;
using EFT.HealthSystem;
using UnityEngine;

namespace TraumaCore
{
    internal interface IHitPressure : IHealthEffect, IEffectTriggersUIPanel,
        IRestorable { }

    internal sealed class HitPressureHealthEffect : ActiveHealthController.Effect,
        IHitPressure
    {
        internal const float DurationSeconds = 1.50f;

        public HitPressureHealthEffect() { }

        public override float DefaultBuildUpTime => 0f;
        public override float DefaultWorkTime => DurationSeconds;
        public override float DefaultResidueTime => 0f;

        public override EFT.Profile.HealthInfo.EffectInfo Store() =>
            new EFT.Profile.HealthInfo.EffectInfo { Time = TimeLeft };

        public override void CalculateCurrentStrength(float deltaTime = 0f)
        {
            if (State != EEffectState.Started)
            {
                base.CalculateCurrentStrength(deltaTime);
                return;
            }

            CurrentStrength = Strength * Mathf.Clamp01(
                CurStateTimeLeft / DurationSeconds);
        }

        public override EffectDescription[] DisplayableVariations =>
            BuildDisplayableVariations(this);

        internal static EffectDescription[] BuildDisplayableVariations(
            IHealthEffect effect) =>
            new[]
            {
                new EffectDescription(effect, true, new List<SimpleBuffDescription>
                {
                    new SimpleBuffDescription("IMPACT SHOCK"),
                    new SimpleBuffDescription("Peripheral vision darkened"),
                    new SimpleBuffDescription("Fades rapidly after the last hit")
                })
            };

        internal static bool Apply(
            ActiveHealthController healthController,
            EBodyPart bodyPart,
            float strength)
        {
            if (healthController == null || !healthController.IsAlive)
                return false;

            try
            {
                HitPressureHealthEffect effect =
                    healthController.FindExistingEffect<HitPressureHealthEffect>();
                if (effect == null)
                {
                    EBodyPart effectBodyPart = bodyPart == EBodyPart.Common
                        ? EBodyPart.Chest
                        : bodyPart;
                    effect = healthController.AddEffect<HitPressureHealthEffect>(
                        effectBodyPart,
                        0f,
                        DurationSeconds,
                        0f,
                        strength);
                    return effect != null;
                }

                effect.SetStrength(strength);
                effect.AddWorkTime(DurationSeconds, true);
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"[HitPressure] Failed to apply health effect to {bodyPart}: " +
                    exception);
                return false;
            }
        }

        internal static void EnsureIconRegistered()
        {
            try
            {
                if (EFTHardSettings.Instance?.StaticIcons == null)
                    return;

                var effectIcons = EFTHardSettings.Instance.StaticIcons.EffectIcons;
                effectIcons.EffectIcons[typeof(IHitPressure)] =
                    EffectIconLoader.LoadEffectIcon("effect_bruised.png") ??
                    effectIcons.Contusion;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    "[HitPressure] Could not register the health-effect icon: " +
                    exception.Message);
            }
        }
    }
}
