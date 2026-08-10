using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace TraumaCore.Patches
{
    [HarmonyPatch(typeof(ActiveHealthController),
        nameof(ActiveHealthController.TryToKillAfterDestroyPart))]
    internal static class VitalBodyPartDeathPatch
    {
        private static bool Prefix(ActiveHealthController __instance,
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
