using System;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches
{
    internal static class TraumaPresentationContext
    {
        [ThreadStatic] internal static bool InsideTraumaDamage;
        [ThreadStatic] internal static bool AllowPresentation;
    }

    public sealed class TraumaVoiceThrottlePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player),
                nameof(Player.OnAudioHealthApplyDamage));

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TraumaPresentationContext.InsideTraumaDamage ||
                   TraumaPresentationContext.AllowPresentation;
        }
    }

    public sealed class TraumaScreenBloodThrottlePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(EffectsController),
                nameof(EffectsController.OnHealthApplyDamage));

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            return !TraumaPresentationContext.InsideTraumaDamage ||
                   TraumaPresentationContext.AllowPresentation;
        }
    }
}
