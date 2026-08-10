using System.Collections.Generic;
using EFT.HealthSystem;

namespace TraumaCore
{
    internal interface ISpinalFracture : IFracture { }

    internal sealed class SpinalFractureHealthEffect :
        ActiveHealthController.Fracture, ISpinalFracture
    {
        public SpinalFractureHealthEffect() { }

        public override EffectDescription[] DisplayableVariations
        {
            get
            {
                EffectDescription description = new EffectDescription(this, true,
                    new List<SimpleBuffDescription>
                    {
                        new SimpleBuffDescription("SPINAL FRACTURE")
                    });
                // The native base class identifies as IFracture. Give this
                // variation a distinct lookup key without changing mechanics.
                description.Type = typeof(ISpinalFracture);
                return new[] { description };
            }
        }

        internal static void EnsureIconRegistered()
        {
            try
            {
                if (EFTHardSettings.Instance == null ||
                    EFTHardSettings.Instance.StaticIcons == null) return;
                var sprites = EFTHardSettings.Instance.StaticIcons.EffectIcons;
                sprites.EffectIcons[typeof(ISpinalFracture)] =
                    EffectIconLoader.Load("effect_spinal_fracture.png") ??
                    sprites.Fracture;
            }
            catch { }
        }
    }
}
