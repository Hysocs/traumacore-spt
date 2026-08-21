using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.UI.Health;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.Tooltips;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class RegisterBleedDamageTooltipPatch : ModulePatch
    {
        private static readonly FieldInfo BodyPartField =
            AccessTools.Field(typeof(DamagePanel), "_bodyPart");
        private static readonly FieldInfo HealthControllerField =
            AccessTools.Field(typeof(DamagePanel), "_healthController");

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(DamagePanel),
                nameof(DamagePanel.CreateDamageList));

        [PatchPostfix]
        private static void PatchPostfix(
            DamagePanel __instance,
            DamagePanel.BodyPartDamageList __result)
        {
            EBodyPart bodyPart = (EBodyPart)BodyPartField.GetValue(__instance);
            IHealthController healthController =
                HealthControllerField.GetValue(__instance) as IHealthController;

            BleedDamageTooltipBuilder.CaptureBleedSummary(
                __result,
                bodyPart,
                healthController);
        }
    }

}
