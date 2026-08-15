using System.Reflection;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches
{
    public sealed class BruiseMovementPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(MovementContext),
                nameof(MovementContext.ClampSpeed));

        [PatchPostfix]
        private static void PatchPostfix(Player ____player, ref float __result)
        {
            if (____player == null || __result <= 0f) return;
            TraumaController trauma = ____player.GetComponent<TraumaController>();
            if (trauma == null || trauma.BruiseStrength <= 0f) return;
            __result *= Mathf.Lerp(1f, 0.85f, trauma.BruiseStrength);
        }
    }

    public sealed class SpinalFractureMovementPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player),
                nameof(Player.UpdateSpeedLimitByHealth));

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (__instance == null || __instance.ActiveHealthController == null)
                return;
            ActiveHealthController health = __instance.ActiveHealthController;
            bool spinalFracture =
                health.FindExistingEffect<ISpinalFracture>(EBodyPart.Chest) != null ||
                health.FindExistingEffect<ISpinalFracture>(EBodyPart.Stomach) != null;
            if (!spinalFracture) return;

            if (health.FindExistingEffect<IPainKiller>() != null) return;
            __instance.MovementContext.EnableSprint(false);
            __instance.AddStateSpeedLimit(0.2f, Player.ESpeedLimit.HealthCondition);
        }
    }
}
