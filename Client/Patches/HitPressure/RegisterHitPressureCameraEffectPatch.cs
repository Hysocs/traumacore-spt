using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.HitPressure
{
    public sealed class RegisterHitPressureCameraEffectPatch : ModulePatch
    {
        private static readonly FieldInfo EffectAccumulatorsField =
            AccessTools.Field(typeof(EffectsController), "_effectAccumulators");

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EffectsController), nameof(EffectsController.Init));

        [PatchPostfix]
        private static void PatchPostfix(EffectsController __instance)
        {
            List<EffectsController.EffectAccumulator> effectAccumulators =
                EffectAccumulatorsField?.GetValue(__instance)
                    as List<EffectsController.EffectAccumulator>;
            if (effectAccumulators == null)
            {
                TraumaLog.Error(
                    "[HitPressure] EFT effect accumulators were not found");
                return;
            }

            foreach (EffectsController.EffectAccumulator accumulator in effectAccumulators)
            {
                if (!(accumulator is EffectsController.CC_FastVignetteAccumulator))
                    continue;

                Type[] acceptedEffectTypes =
                    accumulator.ValidTypes ?? Array.Empty<Type>();
                if (Array.IndexOf(acceptedEffectTypes, typeof(IHitPressure)) < 0)
                {
                    Array.Resize(
                        ref acceptedEffectTypes,
                        acceptedEffectTypes.Length + 1);
                    acceptedEffectTypes[acceptedEffectTypes.Length - 1] =
                        typeof(IHitPressure);
                    accumulator.ValidTypes = acceptedEffectTypes;
                }

                TraumaLog.Info(
                    "[HitPressure] Registered with EFT tunnel-vision renderer");
                return;
            }

            TraumaLog.Error(
                "[HitPressure] EFT tunnel-vision renderer was not found");
        }
    }
}
