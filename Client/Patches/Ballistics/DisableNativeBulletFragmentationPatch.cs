using System.Reflection;
using EFT.Ballistics;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.Ballistics
{
    public sealed class DisableNativeBulletFragmentationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Shot), nameof(Shot.IsBulletFragmented));

        [PatchPrefix]
        private static bool DisableRandomFragmentation(ref bool __result)
        {
            if (!OrganSystem.Enabled.Value ||
                !Plugin.EnableCustomFragmentation.Value)
                return true;

            __result = false;
            return false;
        }
    }
}
