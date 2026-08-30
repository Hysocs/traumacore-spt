using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class RecordHealthLossPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(HealthStatisticsManager),
                nameof(HealthStatisticsManager.OnHealthChanged),
                new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) });

        [PatchPrefix]
        private static void CaptureHealthLoss(
            HealthStatisticsManager __instance,
            EBodyPart bodyPart,
            float diff,
            DamageInfo damageInfo)
        {
            if (!Plugin.EnableDeathScreenReport.Value || diff >= 0f)
                return;

            DeathScreenDamageTracker.CaptureHealthLoss(
                __instance.Profile,
                bodyPart,
                -diff,
                damageInfo);
        }
    }
}
