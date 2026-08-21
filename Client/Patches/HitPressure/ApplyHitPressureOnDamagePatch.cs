using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.HitPressure
{
    public sealed class ApplyHitPressureOnDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(Player),
                nameof(Player.ReceiveDamage),
                new[]
                {
                    typeof(float),
                    typeof(EBodyPart),
                    typeof(EDamageType),
                    typeof(float),
                    typeof(MaterialType)
                });

        [PatchPostfix]
        private static void PatchPostfix(
            Player __instance,
            float damage,
            EBodyPart part,
            EDamageType type,
            float absorbed)
        {
            if (__instance == null ||
                !__instance.IsYourPlayer ||
                type.IsSelfInflicted() ||
                damage + absorbed <= 0f)
                return;

            HitPressureApplication application = HitPressureResponse.Apply(
                __instance.ActiveHealthController,
                part);

            if (OrganSystem.DebugLogging.Value)
            {
                TraumaLog.Info(
                    $"[HitPressure] {part} {type}: body={damage:0.##}, " +
                    $"armor={absorbed:0.##}, strength={application.Strength:P0}, " +
                    $"healthEffect={(application.IsHealthEffectApplied ? "applied" : "failed")}");
            }
        }
    }
}
