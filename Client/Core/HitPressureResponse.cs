using EFT;
using EFT.HealthSystem;
using TraumaCore.Visuals;

namespace TraumaCore
{
    internal static class HitPressureResponse
    {
        internal static HitPressureApplication Apply(
            ActiveHealthController healthController,
            EBodyPart bodyPart)
        {
            float strength = HitPressureVignette.ApplyHitStack();
            bool isHealthEffectApplied = HitPressureHealthEffect.Apply(
                healthController,
                bodyPart,
                strength);
            return new HitPressureApplication(
                strength,
                isHealthEffectApplied);
        }
    }

    internal readonly struct HitPressureApplication
    {
        internal readonly float Strength;
        internal readonly bool IsHealthEffectApplied;

        internal HitPressureApplication(
            float strength,
            bool isHealthEffectApplied)
        {
            Strength = strength;
            IsHealthEffectApplied = isHealthEffectApplied;
        }
    }
}
