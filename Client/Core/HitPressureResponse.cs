using EFT;
using EFT.HealthSystem;
using TraumaCore.Patches.HealthEffects;
using TraumaCore.Visuals;

namespace TraumaCore
{
    internal static class HitPressureResponse
    {
        private const float PainDurationSeconds = 1.5f;

        internal static HitPressureApplication Apply(
            ActiveHealthController healthController,
            EBodyPart bodyPart)
        {
            float strength = HitPressureVignette.ApplyHitStack();
            bool isHealthEffectApplied = ApplyNativePain(
                healthController, bodyPart, strength);
            return new HitPressureApplication(
                strength,
                isHealthEffectApplied);
        }

        private static bool ApplyNativePain(
            ActiveHealthController healthController,
            EBodyPart bodyPart,
            float strength)
        {
            if (healthController == null || !healthController.IsAlive)
                return false;

            EBodyPart effectBodyPart = bodyPart == EBodyPart.Common
                ? EBodyPart.Chest
                : bodyPart;
            ActiveHealthController.Pain pain =
                healthController.FindExistingEffect<
                    ActiveHealthController.Pain>(effectBodyPart);
            if (pain == null)
                pain = healthController.AddEffect<ActiveHealthController.Pain>(
                    effectBodyPart, 0f, PainDurationSeconds, 0f,
                    strength);
            else
            {
                pain.AddWorkTime(PainDurationSeconds, true);
                if (strength > pain.Strength)
                    pain.SetStrength(strength);
            }
            if (pain == null)
                return false;
            NativeEffectLabels.MarkImpactShock(pain, PainDurationSeconds);
            return true;
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
