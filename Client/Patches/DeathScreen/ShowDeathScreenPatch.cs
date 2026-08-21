using System;
using System.Reflection;
using EFT;
using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Features.DeathScreen.HitMarkers;

namespace TraumaCore.Patches.DeathScreen
{
    public sealed class ShowDeathScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(SessionResultExitStatus),
                nameof(SessionResultExitStatus.Show),
                new[]
                {
                    typeof(Profile),
                    typeof(PlayerVisualRepresentation),
                    typeof(ESideType),
                    typeof(ExitStatus),
                    typeof(TimeSpan),
                    typeof(IEftSession),
                    typeof(bool)
                });

        [PatchPostfix]
        private static void PatchPostfix(
            SessionResultExitStatus __instance,
            Profile activeProfile,
            ESideType side,
            ExitStatus exitStatus) =>
            DeathScreenHitMarkerPresenter.Show(
                __instance,
                activeProfile,
                side,
                exitStatus);
    }
}
