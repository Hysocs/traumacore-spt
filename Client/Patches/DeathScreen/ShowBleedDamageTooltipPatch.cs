using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.UI.Health;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.Tooltips;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class ShowBleedDamageTooltipPatch : ModulePatch
    {
        private static readonly FieldInfo TooltipTextField =
            AccessTools.Field(typeof(DamageIcon), "_tooltipText");

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(DamageIcon),
                nameof(DamageIcon.Show),
                new[] { typeof(DamageStats.EDamageResult), typeof(List<DamageStats>) });

        [PatchPostfix]
        private static void PatchPostfix(
            DamageIcon __instance,
            List<DamageStats> damageList)
        {
            if (BleedDamageTooltipBuilder.TryBuildTooltip(
                damageList,
                out string tooltip))
                TooltipTextField.SetValue(__instance, tooltip);
        }
    }
}
