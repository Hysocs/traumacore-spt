using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches.Trauma
{
    internal static class QuestKillAttribution
    {
        private static readonly Dictionary<string, WeaponHit> HitByVictimProfileId = new();

        internal static void Capture(Player victim, DamageInfo damageInfo,
            EBodyPart bodyPart)
        {
            if (victim == null || victim.Profile == null ||
                !damageInfo.HaveOwner ||
                damageInfo.Player.iPlayer.ProfileId == victim.Profile.Id ||
                !damageInfo.DamageType.IsWeaponInduced())
                return;

            float distance = Vector3.Distance(
                damageInfo.Player.iPlayer.Position,
                victim.Position);
            HitByVictimProfileId[victim.Profile.Id] = new WeaponHit(
                damageInfo.Player.iPlayer.ProfileId,
                damageInfo,
                bodyPart,
                distance);
        }

        internal static bool TryApply(string victimProfileId,
            ref DamageInfo damageInfo, ref EBodyPart bodyPart, ref float distance)
        {
            if (string.IsNullOrEmpty(victimProfileId) ||
                !HitByVictimProfileId.TryGetValue(victimProfileId, out WeaponHit hit))
                return false;

            string creditedAggressorId = damageInfo.HaveOwner
                ? damageInfo.Player.iPlayer.ProfileId
                : null;
            if (!string.IsNullOrEmpty(creditedAggressorId) &&
                creditedAggressorId != hit.AggressorProfileId)
                return false;

            damageInfo = hit.DamageInfo;
            bodyPart = hit.BodyPart;
            distance = hit.Distance;
            HitByVictimProfileId.Remove(victimProfileId);
            return true;
        }

        private readonly struct WeaponHit
        {
            internal readonly string AggressorProfileId;
            internal readonly DamageInfo DamageInfo;
            internal readonly EBodyPart BodyPart;
            internal readonly float Distance;

            internal WeaponHit(string aggressorProfileId, DamageInfo damageInfo,
                EBodyPart bodyPart, float distance)
            {
                AggressorProfileId = aggressorProfileId;
                DamageInfo = damageInfo;
                BodyPart = bodyPart;
                Distance = distance;
            }
        }
    }

    public sealed class CaptureQuestKillHitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.ApplyDamageInfo));

        [PatchPrefix]
        private static void PatchPrefix(Player __instance, DamageInfo damageInfo,
            EBodyPart bodyPartType)
        {
            QuestKillAttribution.Capture(__instance, damageInfo, bodyPartType);
        }
    }

    public sealed class ApplyQuestKillAttributionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(BaseStatisticsManager),
                nameof(BaseStatisticsManager.OnEnemyKill));

        [PatchPrefix]
        private static void PatchPrefix(ref DamageInfo damage,
            ref EBodyPart bodyPart, string playerProfileId, ref float distance)
        {
            QuestKillAttribution.TryApply(
                playerProfileId,
                ref damage,
                ref bodyPart,
                ref distance);
        }
    }
}
