using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class BeginDamageTrackingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(HealthStatisticsManager),
                nameof(HealthStatisticsManager.BeginStatisticsSession));

        [PatchPostfix]
        private static void PatchPostfix(HealthStatisticsManager __instance) =>
            DeathScreenDamageTracker.StartRaidTracking(__instance.Profile);
    }
}
