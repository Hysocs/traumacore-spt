using System.Collections.Generic;
using EFT.HealthSystem;

namespace TraumaCore
{
    internal interface IBruised : IHealthEffect, IEffectTriggersUIPanel,
        IRestorable { }

    internal sealed class BruisedHealthEffect : ActiveHealthController.Effect, IBruised
    {
        public BruisedHealthEffect() { }

        public override float DefaultBuildUpTime { get { return 0f; } }
        public override float DefaultResidueTime { get { return 0f; } }

        public override EFT.Profile.HealthInfo.EffectInfo Store() =>
            new EFT.Profile.HealthInfo.EffectInfo { Time = TimeLeft };

        public override EffectDescription[] DisplayableVariations
        {
            get { return BuildDisplayableVariations(this); }
        }

        internal static EffectDescription[] BuildDisplayableVariations(
            IHealthEffect effect) =>
            new[]
            {
                new EffectDescription(effect, true, new List<SimpleBuffDescription>
                {
                    new SimpleBuffDescription("BRUISED"),
                    new SimpleBuffDescription("Reduced movement speed"),
                    new SimpleBuffDescription("Reduced stamina recovery")
                })
            };

        internal static void EnsureIconRegistered()
        {
            try
            {
                if (EFTHardSettings.Instance == null ||
                    EFTHardSettings.Instance.StaticIcons == null) return;
                var sprites = EFTHardSettings.Instance.StaticIcons.EffectIcons;
                sprites.EffectIcons[typeof(IBruised)] =
                    EffectIconLoader.LoadEffectIcon("effect_bruised.png") ??
                    sprites.Contusion;
            }
            catch { }
        }
    }
}
