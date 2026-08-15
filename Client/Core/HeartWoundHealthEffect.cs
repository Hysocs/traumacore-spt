using System.Collections.Generic;
using EFT.HealthSystem;

namespace TraumaCore
{
    internal interface IHeartWound : IHealthEffect, IEffectTriggersUIPanel { }

    internal sealed class HeartWoundHealthEffect : ActiveHealthController.Effect,
        IHeartWound
    {
        public HeartWoundHealthEffect() { }

        public override float DefaultBuildUpTime { get { return 0f; } }
        public override float DefaultResidueTime { get { return 0f; } }

        public override EffectDescription[] DisplayableVariations
        {
            get
            {
                return new[]
                {
                    new EffectDescription(this, true,
                        new List<SimpleBuffDescription>
                        {
                            new SimpleBuffDescription("HEART WOUND"),
                            new SimpleBuffDescription("Catastrophic internal bleeding")
                        })
                };
            }
        }

        internal static void EnsureIconRegistered()
        {
            try
            {
                if (EFTHardSettings.Instance == null ||
                    EFTHardSettings.Instance.StaticIcons == null) return;
                var sprites = EFTHardSettings.Instance.StaticIcons.EffectIcons;
                sprites.EffectIcons[typeof(IHeartWound)] =
                    EffectIconLoader.LoadEffectIcon("effect_heart_wound.png") ??
                    sprites.HeavyBleeding;
            }
            catch { }
        }
    }
}
