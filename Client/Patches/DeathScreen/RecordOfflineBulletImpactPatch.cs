using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class RecordOfflineBulletImpactPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            BulletImpactPatchTarget.FindApplyShot(typeof(Player));

        [PatchPrefix]
        private static void CaptureOfflineBulletImpact(
            Player __instance,
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

    internal static class BulletImpactPatchTarget
    {
        internal static MethodBase FindApplyShot(System.Type playerType) =>
            AccessTools.Method(
                playerType,
                nameof(Player.ApplyShot),
                new[]
                {
                    typeof(DamageInfo),
                    typeof(EBodyPart),
                    typeof(EBodyPartColliderType),
                    typeof(EArmorPlateCollider),
                    typeof(ShotId)
                });
    }
}
