using System.Reflection;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.HealthEffects
{
    public sealed class BloodLossStimDescriptionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(StimulatorHelper),
                nameof(StimulatorHelper.BuffName));

        [PatchPostfix]
        private static void PatchPostfix(
            EffectsSettings.StimulatorSettings.StimulatorBuffSettings buffSettings,
            ref string __result)
        {
            if (buffSettings != null &&
                buffSettings.BuffType == EStimulatorBuffType.RemoveAllBloodLosses)
            {
                __result = "Removes all treatable bleeding upon use. Reduces treatable bleed damage by 50% for the remaining duration. Does not affect heart hemorrhage.";
            }
        }
    }

    public sealed class BloodLossStimBuffRowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Constructor(typeof(BuffDescription),
                new[] { typeof(IStimulatorBuff) });

        [PatchPostfix]
        private static void PatchPostfix(IStimulatorBuff buff,
            BuffDescription __instance)
        {
            if (buff != null && buff.Settings != null &&
                buff.Settings.BuffType == EStimulatorBuffType.RemoveAllBloodLosses)
            {
                __instance.Text = "Clears existing non-heart bleeding on use. Reduces non-heart bleed damage by 50% for the remaining duration.";
            }
        }
    }

    public sealed class BloodLossStimExamPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Constructor(typeof(HealthEffectsComponent), new[]
            {
                typeof(Item), typeof(IHealthEffectsComponentTemplate)
            });

        [PatchPostfix]
        private static void PatchPostfix(Item item)
        {
            if (item == null) return;

            ItemAttribute blocker = null;
            for (int i = 0; i < item.Attributes.Count; i++)
            {
                ItemAttribute attribute = item.Attributes[i];
                if (attribute is StimulatorBuffAttribute &&
                    attribute.Id.Equals(EStimulatorBuffType.RemoveAllBloodLosses))
                {
                    blocker = attribute;
                    break;
                }
            }
            if (blocker == null) return;

            for (int i = item.Attributes.Count - 1; i >= 0; i--)
            {
                ItemAttribute attribute = item.Attributes[i];
                if (attribute.Id.Equals(EDamageEffectType.LightBleeding) ||
                    attribute.Id.Equals(EDamageEffectType.HeavyBleeding))
                    item.Attributes.RemoveAt(i);
            }

            blocker.Name = "NON-HEART BLEED PROTECTION";
            blocker.DisplayNameFunc = () => "Non-heart bleed protection";
            blocker.FullStringValue = () =>
                "Clears existing non-heart bleeding on use and reduces non-heart bleed damage by 50% for the remaining duration.";
        }
    }
}
