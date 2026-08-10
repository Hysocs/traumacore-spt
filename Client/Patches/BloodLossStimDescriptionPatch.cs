using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;

namespace TraumaCore
{
    [HarmonyPatch(typeof(StimulatorHelper), nameof(StimulatorHelper.BuffName))]
    internal static class BloodLossStimDescriptionPatch
    {
        private static void Postfix(
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

    [HarmonyPatch(typeof(BuffDescription), MethodType.Constructor,
        new[] { typeof(IStimulatorBuff) })]
    internal static class BloodLossStimBuffRowPatch
    {
        private static void Postfix(IStimulatorBuff buff, BuffDescription __instance)
        {
            if (buff != null && buff.Settings != null &&
                buff.Settings.BuffType == EStimulatorBuffType.RemoveAllBloodLosses)
            {
                __instance.Text = "Clears existing non-heart bleeding on use. Reduces non-heart bleed damage by 50% for the remaining duration.";
            }
        }
    }

    [HarmonyPatch(typeof(HealthEffectsComponent), MethodType.Constructor,
        new[] { typeof(Item), typeof(IHealthEffectsComponentTemplate) })]
    internal static class BloodLossStimExamPanelPatch
    {
        private static void Postfix(Item item)
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
