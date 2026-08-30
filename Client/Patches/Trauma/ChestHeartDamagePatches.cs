using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using TraumaCore.Patches.HitPressure;
using TraumaCore.Features.DeathScreen.DamageTracking;
using UnityEngine;

namespace TraumaCore.Patches.Trauma
{
    public sealed class BodyTraumaPatch : ModulePatch
    {
        private enum BoneCollisionType
        {
            Skull,
            Ribcage,
            CervicalSpine,
            ThoracicSpine,
            Limb
        }

        private const float RibResistanceDepth = 0.04f;
        private const float LimbResistanceDepth = 0.05f;
        private const float SkullResistanceDepth = 0.05f;
        private const float CervicalSpineResistanceDepth = 0.12f;
        private const float ThoracicSpineResistanceDepth = 0.14f;

        private readonly struct BoneCollision
        {
            internal readonly BoneCollisionType Type;
            internal readonly Vector3 Point;
            internal readonly float Distance;

            internal BoneCollision(BoneCollisionType type, Vector3 point,
                Vector3 hitPoint)
            {
                Type = type;
                Point = point;
                Distance = Vector3.Distance(hitPoint, point);
            }
        }

        private readonly struct BoneResistance
        {
            internal readonly float Depth;
            internal readonly float VelocityRetention;

            internal BoneResistance(float depth, float velocityRetention)
            {
                Depth = depth;
                VelocityRetention = velocityRetention;
            }
        }

