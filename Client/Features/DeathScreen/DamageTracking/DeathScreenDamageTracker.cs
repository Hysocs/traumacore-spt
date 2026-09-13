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
        private const int MaximumImpactsPerBodyPart = 64;
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
            DamageInfo damageInfo)
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

            recordedDamage.DirectDamage += healthLost;
            recordedDamage.LastDirectType = damageType;
        }

        internal static void CaptureBulletImpact(
            Profile profile,
            Player victim,
            EBodyPart bodyPart,
            DamageInfo damageInfo,
            int projectileIndex = int.MinValue)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id) ||
                victim == null || damageInfo.Damage <= 0f ||
                !damageInfo.DamageType.IsWeaponInduced())
                return;

            BodyPartDamageRecord recordedDamage = FindOrCreateBodyPartDamage(
                profile.Id,
                bodyPart);

            Transform anchor = BodyPartAnchorResolver.FindImpactAnchor(victim, bodyPart, damageInfo.HitPoint);
            if (anchor == null)
            {
                if (OrganSystem.DebugLogging.Value)
                    TraumaLog.Warning(
                        $"[DeathScreenHitMarkers] Could not capture {bodyPart} impact: " +
                        "live bone anchor missing");
                return;
            }

            Vector3 localPoint = anchor.InverseTransformPoint(damageInfo.HitPoint);
            bool isDuplicate = false;
            for (int index = 0; index < recordedDamage.Impacts.Count; index++)
            {
                BulletImpactRecord impact = recordedDamage.Impacts[index];
                if (impact.FireIndex != damageInfo.FireIndex)
                    continue;
                bool isSameProjectile = projectileIndex != int.MinValue
                    ? impact.ProjectileIndex == projectileIndex
                    : impact.AnchorName == anchor.name &&
                        (impact.LocalPoint - localPoint).sqrMagnitude < 0.000001f;
                if (!isSameProjectile)
                    continue;

                isDuplicate = true;
                break;
            }
            if (!isDuplicate)
            {
                int sequence = ++_nextImpactSequence;
                recordedDamage.Impacts.Add(new BulletImpactRecord(
                    localPoint,
                    anchor.InverseTransformDirection(
                        damageInfo.Direction).normalized,
                    0f,
                    false,
                    damageInfo.DamageType,
                    damageInfo.FireIndex,
                    projectileIndex,
                    sequence,
                    default,
                    default,
                    default, anchor.name));
                if (recordedDamage.Impacts.Count > MaximumImpactsPerBodyPart)
                    recordedDamage.Impacts.RemoveAt(0);
                LatestImpactSequenceByProfileId[profile.Id] = sequence;
                recordedDamage.DirectHits++;
                if (OrganSystem.DebugLogging.Value)
                    TraumaLog.Info(
                        $"[DeathScreenHitMarkers] Captured {bodyPart} impact " +
                        $"fireIndex={damageInfo.FireIndex}, anchor={anchor.name}, " +
                        $"local=({localPoint.x:F3}, {localPoint.y:F3}, {localPoint.z:F3})");
            }
        }

        internal static void CaptureTrajectory(
            Profile profile,
            Player victim,
            TraumaController.ImpactCapture capture)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Id) || victim == null)
                return;

            BodyPartDamageRecord damage = FindOrCreateBodyPartDamage(profile.Id,
                capture.BodyPart);

            Transform anchor = BodyPartAnchorResolver.FindImpactAnchor(victim, capture.BodyPart, capture.HitPoint);
            if (anchor == null)
                return;

            Vector3 localEntry = anchor.InverseTransformPoint(capture.HitPoint);
            int closestIndex = -1;
            float closestDistance = float.MaxValue;
            for (int index = 0; index < damage.Impacts.Count; index++)
            {
                if (damage.Impacts[index].FireIndex != capture.FireIndex ||
                    damage.Impacts[index].ProjectileIndex != capture.ProjectileIndex ||
                    damage.Impacts[index].AnchorName != anchor.name)
                    continue;
                float distance = (damage.Impacts[index].LocalPoint - localEntry).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;
                closestDistance = distance;
                closestIndex = index;
            }
            Vector3 localDirection =
                anchor.InverseTransformDirection(capture.Direction).normalized;
            HitPenetrationRecord penetration = new HitPenetrationRecord(
                capture.Wound);
            WoundTrajectory trajectory = WoundTrajectory.Create(capture.Wound,
                capture.Direction).ToLocal(anchor);
            HitAnatomyRecord anatomy = new HitAnatomyRecord(anchor, capture);
            if (closestIndex < 0 || closestDistance > 0.01f)
            {
                int sequence = ++_nextImpactSequence;
                damage.Impacts.Add(new BulletImpactRecord(
                    localEntry,
                    localDirection,
                    Mathf.Max(0f, capture.Wound.TraveledDepth),
                    capture.Wound.PassedThrough,
                    capture.DamageType,
                    capture.FireIndex,
                    capture.ProjectileIndex,
                    sequence,
                    penetration,
                    trajectory,
                    anatomy, anchor.name));
                if (damage.Impacts.Count > MaximumImpactsPerBodyPart)
                    damage.Impacts.RemoveAt(0);
                LatestImpactSequenceByProfileId[profile.Id] = sequence;
                damage.DirectHits++;
                return;
            }

            BulletImpactRecord impact = damage.Impacts[closestIndex];
            damage.Impacts[closestIndex] = new BulletImpactRecord(
                localEntry,
                localDirection,
                Mathf.Max(0f, capture.Wound.TraveledDepth),
                capture.Wound.PassedThrough,
                impact.DamageType,
                impact.FireIndex,
                impact.ProjectileIndex,
                impact.Sequence,
                penetration,
                trajectory,
                anatomy, anchor.name);
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
            public readonly string AnchorName;
            public readonly Vector3 LocalPoint;
            public readonly Vector3 LocalDirection;
            public readonly float TraveledDepth;
            public readonly bool PassedThrough;
            public readonly EDamageType DamageType;
            public readonly int FireIndex;
            public readonly int ProjectileIndex;
            public readonly int Sequence;
            public readonly HitPenetrationRecord Penetration;
            public readonly WoundTrajectory Trajectory;
            public readonly HitAnatomyRecord Anatomy;

            public bool HasWoundTrajectory =>
                Trajectory.HasPath;

            public BulletImpactRecord(
                Vector3 localPoint,
                Vector3 localDirection,
                float traveledDepth,
                bool passedThrough,
                EDamageType damageType,
                int fireIndex,
                int projectileIndex,
                int sequence,
                HitPenetrationRecord penetration,
                WoundTrajectory trajectory,
                HitAnatomyRecord anatomy, string anchorName = null)
            {
                LocalPoint = localPoint;
                LocalDirection = localDirection;
                TraveledDepth = traveledDepth;
                PassedThrough = passedThrough;
                DamageType = damageType;
                FireIndex = fireIndex;
                ProjectileIndex = projectileIndex;
                Sequence = sequence;
                Penetration = penetration;
                Trajectory = trajectory;
                Anatomy = anatomy;
                AnchorName = anchorName;
            }
        }

        internal readonly struct HitAnatomyRecord
        {
            internal readonly Vector3 LocalSurfaceNormal;
            internal readonly Vector3 LocalIntersection;
            internal readonly Vector3 LocalBoneIntersection;
            internal readonly bool Heart;
            internal readonly bool Brain;
            internal readonly bool CervicalSpine;
            internal readonly bool ThoracicSpine;
            internal readonly bool Ribcage;
            internal readonly bool Skull;
            internal readonly bool LimbBone;

            internal bool HasCustomHit => Heart || Brain || CervicalSpine ||
                ThoracicSpine || Ribcage || Skull || LimbBone;

            internal HitAnatomyRecord(Transform anchor,
                TraumaController.ImpactCapture capture)
            {
                LocalSurfaceNormal = anchor.InverseTransformDirection(capture.HitNormal).normalized;
                LocalIntersection = anchor.InverseTransformPoint(
                    capture.Intersection);
                LocalBoneIntersection = anchor.InverseTransformPoint(
                    capture.BoneIntersection);
                Heart = capture.Heart;
                Brain = capture.Brain;
                CervicalSpine = capture.CervicalSpine;
                ThoracicSpine = capture.ThoracicSpine;
                Ribcage = capture.Ribcage;
                Skull = capture.Skull;
                LimbBone = capture.Bone;
            }
        }

        internal readonly struct HitPenetrationRecord
        {
            internal readonly float TissueThickness;
            internal readonly float PenetrationDepth;
            internal readonly float ReferenceThickness;
            internal readonly float ImpactVelocity;
            internal readonly float BulletDiameter;
            internal readonly float WoundScore;
            internal readonly float DepthRatio;
            internal readonly float BleedDamageMultiplier;
            internal readonly float BleedDurationMultiplier;
            internal readonly string PenetrationModel;
            internal readonly string DepthReferenceName;
            internal readonly BleedType BleedType;

            internal HitPenetrationRecord(WoundBallistics wound)
            {
                TissueThickness = wound.TissueThickness;
                PenetrationDepth = wound.PenetrationDepth;
                ReferenceThickness = wound.ReferenceThickness;
                ImpactVelocity = wound.ImpactVelocity;
                BulletDiameter = wound.BulletDiameter;
                WoundScore = wound.WoundScore;
                DepthRatio = wound.DepthRatio;
                BleedDamageMultiplier = wound.BleedDamageMultiplier;
                BleedDurationMultiplier = wound.BleedDurationMultiplier;
                PenetrationModel = wound.PenetrationModel;
                DepthReferenceName = wound.DepthReferenceName;
                BleedType = wound.BleedType;
            }
        }
    }

}
