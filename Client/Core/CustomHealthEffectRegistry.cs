using System;
using System.Linq;
using EFT;
using EFT.HealthSystem;

namespace TraumaCore
{
    internal static class CustomHealthEffectRegistry
    {
        private static bool _isRegistered;

        internal static void Register()
        {
            if (_isRegistered)
                return;

            RegisterEffect<ActiveHealthController, BruisedHealthEffect>();
            RegisterEffect<ActiveHealthController, HitPressureHealthEffect>();
            RegisterEffect<OfflineHealthController,
                OfflineEffects.BruisedHealthEffect>();
            RegisterEffect<OfflineHealthController,
                OfflineEffects.HitPressureHealthEffect>();
            _isRegistered = true;
        }

        private static void RegisterEffect<THealthController, TEffect>()
            where THealthController : IHealthController
            where TEffect : IHealthEffect
        {
            Type effectType = typeof(TEffect);
            Type[] registeredTypes =
                HealthHelper.EffectActivator<THealthController>._effectTypes;
            if (!registeredTypes.Contains(effectType))
                HealthHelper.EffectActivator<THealthController>._effectTypes =
                    registeredTypes.Concat(new[] { effectType }).ToArray();

            HealthHelper.EffectActivator<THealthController>._constructors.Remove(
                effectType.Name);
        }

        private static class OfflineEffects
        {
            internal sealed class BruisedHealthEffect :
                OfflineHealthController.Effect, IBruised
            {
                public BruisedHealthEffect() { }

                public override EffectDescription[] DisplayableVariations =>
                    TraumaCore.BruisedHealthEffect.BuildDisplayableVariations(this);

                public override Profile.HealthInfo.EffectInfo Store() =>
                    new Profile.HealthInfo.EffectInfo { Time = TimeLeft };
            }

            internal sealed class HitPressureHealthEffect :
                OfflineHealthController.Effect, IHitPressure
            {
                public HitPressureHealthEffect() { }

                public override EffectDescription[] DisplayableVariations =>
                    TraumaCore.HitPressureHealthEffect.BuildDisplayableVariations(this);

                public override Profile.HealthInfo.EffectInfo Store() =>
                    new Profile.HealthInfo.EffectInfo { Time = TimeLeft };
            }
        }
    }
}
