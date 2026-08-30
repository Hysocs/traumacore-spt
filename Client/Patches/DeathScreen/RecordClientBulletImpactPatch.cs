using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class RecordClientBulletImpactPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            BulletImpactPatchTarget.FindApplyShot(typeof(ClientPlayer));

        [PatchPrefix]
        private static void CaptureClientBulletImpact(
            ClientPlayer __instance,
            DamageInfo damageInfo,
            EBodyPart bodyPartType,
            ShotId shotId)
        {
            if (!Plugin.EnableDeathScreenReport.Value)
                return;

            DeathScreenDamageTracker.CaptureBulletImpact(
                __instance.Profile,
                __instance,
                bodyPartType,
                damageInfo,
                shotId._fragmentIndex);
        }
    }
}
