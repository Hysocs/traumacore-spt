using System.Collections.Generic;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace TraumaCore.Patches
{
    [HarmonyPatch(typeof(HealthHelper), nameof(HealthHelper.GetDisplayVariation))]
    internal static class SpinalFractureDisplayPatch
    {
        private static void Postfix(IHealthEffect effect,
            ref EffectDescription[] __result)
        {
            if (!(effect is IFracture) ||
                (effect.BodyPart != EBodyPart.Chest &&
                 effect.BodyPart != EBodyPart.Stomach) ||
                __result == null)
                return;

            for (int i = 0; i < __result.Length; i++)
            {
                EffectDescription description = __result[i];
                if (description == null) continue;
                description.Buffs = new List<SimpleBuffDescription>
                {
                    new SimpleBuffDescription("SPINAL FRACTURE")
                };
            }
        }
    }
}
