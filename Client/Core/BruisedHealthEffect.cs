using System.Collections.Generic;
using EFT.HealthSystem;

namespace TraumaCore
{
    internal interface IBruised : IHealthEffect, IEffectTriggersUIPanel { }

    internal sealed class BruisedHealthEffect : ActiveHealthController.Effect, IBruised
    {
        public BruisedHealthEffect() { }

        public override float DefaultBuildUpTime { get { return 0f; } }
        public override float DefaultResidueTime { get { return 0f; } }

        public override EffectDescription[] DisplayableVariations
        {
            get
            {
                return new[]
                {
                    new EffectDescription(this, true, new List<SimpleBuffDescription>
                    {
                        new SimpleBuffDescription("BRUISED"),
                        new SimpleBuffDescription("Reduced movement speed"),
                        new SimpleBuffDescription("Reduced stamina recovery")
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
                sprites.EffectIcons[typeof(IBruised)] =
                    EffectIconLoader.LoadEffectIcon("effect_bruised.png") ??
                    sprites.Contusion;
            }
            catch { }
        }
    }
}