        internal struct HitState
        {
            public bool Processed, CorpseShot, Heart, Brain, UpperSpine,
                ThoracicSpine, Skull, Ribcage, Bone, LocalShot;
            public EBodyPart BodyPart;
            public float Distance, OriginalDamage, EffectiveDamage, FinalDamage,
                Multiplier, TargetMultiplier, OriginalPenetrationDepth,
                OriginalImpactVelocity, FirstBoneDistance, BoneResistanceDepth,
                FragmentDamageBonus, ArmorPenetrationMargin,
                ArmorFragmentationChanceBonus, ArmorPenetrationRetention,
                OrganExitDistance;
            public int BoneCollisionCount, ProjectileIndex;
            public string HitboxName;
            public Vector3 HitPoint, HitNormal, Direction, OrganIntersection, BoneIntersection;
            public Transform HitTransform;
            public WoundBallistics Wound;
        }

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.ApplyShot),
                new[]
                {
                    typeof(DamageInfo),
                    typeof(EBodyPart),
                    typeof(EBodyPartColliderType),
                    typeof(EArmorPlateCollider),
                    typeof(ShotId)
                });

        [PatchPrefix]
        private static void CaptureTraumaHit(Player __instance,
            ref DamageInfo damageInfo,
            EBodyPart bodyPartType, ShotId shotId, ref HitState __state)
        {
            DeterministicFracturePatch.InsideShot = false;
            HitPresentationDamageContext.Clear();
            PostArmorDamageContext.Clear();
            if (!OrganSystem.Enabled.Value || __instance == null ||
                bodyPartType == EBodyPart.Common || damageInfo.Damage <= 0f)
                return;

            try
            {
                TargetRules rules = OrganSystem.GetTargetRules(__instance);
                if (!rules.BodyTraumaEnabled) return;
                DeterministicFracturePatch.InsideShot = true;
                OrganDefinition organ = ResolveOrgan(bodyPartType, rules);
                __state.Processed = true;
                __state.BodyPart = bodyPartType;
                __state.OriginalDamage = damageInfo.Damage;
                HitPresentationDamageContext.Capture(
                    bodyPartType,
                    __state.OriginalDamage);
                __state.TargetMultiplier = rules.DamageMultiplier;
                __state.EffectiveDamage = __state.OriginalDamage * __state.TargetMultiplier;
                __state.LocalShot = damageInfo.HaveOwner && damageInfo.Player.iPlayer.IsYourPlayer;
                __state.HitPoint = damageInfo.HitPoint;
                __state.HitNormal = damageInfo.HitNormal;
                __state.Direction = damageInfo.Direction;
                __state.ProjectileIndex = shotId._fragmentIndex;
                if (damageInfo.HitCollider != null)
                {
                    __state.HitboxName = damageInfo.HitCollider.name;
                    __state.HitTransform = damageInfo.HitCollider.attachedRigidbody != null
                        ? damageInfo.HitCollider.attachedRigidbody.transform
                        : damageInfo.HitCollider.transform;
                }
                if (IsStoppedByArmor(damageInfo)) return;

                DamageInfo woundDamageInfo = damageInfo;
                ResolveNativeArmorInteraction(__instance, damageInfo,
                    ref woundDamageInfo, ref __state);
                __state.Wound = WoundBallistics.Evaluate(__instance,
                    bodyPartType, woundDamageInfo);
                __state.OriginalPenetrationDepth =
                    __state.Wound.PenetrationDepth;
                __state.OriginalImpactVelocity = __state.Wound.ImpactVelocity;
                __state.FirstBoneDistance = -1f;
                if (__instance.ActiveHealthController != null &&
                    !__instance.ActiveHealthController.IsAlive)
                {
                    __state.CorpseShot = true;
                    return;
                }
                Vector3 spineIntersection = damageInfo.HitPoint;
                bool upperSpineCandidate = rules.CervicalSpineEnabled &&
                    (bodyPartType == EBodyPart.Head ||
                    bodyPartType == EBodyPart.Chest) &&
                    OrganSystem.IntersectsUpperSpine(__instance, damageInfo.HitPoint,
                        damageInfo.Direction, out spineIntersection);
                Vector3 thoracicIntersection = damageInfo.HitPoint;
                bool thoracicSpineCandidate = rules.ThoracicSpineEnabled &&
                    (bodyPartType == EBodyPart.Chest ||
                    bodyPartType == EBodyPart.Stomach) &&
                    OrganSystem.IntersectsThoracicSpine(__instance, damageInfo.HitPoint,
                        damageInfo.Direction, out thoracicIntersection);
                bool boneCandidate = OrganSystem.IntersectsLimbBone(__instance,
                    bodyPartType, damageInfo.HitPoint, damageInfo.Direction,
                    out __state.BoneIntersection);
                Vector3 ribIntersection = damageInfo.HitPoint;
                bool ribCandidate = bodyPartType == EBodyPart.Chest &&
                    OrganSystem.TryFindRibcageIntersection(__state.Wound,
                        damageInfo.Direction, out ribIntersection);
                Vector3 skullIntersection = damageInfo.HitPoint;
                bool skullCandidate = bodyPartType == EBodyPart.Head &&
                    rules.BrainEnabled && OrganSystem.TryFindSkullIntersection(
                        __state.Wound, damageInfo.Direction,
                        out skullIntersection);

                List<BoneCollision> boneCollisions = new List<BoneCollision>(6);
                if (skullCandidate) boneCollisions.Add(new BoneCollision(
                    BoneCollisionType.Skull, skullIntersection,
                    damageInfo.HitPoint));
                if (ribCandidate)
                    boneCollisions.Add(new BoneCollision(
                        BoneCollisionType.Ribcage, ribIntersection,
                        damageInfo.HitPoint));
                if (upperSpineCandidate) boneCollisions.Add(new BoneCollision(
                    BoneCollisionType.CervicalSpine, spineIntersection,
                    damageInfo.HitPoint));
                if (thoracicSpineCandidate) boneCollisions.Add(new BoneCollision(
                    BoneCollisionType.ThoracicSpine, thoracicIntersection,
                    damageInfo.HitPoint));
                if (boneCandidate) boneCollisions.Add(new BoneCollision(
                    BoneCollisionType.Limb, __state.BoneIntersection,
                    damageInfo.HitPoint));
                boneCollisions.Sort((left, right) =>
                    left.Distance.CompareTo(right.Distance));
                for (int i = 0; i < boneCollisions.Count; i++)
                {
                    BoneCollision collision = boneCollisions[i];
                    if (!CanReachInternalPoint(__state.Wound, collision.Distance))
                        continue;
                    BoneResistance resistance = ResolveBoneResistance(
                        collision.Type);
                    float depthBeforeBone = __state.Wound.PenetrationDepth;
                    __state.Wound = __state.Wound.ApplyBoneResistance(
                        damageInfo.Direction, collision.Distance,
                        resistance.Depth, resistance.VelocityRetention);
                    __state.BoneCollisionCount++;
                    if (__state.FirstBoneDistance < 0f)
                        __state.FirstBoneDistance = collision.Distance;
                    __state.BoneResistanceDepth += depthBeforeBone -
                        __state.Wound.PenetrationDepth;
                    switch (collision.Type)
                    {
                        case BoneCollisionType.Skull:
                            __state.Skull = true;
                            break;
                        case BoneCollisionType.Ribcage:
                            __state.Ribcage = true;
                            break;
                        case BoneCollisionType.CervicalSpine:
                            __state.UpperSpine = true;
                            break;
                        case BoneCollisionType.ThoracicSpine:
                            __state.ThoracicSpine = true;
                            break;
                        case BoneCollisionType.Limb:
                            __state.Bone = true;
                            break;
                    }
                }
                __state.Wound = __state.Wound.ApplyFragmentation(__instance,
                    bodyPartType, damageInfo.HitCollider,
                    damageInfo.Direction, damageInfo.FireIndex,
                    __state.ArmorFragmentationChanceBonus);
                __state.FragmentDamageBonus =
                    __state.Wound.CalculateFragmentDamageBonus(
                        __state.OriginalDamage);

                bool organHit = organ != null && organ.IntersectsShot(__instance,
                    damageInfo.HitPoint, damageInfo.Direction,
                    out __state.OrganIntersection, out __state.Distance,
                    out __state.OrganExitDistance) &&
                    CanReachInternalPoint(__state.Wound, __state.Distance);
                if (bodyPartType == EBodyPart.Head && rules.BrainEnabled &&
                    OrganSystem.LowerBrain.IntersectsShot(__instance,
                        damageInfo.HitPoint, damageInfo.Direction,
                        out Vector3 lowerIntersection, out float lowerDistance) &&
                    CanReachInternalPoint(__state.Wound, lowerDistance) &&
                    (!organHit || lowerDistance < __state.Distance))
                {
                    organHit = true;
                    __state.OrganIntersection = lowerIntersection;
                    __state.Distance = lowerDistance;
                }
                __state.Heart = bodyPartType == EBodyPart.Chest && organHit;
                __state.Brain = bodyPartType == EBodyPart.Head && organHit;
                if (__state.UpperSpine)
                {
                    __state.Brain = true;
                    __state.OrganIntersection = spineIntersection;
                    __state.Distance = Vector3.Distance(damageInfo.HitPoint,
                        spineIntersection);
                }
                if (__state.ThoracicSpine && !__state.Brain)
                {
                    __state.OrganIntersection = thoracicIntersection;
                    __state.Distance = Vector3.Distance(damageInfo.HitPoint,
                        thoracicIntersection);
                }
                __state.Multiplier = OrganSystem.DirectDamagePercent.Value *
                    __state.TargetMultiplier *
                    __state.Wound.DirectDamageMultiplier;
                float currentPart = __instance.ActiveHealthController != null
                    ? __instance.ActiveHealthController.GetBodyPartHealth(bodyPartType).Current
                    : float.MaxValue;
                float maximumDamage = IsVitalBodyPart(bodyPartType)
                    ? Mathf.Max(0f, currentPart - 1f) : float.MaxValue;
                PostArmorDamageContext.Capture(__instance, __state.Multiplier,
                    __state.FragmentDamageBonus, __state.OriginalDamage,
                    maximumDamage);
                __state.FinalDamage = (__state.OriginalDamage +
                    __state.FragmentDamageBonus) * __state.Multiplier;
            }
            catch (Exception e) { TraumaLog.Error("[OrganHit] Classification failed: " + e); }
        }

        private static void ResolveNativeArmorInteraction(Player player,
            DamageInfo damageInfo, ref DamageInfo woundDamageInfo,
            ref HitState state)
        {
            state.ArmorPenetrationRetention = 1f;
            if (!(damageInfo.HittedBallisticCollider is BodyPartCollider bodyPart) ||
                !player.TryGetArmorResistData(bodyPart,
                    damageInfo.PenetrationPower,
                    out ArmorResistanceData resistance))
                return;

            state.ArmorPenetrationMargin = damageInfo.PenetrationPower -
                resistance.RealResistance;
            state.ArmorPenetrationRetention = resistance.CF;
            woundDamageInfo.PenetrationPower *= resistance.CF;
            float marginalPenetration = 1f - Mathf.InverseLerp(
                0f, 15f, state.ArmorPenetrationMargin);
            state.ArmorFragmentationChanceBonus =
                marginalPenetration * 0.25f;
        }

        private static BoneResistance ResolveBoneResistance(
            BoneCollisionType collisionType)
        {
            switch (collisionType)
            {
                case BoneCollisionType.Skull:
                    return new BoneResistance(SkullResistanceDepth, 0.85f);
                case BoneCollisionType.ThoracicSpine:
                    return new BoneResistance(ThoracicSpineResistanceDepth, 0.72f);
                case BoneCollisionType.CervicalSpine:
                    return new BoneResistance(CervicalSpineResistanceDepth, 0.75f);
                case BoneCollisionType.Ribcage:
                    return new BoneResistance(RibResistanceDepth, 0.94f);
                case BoneCollisionType.Limb:
                    return new BoneResistance(LimbResistanceDepth, 0.90f);
                default:
                    return new BoneResistance(RibResistanceDepth, 0.90f);
            }
        }

        private static bool IsVitalBodyPart(EBodyPart bodyPart) =>
            bodyPart == EBodyPart.Head || bodyPart == EBodyPart.Chest;

        private static OrganDefinition ResolveOrgan(EBodyPart bodyPart, TargetRules rules)
        {
            if (bodyPart == EBodyPart.Chest && rules.HeartEnabled) return OrganSystem.Heart;
            if (bodyPart == EBodyPart.Head && rules.BrainEnabled) return OrganSystem.Brain;
            return null;
        }

        private static bool CanReachInternalPoint(WoundBallistics wound,
            float distance)
        {
            return distance <= wound.PenetrationDepth + 0.002f;
        }

        [PatchPostfix]
        private static void ApplyTraumaHit(Player __instance,
            ref DamageInfo damageInfo,
            HitState __state)
        {
            DeterministicFracturePatch.InsideShot = false;
            if (!__state.Processed || __instance == null || __instance.ActiveHealthController == null)
                return;

            try
            {
                if (__state.LocalShot) Plugin.SetLastHitTarget(__instance);
                TraumaController trauma = GetOrCreateTrauma(__instance);
                bool armorStopped = IsStoppedByArmor(damageInfo);
                if (!armorStopped)
                    trauma.CaptureImpact(new TraumaController.ImpactCapture
                {
                    HitPoint = __state.HitPoint,
                    Direction = __state.Direction,
                    Intersection = __state.OrganIntersection,
                    Heart = __state.Heart,
                    Brain = __state.Brain,
                    ArmorStopped = armorStopped,
                    Bone = __state.Bone,
                    BoneIntersection = __state.BoneIntersection,
                    HitTransform = __state.HitTransform,
                    CervicalSpine = __state.UpperSpine,
                    ThoracicSpine = __state.ThoracicSpine,
                    Wound = __state.Wound,
                    Ribcage = __state.Ribcage,
                    Skull = __state.Skull,
                    HitboxName = __state.HitboxName,
                    BodyPart = __state.BodyPart,
                    OriginalPenetrationDepth = __state.OriginalPenetrationDepth,
                    OriginalImpactVelocity = __state.OriginalImpactVelocity,
                    BoneCollisionCount = __state.BoneCollisionCount,
                    FirstBoneDistance = __state.FirstBoneDistance,
                    BoneResistanceDepth = __state.BoneResistanceDepth,
                    FragmentDamageBonus = __state.FragmentDamageBonus,
                    ArmorPenetrationMargin = __state.ArmorPenetrationMargin,
                    ArmorFragmentationChanceBonus =
                        __state.ArmorFragmentationChanceBonus,
                    ArmorPenetrationRetention =
                        __state.ArmorPenetrationRetention,
                    FireIndex = damageInfo.FireIndex,
                    ProjectileIndex = __state.ProjectileIndex,
                    DamageType = damageInfo.DamageType
                });
                if (__state.CorpseShot)
                {
                    if (armorStopped)
                    {
                        LogDebug("[OrganHit] CORPSE HIT: armor stopped blood effect");
                        return;
                    }
                    trauma.PaintNativeBloodAtHit(__state.HitPoint, __state.HitNormal);
                    trauma.AddCorpseWound(__state.BodyPart);
                    LogDebug("[OrganHit] CORPSE HIT: finite-reserve blood effect added");
                    return;
                }
                if (armorStopped)
                {
                    trauma.AddBruise(__state.EffectiveDamage);
                    LogDebug("[OrganHit] ARMOR STOP: bruise applied; no organ or bleed wound");
                    return;
                }
                trauma.PaintNativeBloodAtHit(__state.HitPoint, __state.HitNormal);
                ApplyFractures(__instance, __state);
                if (__state.Brain)
                {
                    trauma.AddFatalHeadBlood();
                    trauma.SetHeadDeathVoicePending(true);
                    try
                    {
                        __instance.ActiveHealthController.Kill(EDamageType.Bullet);
                    }
                    finally
                    {
                        trauma.SetHeadDeathVoicePending(false);
                    }
                }
                else if (__state.Heart) trauma.AddHeartWound(
                    __state.Wound.CalculateHeartBleedMultiplier(
                        __state.Distance, __state.OrganExitDistance));
                else ApplyTreatableWound(__instance, trauma, __state,
                    damageInfo.Damage, damageInfo.BleedBlock);

                LogHit(__state, trauma);
            }
            catch (Exception e) { TraumaLog.Error("[OrganHit] Trauma application failed: " + e); }
            finally { HitPresentationDamageContext.Clear(); }
        }

        private static TraumaController GetOrCreateTrauma(Player player)
        {
            TraumaController trauma = player.GetComponent<TraumaController>();
            if (trauma == null) trauma = player.gameObject.AddComponent<TraumaController>();
            trauma.InitializeForPlayer(player);
            return trauma;
        }

        private static bool IsStoppedByArmor(DamageInfo damageInfo)
        { return damageInfo.BlockedBy.HasValue || damageInfo.DeflectedBy.HasValue; }

        private static void ApplyFractures(Player player, HitState state)
        {
            ActiveHealthController health = player.ActiveHealthController;
            if (state.Bone && !HasFracture(player, state.BodyPart))
            {
                DeterministicFracturePatch.AllowNextFracture = true;
                health.DoFracture(state.BodyPart);
                LogDebug("[BoneHit] " + state.BodyPart + " bone intersected: fracture applied");
            }
            EBodyPart spinalPart = state.BodyPart == EBodyPart.Stomach
                ? EBodyPart.Stomach : EBodyPart.Chest;
            if (!state.Brain && state.ThoracicSpine && !HasFracture(player, spinalPart))
            {
                health.AddEffect<ActiveHealthController.Fracture>(
                    spinalPart, 0f, null, null, null);
                LogDebug("[SpineHit] Spine intersected: " + spinalPart +
                    " spinal fracture applied");
            }
        }

        private static void ApplyTreatableWound(Player player,
            TraumaController trauma, HitState state, float directDamage,
            bool bleedBlocked)
        {
            if (bleedBlocked)
            {
                LogDebug("[OrganHit] Hit-level bleed blocker prevented treatable wound on " +
                    state.BodyPart);
                return;
            }
            float eftPostArmorDamage = state.OriginalDamage +
                state.FragmentDamageBonus;
            if (PostArmorDamageContext.TryTakePostArmorDamage(player,
                out float capturedPostArmorDamage))
                eftPostArmorDamage = capturedPostArmorDamage;
            trauma.AddTreatableWound(state.BodyPart, state.Wound.BleedType,
                state.Wound.BleedDamageMultiplier,
                state.Wound.BleedDurationMultiplier,
                eftPostArmorDamage, directDamage, state.Wound.DepthRatio);
        }

        private static void LogHit(HitState state, TraumaController trauma)
        {
            if (!OrganSystem.DebugLogging.Value) return;
            TraumaLog.Info(string.Format(
                "[OrganHit] {0} | traveled={1:F3}m reference={2:F3}m depth={3:P0} pen={4:F3}m {5} {6} bleed={7:F2}x/{8:F2}t | source={9:F1} target-x={10:F2} direct={11:F1} (x{12:F3}) | wounds L/H={13}/{14}",
                GetHitLabel(state), state.Wound.TraveledDepth,
                state.Wound.ReferenceThickness, state.Wound.DepthRatio,
                state.Wound.PenetrationDepth, state.Wound.BleedType,
                state.Wound.PassedThrough ? "THROUGH" : "STOPPED",
                state.Wound.BleedDamageMultiplier,
                state.Wound.BleedDurationMultiplier,
                state.OriginalDamage, state.TargetMultiplier, state.FinalDamage,
                state.Multiplier, trauma.LightWoundCount,
                trauma.HeavyWoundCount));
        }

        private static string GetHitLabel(HitState state)
        {
            if (state.UpperSpine) return "UPPER SPINE";
            if (state.ThoracicSpine) return "THORACIC SPINE";
            if (state.Brain) return "BRAIN";
            if (state.Heart) return "HEART";
            if (state.BodyPart == EBodyPart.Head) return "FACE/HEAD";
            return state.BodyPart.ToString().ToUpperInvariant();
        }

        private static void LogDebug(string message)
        {
            TraumaLog.Info(message);
        }

        [PatchFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            DeterministicFracturePatch.InsideShot = false;
            PostArmorDamageContext.Clear();
            DeterministicFracturePatch.AllowNextFracture = false;
            HitPresentationDamageContext.Clear();
            return __exception;
        }

        private static bool HasFracture(Player player, EBodyPart bodyPart)
        {
            if (player == null || player.ActiveHealthController == null) return false;
            foreach (IHealthEffect effect in player.ActiveHealthController.GetAllActiveEffects())
                if (effect is IFracture && effect.BodyPart == bodyPart) return true;
            return false;
        }
    }

    public sealed class DeterministicFracturePatch : ModulePatch
    {
        [ThreadStatic] internal static bool InsideShot;
        [ThreadStatic] internal static bool AllowNextFracture;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ActiveHealthController),
                nameof(ActiveHealthController.DoFracture));

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            if (AllowNextFracture) { AllowNextFracture = false; return true; }
            return !InsideShot;
        }
    }
}
