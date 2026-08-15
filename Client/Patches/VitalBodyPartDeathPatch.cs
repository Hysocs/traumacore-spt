using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches
{
    public sealed class VitalBodyPartDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ActiveHealthController),
                nameof(ActiveHealthController.TryToKillAfterDestroyPart));

        [PatchPrefix]
        private static bool PatchPrefix(ActiveHealthController __instance,
            EBodyPart bodyPart, EDamageType damageType)
        {
            if (!OrganSystem.Enabled.Value || __instance == null ||
                __instance.Player == null ||
                !OrganSystem.GetTargetRules(__instance.Player).BodyTraumaEnabled)
                return true;
            if (bodyPart != EBodyPart.Head && bodyPart != EBodyPart.Chest)
                return true;

            __instance.Kill(damageType);
            return false;
        }
    }
}
