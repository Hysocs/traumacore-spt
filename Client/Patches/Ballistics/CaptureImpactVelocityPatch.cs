using System.Reflection;
using EFT.Ballistics;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.Ballistics
{
    public sealed class CaptureImpactVelocityPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Shot), nameof(Shot.HandleCollision),
                new[] { typeof(float), typeof(UnityEngine.Vector3),
                    typeof(UnityEngine.Vector3) });

        [PatchPostfix]
        private static void CaptureImpactVelocity(Shot __instance)
        {
            WoundBallistics.CaptureImpactVelocity(__instance);
        }
    }
}
