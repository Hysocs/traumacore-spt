using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;

namespace TraumaCore.Patches
{
    [HarmonyPatch(typeof(MovementContext), nameof(MovementContext.ClampSpeed))]
    internal static class BruiseMovementPatch
    {
        private static void Postfix(Player ____player, ref float __result)
        {
            if (____player == null || __result <= 0f) return;
            TraumaController trauma = ____player.GetComponent<TraumaController>();
            if (trauma == null || trauma.BruiseStrength <= 0f) return;
            __result *= Mathf.Lerp(1f, 0.85f, trauma.BruiseStrength);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateSpeedLimitByHealth))]
    internal static class SpinalFractureMovementPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null || __instance.ActiveHealthController == null)
                return;
            ActiveHealthController health = __instance.ActiveHealthController;
            bool spinalFracture =
                health.FindExistingEffect<ISpinalFracture>(EBodyPart.Chest) != null ||
                health.FindExistingEffect<ISpinalFracture>(EBodyPart.Stomach) != null;
            if (!spinalFracture) return;

            // Match EFT's two-broken-legs behavior, including its painkiller bypass.
            if (health.FindExistingEffect<IPainKiller>() != null) return;
            __instance.MovementContext.EnableSprint(false);
            __instance.AddStateSpeedLimit(0.2f, Player.ESpeedLimit.HealthCondition);
        }
    }
}
