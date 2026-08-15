using System.Collections.Generic;
using EFT;
using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches
{
    public sealed class SpinalFractureDisplayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(HealthHelper),
                nameof(HealthHelper.GetDisplayVariation));

        [PatchPostfix]
        private static void PatchPostfix(IHealthEffect effect,
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
