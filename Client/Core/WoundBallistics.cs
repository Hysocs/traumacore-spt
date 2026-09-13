using Comfort.Common;
using System.Collections.Generic;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace TraumaCore
{
    internal enum BleedType
    {
        Light,
        Heavy
    }

    internal readonly struct WoundBallistics
    {
        internal readonly struct FragmentPath
        {
            internal readonly Vector3 StartPoint;
            internal readonly Vector3 Direction;
            internal readonly float TraveledDepth;
            internal readonly bool PassedThrough;

            internal FragmentPath(Vector3 startPoint, Vector3 direction,
                float traveledDepth, bool passedThrough)
            {
                StartPoint = startPoint;
                Direction = direction;
                TraveledDepth = traveledDepth;
                PassedThrough = passedThrough;
            }
        }

        private const float ColliderJoinTolerance = 0.02f;
        private const float DefaultFullDamageDepth = 0.75f;
        private const float DefaultDamageFloor = 0.35f;
        private const float MinimumEntryDamage = 0.25f;
        private const float MinimumEntryDuration = 0.40f;
        private const float MaximumExitDamage = 0.35f;
        private const float MaximumExitDuration = 0.20f;
        private const float MinimumTissuePenetration = 0.04f;
        private const float PenetrationPowerDepthScale = 0.0055f;
        private const float KineticDepthScale = 0.0032f;
        private const float DiameterWoundWeight = 0.55f;
        private const float DepthWoundWeight = 8f;
        private const float VelocityWoundWeight = 0.002f;
        private const float HeavyWoundThreshold = 8f;
        private const float FragmentationBaseDamageBonus = 0.15f;
        private const float FragmentPathDamageBonus = 0.025f;
        private const float PelletReferencePositionTolerance = 0.06f;
        private const float PelletReferenceDirectionDot = 0.995f;
        private const float BurstCacheDuration = 0.12f;
        private const float BurstPositionTolerance = 0.025f;
        private const float BurstDirectionDot = 0.998f;
        private static readonly List<ImpactVelocityCacheEntry>
            ImpactVelocityCache = new List<ImpactVelocityCacheEntry>(8);

        private readonly struct TissueInterval
        {
            internal readonly float Start;
            internal readonly float End;

            internal TissueInterval(float start, float end)
            {
                Start = start;
                End = end;
            }
        }

        private readonly struct BleedScaling
        {
            internal readonly float CenterDepthRatio;
            internal readonly float StrengthRatio;
            internal readonly float DamageMultiplier;
            internal readonly float DurationMultiplier;

            internal BleedScaling(float traveledDepth,
                float referenceThickness, float fullDamageDepth,
                bool passedThrough)
            {
                CenterDepthRatio = referenceThickness > 0.001f
                    ? Mathf.Clamp01(traveledDepth / referenceThickness)
                    : 1f;
                float fullBleedDepth = referenceThickness *
                    fullDamageDepth;
                StrengthRatio = fullBleedDepth > 0.001f
                    ? Mathf.Clamp01(traveledDepth / fullBleedDepth)
                    : 1f;
                DamageMultiplier = Mathf.Lerp(MinimumEntryDamage, 1f,
                    StrengthRatio) + (passedThrough
                        ? MaximumExitDamage * StrengthRatio : 0f);
                DurationMultiplier = Mathf.Lerp(MinimumEntryDuration, 1f,
                    StrengthRatio) + (passedThrough
                        ? MaximumExitDuration * StrengthRatio : 0f);
            }
        }

        private sealed class ImpactVelocityCacheEntry
        {
            internal string AmmoId;
            internal Vector3 Origin;
            internal Vector3 HitPoint;
            internal Vector3 Direction;
            internal float Velocity;
            internal float InitialVelocity;
            internal float CapturedAt;
            internal int CapturedFrame;
        }

        internal readonly Vector3 EntryPoint;
        internal readonly Vector3 ExitPoint;
        internal readonly EBodyPart BodyPart;
        internal readonly float TissueThickness;
        internal readonly float PenetrationDepth;
        internal readonly float ArmorPenetrationDepth;
        internal readonly float KineticPenetrationDepth;
        internal readonly float ImpactVelocity;
        internal readonly float BulletDiameter;
        internal readonly float WoundScore;
        internal readonly Vector3 ReferenceEntryPoint;
        internal readonly Vector3 ReferenceExitPoint;
        internal readonly float ReferenceThickness;
        internal readonly float TraveledDepth;
        internal readonly float CenterDepthRatio;
        internal readonly float DepthRatio;
        internal readonly float BleedDamageMultiplier;
        internal readonly float BleedDurationMultiplier;
        internal readonly string DepthReferenceName;
        internal readonly string PenetrationModel;
        internal readonly bool PassedThrough;
        internal readonly BleedType BleedType;
        internal readonly float FragmentationChance;
        internal readonly float FragmentationDepth;
        internal readonly FragmentPath[] FragmentPaths;

        internal bool HasFragmentation =>
            FragmentPaths != null && FragmentPaths.Length >= 2;

        internal float CalculateFragmentDamageBonus(float bulletDamage)
        {
            if (!HasFragmentation || bulletDamage <= 0f)
                return 0f;
            float damageBonusMultiplier = FragmentationBaseDamageBonus +
                FragmentPaths.Length * FragmentPathDamageBonus;
            return bulletDamage * damageBonusMultiplier;
        }

        internal float CalculateHeartBleedMultiplier(float heartEntryDistance,
            float heartExitDistance)
        {
            float heartThickness = Mathf.Max(0.001f,
                heartExitDistance - heartEntryDistance);
            float heartTravel = Mathf.Clamp(PenetrationDepth - heartEntryDistance,
                0f, heartThickness);
            float heartDepthRatio = Mathf.Clamp01(heartTravel / heartThickness);
            float diameterMultiplier = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(1f, BulletDiameter) / 7f), 0.75f, 1.5f);
            float velocityMultiplier = Mathf.Clamp(
                Mathf.Sqrt(Mathf.Max(1f, ImpactVelocity) / 700f), 0.75f, 1.3f);
            bool exitsHeart = PenetrationDepth >= heartExitDistance;
            float exitMultiplier = exitsHeart ? 1.25f : 1f;
            float fragmentationMultiplier = HasFragmentation &&
                FragmentationDepth <= heartExitDistance
                ? 1f + 0.1f * (FragmentPaths.Length - 1)
                : 1f;
            return Mathf.Lerp(0.65f, 1f, heartDepthRatio) *
                diameterMultiplier * velocityMultiplier * exitMultiplier *
                fragmentationMultiplier;
        }

        internal float DirectDamageMultiplier =>
            Mathf.Lerp(FindDamageFloor(BodyPart), 1f, DepthRatio);

        private WoundBallistics(EBodyPart bodyPart, Vector3 entryPoint,
            Vector3 exitPoint,
            float tissueThickness, float penetrationDepth, float impactVelocity,
            float bulletDiameter, float woundScore, bool passedThrough,
            BleedType bleedType, Vector3 referenceEntryPoint,
            Vector3 referenceExitPoint, float referenceThickness,
            string depthReferenceName, float armorPenetrationDepth,
            float kineticPenetrationDepth, string penetrationModel,
            float fragmentationChance = 0f,
            float fragmentationDepth = 0f,
            FragmentPath[] fragmentPaths = null)
        {
            BodyPart = bodyPart;
            EntryPoint = entryPoint;
            ExitPoint = exitPoint;
            TissueThickness = tissueThickness;
            PenetrationDepth = penetrationDepth;
            ArmorPenetrationDepth = armorPenetrationDepth;
            KineticPenetrationDepth = kineticPenetrationDepth;
            ImpactVelocity = impactVelocity;
            BulletDiameter = bulletDiameter;
            WoundScore = woundScore;
            ReferenceEntryPoint = referenceEntryPoint;
            ReferenceExitPoint = referenceExitPoint;
            ReferenceThickness = referenceThickness;
            TraveledDepth = FindTraveledDepth(penetrationDepth,
                tissueThickness);
            BleedScaling bleedScaling = new BleedScaling(TraveledDepth,
                referenceThickness, FindFullDamageDepth(bodyPart),
                passedThrough);
            CenterDepthRatio = bleedScaling.CenterDepthRatio;
            DepthRatio = bleedScaling.StrengthRatio;
            BleedDamageMultiplier = bleedScaling.DamageMultiplier;
            BleedDurationMultiplier = bleedScaling.DurationMultiplier;
            DepthReferenceName = depthReferenceName;
            PenetrationModel = penetrationModel;
            PassedThrough = passedThrough;
            BleedType = bleedType;
            FragmentationChance = Mathf.Clamp01(fragmentationChance);
            FragmentationDepth = fragmentationDepth;
            FragmentPaths = fragmentPaths;
        }

        internal static WoundBallistics Evaluate(Player player,
            EBodyPart bodyPart, DamageInfo damageInfo)
        {
            Vector3 direction = damageInfo.Direction.sqrMagnitude > 0.0001f
                ? damageInfo.Direction.normalized : Vector3.forward;
            Vector3 entry = damageInfo.HitPoint;
            bool hasExit = TryMeasureBodyPart(player, bodyPart,
                damageInfo.HitCollider, entry, direction, out Vector3 exit,
                out float thickness);
            TryMeasureCenterLine(player, bodyPart, damageInfo.HitCollider,
                entry, direction, out Vector3 referenceEntry,
                out Vector3 referenceExit, out float referenceThickness,
                out string depthReferenceName);

            AmmoTemplate ammo = FindAmmoTemplate(damageInfo.SourceId);
            float diameter = ammo != null
                ? Mathf.Max(1f, ammo.BulletDiameterMilimeters) : 7f;
            float initialVelocity = ammo != null
                ? Mathf.Max(1f, ammo.InitialSpeed) : 700f;
            float impactVelocity = FindImpactVelocity(damageInfo, direction,
                ref initialVelocity);
            float velocityRatio = Mathf.Clamp(impactVelocity / initialVelocity,
                0.25f, 1.25f);

            float diameterResistance = Mathf.Clamp(7f / diameter, 0.65f, 1.25f);
            float armorPenetrationDepth = (MinimumTissuePenetration +
                Mathf.Max(0f, damageInfo.PenetrationPower) *
                    PenetrationPowerDepthScale) *
                velocityRatio * diameterResistance;
            float projectileMassKg = ammo != null
                ? Mathf.Max(0.0001f, ammo.BulletMassGram * 0.001f)
                : 0.008f;
            float impactEnergyJoules = 0.5f * projectileMassKg *
                impactVelocity * impactVelocity;
            float kineticPenetrationDepth = MinimumTissuePenetration +
                Mathf.Sqrt(Mathf.Max(0f, impactEnergyJoules)) *
                KineticDepthScale;
            bool usesKineticFloor = kineticPenetrationDepth >
                armorPenetrationDepth;
            float penetrationDepth = usesKineticFloor
                ? kineticPenetrationDepth : armorPenetrationDepth;
            bool passedThrough = hasExit && penetrationDepth >= thickness;
            float traveledDepth = hasExit
                ? Mathf.Min(penetrationDepth, thickness) : penetrationDepth;
            float effectiveDiameter = diameter * GetVelocityDiameterScale(
                impactVelocity, velocityRatio);
            float woundScore = CalculateWoundScore(effectiveDiameter,
                traveledDepth, impactVelocity);
            BleedType bleedType = ResolveBleedType(woundScore);

            Vector3 displayedEnd = hasExit
                ? (passedThrough ? exit : entry + direction * penetrationDepth)
                : entry + direction * penetrationDepth;
            return new WoundBallistics(bodyPart, entry, displayedEnd, thickness,
                penetrationDepth, impactVelocity, diameter, woundScore,
                passedThrough, bleedType, referenceEntry, referenceExit,
                referenceThickness, depthReferenceName,
                armorPenetrationDepth, kineticPenetrationDepth,
                usesKineticFloor ? "kinetic tissue floor" : "armor penetration",
                ammo != null ? ammo.FragmentationChance : 0f);
        }

        internal WoundBallistics ApplyBoneResistance(Vector3 direction,
            float boneDistance, float resistanceDepth,
            float velocityRetention)
        {
            if (boneDistance < 0f || boneDistance > PenetrationDepth)
                return this;
            float penetrationDepth = boneDistance +
                Mathf.Max(0f, PenetrationDepth - boneDistance -
                    Mathf.Max(0f, resistanceDepth));
            float retainedVelocity = ImpactVelocity *
                Mathf.Clamp01(velocityRetention);
            bool passedThrough = TissueThickness > 0.001f &&
                penetrationDepth >= TissueThickness;
            float traveledDepth = FindTraveledDepth(penetrationDepth,
                TissueThickness);
            float previousTraveledDepth = FindTraveledDepth(PenetrationDepth,
                TissueThickness);
            float diameterContribution = WoundScore -
                previousTraveledDepth * DepthWoundWeight -
                ImpactVelocity * VelocityWoundWeight;
            float woundScore = diameterContribution +
                traveledDepth * DepthWoundWeight +
                retainedVelocity * VelocityWoundWeight;
            BleedType bleedType = ResolveBleedType(woundScore);
            Vector3 normalizedDirection = direction.sqrMagnitude > 0.0001f
                ? direction.normalized : Vector3.forward;
            Vector3 displayedEnd = passedThrough
                ? EntryPoint + normalizedDirection * TissueThickness
                : EntryPoint + normalizedDirection * penetrationDepth;
            return new WoundBallistics(BodyPart, EntryPoint, displayedEnd,
                TissueThickness,
                penetrationDepth, retainedVelocity, BulletDiameter, woundScore,
                passedThrough, bleedType, ReferenceEntryPoint,
                ReferenceExitPoint, ReferenceThickness, DepthReferenceName,
                ArmorPenetrationDepth, KineticPenetrationDepth,
                PenetrationModel, FragmentationChance);
        }

        internal WoundBallistics ApplyFragmentation(Player player,
            EBodyPart bodyPart, Collider hitCollider, Vector3 direction,
            int fireIndex, float chanceBonus = 0f)
        {
            if (!Plugin.EnableCustomFragmentation.Value)
                return this;

            bool isForced = OrganSystem.ForceFragmentation.Value;
            float effectiveChance = isForced
                ? 1f : Mathf.Clamp01(FragmentationChance + chanceBonus);
            if (effectiveChance <= 0f ||
                TraveledDepth <= 0.02f)
                return this;

            System.Random random = new System.Random(BuildFragmentSeed(
                fireIndex, EntryPoint));
            float chanceRoll = (float)random.NextDouble();
            if (chanceRoll >= effectiveChance)
                return this;

            int pathCount = random.Next(2, 4);
            float depthRoll = (float)random.NextDouble();
            float fragmentationDepth = TraveledDepth * depthRoll * depthRoll;
            fragmentationDepth = Mathf.Clamp(fragmentationDepth, 0.005f,
                TraveledDepth - 0.005f);
            float remainingDepth = Mathf.Max(0f,
                PenetrationDepth - fragmentationDepth);
            if (remainingDepth <= 0.001f)
                return this;

            Vector3 forward = direction.sqrMagnitude > 0.0001f
                ? direction.normalized : Vector3.forward;
            Vector3 splitPoint = EntryPoint + forward * fragmentationDepth;
            FragmentPath[] paths = new FragmentPath[pathCount];
            bool hasFragmentExit = false;
            float massRetention = Mathf.Pow(pathCount, -1f / 3f);
            float shortestPathDepth = float.MaxValue;
            float longestPathDepth = 0f;
            for (int index = 0; index < pathCount; index++)
            {
                float velocityRetention = Mathf.Lerp(0.85f, 0.95f,
                    (float)random.NextDouble());
                float pathDepth = remainingDepth * velocityRetention *
                    velocityRetention * massRetention;
                shortestPathDepth = Mathf.Min(shortestPathDepth, pathDepth);
                longestPathDepth = Mathf.Max(longestPathDepth, pathDepth);
                Vector3 pathDirection = CreateFragmentDirection(
                    forward, random);
                bool hasExit = MeasureBodyPart(player, bodyPart, hitCollider,
                    splitPoint, pathDirection, out _, out float exitDepth);
                bool passedThrough = hasExit && pathDepth >= exitDepth;
                hasFragmentExit |= passedThrough;
                float traveledDepth = hasExit
                    ? Mathf.Min(pathDepth, exitDepth) : pathDepth;
                paths[index] = new FragmentPath(splitPoint, pathDirection,
                    traveledDepth, passedThrough);
            }

            if (OrganSystem.DebugLogging.Value)
                TraumaLog.Info(
                    $"[WoundFragmentation] chance={effectiveChance:P1}" +
                    $"{(isForced ? " (forced)" : string.Empty)}, " +
                    $"roll={chanceRoll:P1}, paths={pathCount}, " +
                    $"splitDepth={fragmentationDepth:F3}m, " +
                    $"pathBudget={shortestPathDepth:F3}-" +
                    $"{longestPathDepth:F3}m, velocity=85-95%");

            return new WoundBallistics(BodyPart, EntryPoint, ExitPoint,
                TissueThickness,
                PenetrationDepth, ImpactVelocity, BulletDiameter, WoundScore,
                hasFragmentExit, BleedType, ReferenceEntryPoint,
                ReferenceExitPoint, ReferenceThickness, DepthReferenceName,
                ArmorPenetrationDepth, KineticPenetrationDepth,
                PenetrationModel, FragmentationChance, fragmentationDepth,
                paths);
        }

        private static int BuildFragmentSeed(int fireIndex, Vector3 entryPoint)
        {
            unchecked
            {
                int seed = fireIndex * 397;
                seed = seed * 397 ^ Mathf.RoundToInt(entryPoint.x * 1000f);
                seed = seed * 397 ^ Mathf.RoundToInt(entryPoint.y * 1000f);
                return seed * 397 ^ Mathf.RoundToInt(entryPoint.z * 1000f);
            }
        }

        private static Vector3 CreateFragmentDirection(Vector3 forward,
            System.Random random)
        {
            const float maximumConeAngleDegrees = 18f;
            Vector3 tangent = Vector3.Cross(forward,
                Mathf.Abs(forward.y) < 0.95f ? Vector3.up : Vector3.right)
                .normalized;
            Vector3 bitangent = Vector3.Cross(forward, tangent).normalized;
            float azimuth = (float)random.NextDouble() * Mathf.PI * 2f;
            float coneAngle = Mathf.Sqrt((float)random.NextDouble()) *
                maximumConeAngleDegrees * Mathf.Deg2Rad;
            Vector3 radial = tangent * Mathf.Cos(azimuth) +
                bitangent * Mathf.Sin(azimuth);
            return (forward * Mathf.Cos(coneAngle) +
                radial * Mathf.Sin(coneAngle)).normalized;
        }

        private static float FindTraveledDepth(float penetrationDepth,
            float tissueThickness) => tissueThickness > 0.001f
                ? Mathf.Min(penetrationDepth, tissueThickness)
                : penetrationDepth;

        private static float FindFullDamageDepth(EBodyPart bodyPart)
        {
            if (bodyPart == EBodyPart.Head)
                return OrganSystem.HeadFullDamageDepth.Value;
            if (bodyPart == EBodyPart.Chest)
                return OrganSystem.ChestFullDamageDepth.Value;
            return DefaultFullDamageDepth;
        }

        private static float FindDamageFloor(EBodyPart bodyPart)
        {
            if (bodyPart == EBodyPart.Head)
                return OrganSystem.HeadDamageFloor.Value;
            if (bodyPart == EBodyPart.Chest)
                return OrganSystem.ChestDamageFloor.Value;
            return DefaultDamageFloor;
        }

        private static float CalculateWoundScore(float effectiveDiameter,
            float traveledDepth, float impactVelocity) =>
            effectiveDiameter * DiameterWoundWeight +
            traveledDepth * DepthWoundWeight +
            impactVelocity * VelocityWoundWeight;

        private static BleedType ResolveBleedType(float woundScore) =>
            woundScore >= HeavyWoundThreshold
                ? BleedType.Heavy
                : BleedType.Light;

        private static bool TryMeasureCenterLine(Player player,
            EBodyPart bodyPart, Collider hitCollider, Vector3 hitPoint,
            Vector3 direction,
            out Vector3 referenceEntry, out Vector3 referenceExit,
            out float referenceThickness, out string referenceName)
        {
            return MeasureCenterLine(player, bodyPart, hitCollider, hitPoint,
                direction, out referenceEntry, out referenceExit,
                out referenceThickness, out referenceName);
        }

        private static bool MeasureCenterLine(Player player,
            EBodyPart bodyPart, Collider hitCollider, Vector3 hitPoint,
            Vector3 direction,
            out Vector3 referenceEntry, out Vector3 referenceExit,
            out float referenceThickness, out string referenceName)
        {
            referenceEntry = referenceExit = hitCollider != null
                ? hitCollider.bounds.center : Vector3.zero;
            referenceThickness = 0f;
            referenceName = "collider bounds";
            BodyPartCollider[] bodyColliders = player?.PlayerBones?.BodyPartColliders;
            bool hasBounds = OrganSystem.TryGetBodyPartBounds(player,
                bodyPart, out Bounds bodyBounds);

            Vector3 fallbackCenter = hasBounds ? bodyBounds.center : referenceEntry;
            bool hasFallback = TryMeasureLineAtCenter(bodyColliders, bodyPart,
                hitCollider, fallbackCenter, direction, out Vector3 fallbackEntry,
                out Vector3 fallbackExit, out float fallbackThickness);
            bool hasAnatomicalCenter = OrganSystem.TryFindDepthReferenceCenter(
                player, bodyPart, hitPoint, out Vector3 anatomicalCenter,
                out string anatomicalName);
            Vector3 anatomicalEntry = anatomicalCenter;
            Vector3 anatomicalExit = anatomicalCenter;
            float anatomicalThickness = 0f;
            bool hasAnatomical = hasAnatomicalCenter && TryMeasureLineAtCenter(
                bodyColliders, bodyPart, hitCollider, anatomicalCenter, direction,
                out anatomicalEntry, out anatomicalExit,
                out anatomicalThickness);

            if (hasAnatomical &&
                (!hasFallback || anatomicalThickness >= fallbackThickness))
            {
                referenceEntry = anatomicalEntry;
                referenceExit = anatomicalExit;
                referenceThickness = anatomicalThickness;
                referenceName = anatomicalName;
                return true;
            }
            if (!hasFallback)
                return false;
            referenceEntry = fallbackEntry;
            referenceExit = fallbackExit;
            referenceThickness = fallbackThickness;
            referenceName = hasAnatomical
                ? anatomicalName + " + bounds safeguard"
                : "collider bounds";
            return true;
        }

        private static bool TryMeasureLineAtCenter(
            BodyPartCollider[] bodyColliders, EBodyPart bodyPart,
            Collider hitCollider, Vector3 center, Vector3 direction,
            out Vector3 referenceEntry, out Vector3 referenceExit,
            out float referenceThickness)
        {
            referenceEntry = referenceExit = center;
            referenceThickness = 0f;
            List<TissueInterval> intervals = new List<TissueInterval>();
            if (bodyColliders != null)
            {
                for (int i = 0; i < bodyColliders.Length; i++)
                {
                    BodyPartCollider bodyCollider = bodyColliders[i];
                    if (bodyCollider == null || bodyCollider.BodyPartType != bodyPart)
                        continue;
                    if (TryFindColliderInterval(bodyCollider.Collider, center,
                        direction, out TissueInterval interval))
                        intervals.Add(interval);
                }
            }
            if (intervals.Count == 0 && TryFindColliderInterval(hitCollider,
                center, direction, out TissueInterval fallback))
                intervals.Add(fallback);
            if (!TryFindLongestConnectedInterval(intervals, out float start,
                out float end)) return false;

            referenceEntry = center + direction * start;
            referenceExit = center + direction * end;
            referenceThickness = end - start;
            return referenceThickness > 0.001f;
        }

        private static bool TryFindLongestConnectedInterval(
            List<TissueInterval> intervals, out float longestStart,
            out float longestEnd)
        {
            longestStart = longestEnd = 0f;
            if (intervals == null || intervals.Count == 0)
                return false;
            intervals.Sort((left, right) => left.Start.CompareTo(right.Start));
            float groupStart = intervals[0].Start;
            float groupEnd = intervals[0].End;
            for (int i = 1; i <= intervals.Count; i++)
            {
                if (i < intervals.Count &&
                    intervals[i].Start <= groupEnd + ColliderJoinTolerance)
                {
                    groupEnd = Mathf.Max(groupEnd, intervals[i].End);
                    continue;
                }
                if (groupEnd - groupStart > longestEnd - longestStart)
                {
                    longestStart = groupStart;
                    longestEnd = groupEnd;
                }
                if (i < intervals.Count)
                {
                    groupStart = intervals[i].Start;
                    groupEnd = intervals[i].End;
                }
            }
            return longestEnd - longestStart > 0.001f;
        }

        private static float GetVelocityDiameterScale(float impactVelocity,
            float retainedVelocityRatio)
        {
            float retainedVelocityAdjustment =
                (retainedVelocityRatio - 1f) * 0.06f;
            float absoluteVelocityAdjustment =
                (impactVelocity - 700f) * 0.00003f;
            return Mathf.Clamp(1f + retainedVelocityAdjustment +
                absoluteVelocityAdjustment, 0.94f, 1.04f);
        }

        private static bool TryMeasureBodyPart(Player player,
            EBodyPart bodyPart, Collider hitCollider, Vector3 entry,
            Vector3 direction, out Vector3 exit, out float thickness)
        {
            exit = entry;
            thickness = 0f;
            return MeasureBodyPart(player, bodyPart, hitCollider, entry,
                direction, out exit, out thickness);
        }

        private static bool MeasureBodyPart(Player player,
            EBodyPart bodyPart, Collider hitCollider, Vector3 entry,
            Vector3 direction, out Vector3 exit, out float thickness)
        {
            exit = entry;
            thickness = 0f;
            BodyPartCollider[] bodyColliders = player != null &&
                player.PlayerBones != null
                ? player.PlayerBones.BodyPartColliders : null;
            if (bodyColliders == null || bodyColliders.Length == 0)
                return TryFindColliderExit(hitCollider, entry, direction,
                    out exit, out thickness);

            List<TissueInterval> tissueIntervals =
                new List<TissueInterval>(bodyColliders.Length);
            for (int i = 0; i < bodyColliders.Length; i++)
            {
                BodyPartCollider bodyCollider = bodyColliders[i];
                if (bodyCollider == null ||
                    bodyCollider.BodyPartType != bodyPart) continue;

                Collider collider = bodyCollider.Collider;
                if (!TryFindColliderInterval(collider, entry, direction,
                    out TissueInterval interval)) continue;
                tissueIntervals.Add(interval);
            }

            if (tissueIntervals.Count == 0)
                return TryFindColliderExit(hitCollider, entry, direction,
                    out exit, out thickness);

            tissueIntervals.Sort((left, right) =>
                left.Start.CompareTo(right.Start));
            int entryIntervalIndex = -1;
            for (int i = 0; i < tissueIntervals.Count; i++)
            {
                TissueInterval interval = tissueIntervals[i];
                if (interval.Start <= ColliderJoinTolerance &&
                    interval.End >= -ColliderJoinTolerance)
                {
                    entryIntervalIndex = i;
                    break;
                }
            }

            if (entryIntervalIndex < 0)
                return TryFindColliderExit(hitCollider, entry, direction,
                    out exit, out thickness);

            float connectedEnd = Mathf.Max(0f,
                tissueIntervals[entryIntervalIndex].End);
            for (int i = entryIntervalIndex + 1;
                i < tissueIntervals.Count; i++)
            {
                TissueInterval interval = tissueIntervals[i];
                if (interval.Start > connectedEnd + ColliderJoinTolerance)
                    break;
                connectedEnd = Mathf.Max(connectedEnd, interval.End);
            }

            thickness = connectedEnd;
            exit = entry + direction * thickness;
            return thickness > 0.001f;
        }

        private static bool TryFindColliderInterval(Collider collider,
            Vector3 entry, Vector3 direction, out TissueInterval interval)
        {
            interval = default;
            if (collider == null || !collider.enabled)
                return false;

            float searchDistance = Vector3.Distance(entry,
                collider.bounds.center) + collider.bounds.size.magnitude + 0.25f;
            Vector3 beforeCollider = entry - direction * searchDistance;
            Vector3 afterCollider = entry + direction * searchDistance;
            if (!collider.Raycast(new Ray(beforeCollider, direction),
                out RaycastHit entryHit, searchDistance * 2f)) return false;
            if (!collider.Raycast(new Ray(afterCollider, -direction),
                out RaycastHit exitHit, searchDistance * 2f)) return false;

            float start = Vector3.Dot(entryHit.point - entry, direction);
            float end = Vector3.Dot(exitHit.point - entry, direction);
            if (end < start)
            {
                float previousStart = start;
                start = end;
                end = previousStart;
            }

            interval = new TissueInterval(start, end);
            return end - start > 0.001f;
        }

        private static bool TryFindColliderExit(Collider collider, Vector3 entry,
            Vector3 direction, out Vector3 exit, out float thickness)
        {
            exit = entry;
            thickness = 0f;
            if (!TryFindColliderInterval(collider, entry, direction,
                out TissueInterval interval)) return false;

            thickness = Mathf.Max(0f, interval.End);
            exit = entry + direction * thickness;
            return thickness > 0.001f;
        }

        private static AmmoTemplate FindAmmoTemplate(string templateId)
        {
            if (string.IsNullOrEmpty(templateId) ||
                !Singleton<ItemFactory>.Instantiated) return null;
            return Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(
                templateId, out ItemTemplate template)
                ? template as AmmoTemplate : null;
        }

        internal static void CaptureImpactVelocity(Shot shot)
        {
            if (shot == null || shot.Ammo == null ||
                shot.CurrentVelocity.sqrMagnitude <= 0.0001f) return;
            float now = Time.unscaledTime;
            if (ImpactVelocityCache.Count >= 32)
                ImpactVelocityCache.RemoveAt(0);
            ImpactVelocityCache.Add(new ImpactVelocityCacheEntry
            {
                AmmoId = shot.Ammo.TemplateId,
                Origin = shot.MasterOrigin,
                HitPoint = shot.HitPoint,
                Direction = shot.Direction.normalized,
                Velocity = shot.VelocityMagnitude,
                InitialVelocity = Mathf.Max(1f, shot.InitialSpeed),
                CapturedAt = now,
                CapturedFrame = Time.frameCount
            });
        }

        private static float FindImpactVelocity(DamageInfo damageInfo,
            Vector3 direction, ref float initialVelocity)
        {
            float now = Time.unscaledTime;
            int frame = Time.frameCount;
            for (int i = ImpactVelocityCache.Count - 1; i >= 0; i--)
            {
                ImpactVelocityCacheEntry cached = ImpactVelocityCache[i];
                if (now - cached.CapturedAt > BurstCacheDuration)
                {
                    ImpactVelocityCache.RemoveAt(i);
                    continue;
                }
                bool isPellet = cached.CapturedFrame == frame;
                float hitTolerance = isPellet
                    ? PelletReferencePositionTolerance
                    : BurstPositionTolerance;
                float directionDot = isPellet
                    ? PelletReferenceDirectionDot
                    : BurstDirectionDot;
                if (cached.AmmoId != damageInfo.SourceId ||
                    (cached.Origin - damageInfo.MasterOrigin).sqrMagnitude >
                        0.01f ||
                    (cached.HitPoint - damageInfo.HitPoint).sqrMagnitude >
                        hitTolerance * hitTolerance ||
                    Vector3.Dot(cached.Direction, direction) <
                        directionDot) continue;
                initialVelocity = cached.InitialVelocity;
                return cached.Velocity;
            }
            return initialVelocity;
        }
    }
}
