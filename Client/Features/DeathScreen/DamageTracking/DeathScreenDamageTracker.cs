using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using UnityEngine;
using TraumaCore.Features.DeathScreen.HitMarkers;

namespace TraumaCore.Features.DeathScreen.DamageTracking
{
    internal static class DeathScreenDamageTracker
    {
        private static int _nextImpactSequence;
        private static readonly Dictionary<string, int> LatestImpactSequenceByProfileId =
            new();
        private static readonly Dictionary<string, Dictionary<EBodyPart, BodyPartDamageRecord>>
            DamageByBodyPartByProfileId = new();

        internal static void StartRaidTracking(Profile profile)
        {
            DamageByBodyPartByProfileId.Clear();
            _nextImpactSequence = 0;
            LatestImpactSequenceByProfileId.Clear();
            BodyPartAnchorResolver.ClearLiveAnchorCache();
            if (Singleton<GameWorld>.Instantiated &&
                profile != null &&
                !string.IsNullOrEmpty(profile.Id))
                DamageByBodyPartByProfileId[profile.Id] =
                    new Dictionary<EBodyPart, BodyPartDamageRecord>();
        }

        internal static void CaptureHealthLoss(
            Profile profile,
            EBodyPart bodyPart,
            float healthLost,
            DamageInfo damageInfo,
            IHealthController healthController)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id) || healthLost <= 0f)
                return;

            BodyPartDamageRecord recordedDamage = FindOrCreateBodyPartDamage(
                profile.Id,
                bodyPart);

            EDamageType damageType = damageInfo.DamageType;
            if (IsBleeding(damageType))
            {
                float nowSeconds = Time.unscaledTime;
                recordedDamage.BleedDamage += healthLost;
                if (damageType == EDamageType.HeavyBleeding)
                    recordedDamage.HeavyBleedDamage += healthLost;
                else
                    recordedDamage.LightBleedDamage += healthLost;
                recordedDamage.BleedTicks++;
                recordedDamage.FirstBleedTimeSeconds = recordedDamage.BleedTicks == 1
                    ? nowSeconds
                    : recordedDamage.FirstBleedTimeSeconds;
                recordedDamage.LastBleedTimeSeconds = nowSeconds;
                recordedDamage.HasHeavyBleed |= damageType == EDamageType.HeavyBleeding;
                return;
            }

            if (damageType.IsWeaponInduced() &&
                healthController is ActiveHealthController activeHealthController &&
                activeHealthController.Player != null)
            {
                CaptureBulletImpact(
                    profile,
                    activeHealthController.Player,
                    bodyPart,
                    damageInfo);
            }

            recordedDamage.DirectDamage += healthLost;
            recordedDamage.LastDirectType = damageType;
        }

        internal static void CaptureBulletImpact(
            Profile profile,
            Player victim,
            EBodyPart bodyPart,
            DamageInfo damageInfo)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id) ||
                victim == null || !damageInfo.DamageType.IsWeaponInduced())
                return;

            BodyPartDamageRecord recordedDamage = FindOrCreateBodyPartDamage(
                profile.Id,
                bodyPart);

            Transform anchor = BodyPartAnchorResolver.Find(
                victim.gameObject.transform,
                bodyPart);
            if (anchor == null)
            {
                if (OrganSystem.DebugLogging.Value)
                    Plugin.Log?.LogWarning(
                        $"[DeathScreenHitMarkers] Could not capture {bodyPart} impact: " +
                        "live bone anchor missing");
                return;
            }

            Vector3 localPoint = anchor.InverseTransformPoint(damageInfo.HitPoint);
            bool isDuplicate = false;
            for (int index = 0; index < recordedDamage.Impacts.Count; index++)
            {
                BulletImpactRecord impact = recordedDamage.Impacts[index];
                if (impact.FireIndex != damageInfo.FireIndex ||
                    (impact.LocalPoint - localPoint).sqrMagnitude >= 0.000001f)
                    continue;

                isDuplicate = true;
                break;
            }
            if (!isDuplicate)
            {
                int sequence = ++_nextImpactSequence;
                recordedDamage.Impacts.Add(new BulletImpactRecord(
                    localPoint,
                    damageInfo.DamageType,
                    damageInfo.FireIndex,
                    sequence));
                LatestImpactSequenceByProfileId[profile.Id] = sequence;
                recordedDamage.DirectHits++;
                if (OrganSystem.DebugLogging.Value)
                    Plugin.Log?.LogInfo(
                        $"[DeathScreenHitMarkers] Captured {bodyPart} impact " +
                        $"fireIndex={damageInfo.FireIndex}, anchor={anchor.name}, " +
                        $"local=({localPoint.x:F3}, {localPoint.y:F3}, {localPoint.z:F3})");
            }
        }

        internal static bool TryGetRecordedDamage(
            Profile profile,
            EBodyPart bodyPart,
            out BodyPartDamageRecord recordedDamage)
        {
            recordedDamage = null;
            return profile != null &&
                   !string.IsNullOrEmpty(profile.Id) &&
                   DamageByBodyPartByProfileId.TryGetValue(
                       profile.Id,
                       out Dictionary<EBodyPart, BodyPartDamageRecord> damageByBodyPart) &&
                   damageByBodyPart.TryGetValue(bodyPart, out recordedDamage);
        }

        private static BodyPartDamageRecord FindOrCreateBodyPartDamage(
            string profileId,
            EBodyPart bodyPart)
        {
            if (!DamageByBodyPartByProfileId.TryGetValue(
                profileId,
                out Dictionary<EBodyPart, BodyPartDamageRecord> damageByBodyPart))
            {
                damageByBodyPart = new Dictionary<EBodyPart, BodyPartDamageRecord>();
                DamageByBodyPartByProfileId.Add(profileId, damageByBodyPart);
            }

            if (damageByBodyPart.TryGetValue(
                bodyPart,
                out BodyPartDamageRecord recordedDamage))
                return recordedDamage;

            recordedDamage = new BodyPartDamageRecord();
            damageByBodyPart.Add(bodyPart, recordedDamage);
            return recordedDamage;
        }

        internal static bool IsBleeding(EDamageType damageType) =>
            damageType == EDamageType.LightBleeding ||
            damageType == EDamageType.HeavyBleeding;

        internal static int FindLatestImpactSequence(Profile profile) =>
            profile != null && !string.IsNullOrEmpty(profile.Id) &&
            LatestImpactSequenceByProfileId.TryGetValue(profile.Id, out int sequence)
                ? sequence
                : 0;

        internal sealed class BodyPartDamageRecord
        {
            public int DirectHits;
            public int BleedTicks;
            public float DirectDamage;
            public float BleedDamage;
            public float LightBleedDamage;
            public float HeavyBleedDamage;
            public float FirstBleedTimeSeconds;
            public float LastBleedTimeSeconds;
            public bool HasHeavyBleed;
            public EDamageType LastDirectType;
            public readonly List<BulletImpactRecord> Impacts = new();

            public float BleedDurationSeconds => BleedTicks == 0
                ? 0f
                : Mathf.Max(
                    1f / 60f,
                    LastBleedTimeSeconds - FirstBleedTimeSeconds + 1f / 60f);

            public float AverageBleedDamagePerSecond => BleedDurationSeconds > 0f
                ? BleedDamage / BleedDurationSeconds
                : 0f;
        }

        internal readonly struct BulletImpactRecord
        {
            public readonly Vector3 LocalPoint;
            public readonly EDamageType DamageType;
            public readonly int FireIndex;
            public readonly int Sequence;

            public BulletImpactRecord(
                Vector3 localPoint,
                EDamageType damageType,
                int fireIndex,
                int sequence)
            {
                LocalPoint = localPoint;
                DamageType = damageType;
                FireIndex = fireIndex;
                Sequence = sequence;
            }
        }
    }

}
