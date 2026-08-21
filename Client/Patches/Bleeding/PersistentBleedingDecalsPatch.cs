using System.Reflection;
using DeferredDecals;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.Bleeding
{
    public sealed class PersistentBleedingDecalsPatch : ModulePatch
    {
        private const int PersistentStaticDecalCapacity = 2000;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(DeferredDecalRenderer),
                nameof(DeferredDecalRenderer.Awake));

        [PatchPrefix]
        private static void PatchPrefix(DeferredDecalRenderer __instance)
        {
            __instance._maxDecals = PersistentStaticDecalCapacity;
            Plugin.Log.LogInfo(
                "[BloodDecals] Static decal capacity set to " +
                PersistentStaticDecalCapacity);
        }
    }
}
