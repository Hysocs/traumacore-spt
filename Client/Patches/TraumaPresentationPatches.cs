using System;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;

namespace TraumaCore.Patches
{
    internal static class TraumaPresentationContext
    {
        [ThreadStatic] internal static bool InsideTraumaDamage;
        [ThreadStatic] internal static bool AllowPresentation;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnAudioHealthApplyDamage))]
    internal static class TraumaVoiceThrottlePatch
    {
        private static bool Prefix()
        {
            return !TraumaPresentationContext.InsideTraumaDamage ||
                   TraumaPresentationContext.AllowPresentation;
        }
    }

    [HarmonyPatch(typeof(EffectsController), nameof(EffectsController.OnHealthApplyDamage))]
    internal static class TraumaScreenBloodThrottlePatch
    {
        private static bool Prefix()
        {
            return !TraumaPresentationContext.InsideTraumaDamage ||
                   TraumaPresentationContext.AllowPresentation;
        }
    }
}
