using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches
{
    public sealed class BodyTraumaPatch : ModulePatch
    {
        internal struct HitState
        {
            public bool Processed, CorpseShot, Heart, Brain, UpperSpine, ThoracicSpine, Bone, LocalShot;
            public EBodyPart BodyPart;
            public float Distance, OriginalDamage, EffectiveDamage, FinalDamage, Multiplier, TargetMultiplier;
            public Vector3 HitPoint, HitNormal, Direction, OrganIntersection, BoneIntersection;
            public Transform HitTransform;
        }

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.ApplyShot));

        [PatchPrefix]
        private static void PatchPrefix(Player __instance,
            ref DamageInfo damageInfo,
            EBodyPart bodyPartType, ref HitState __state)
        {
            DeterministicFracturePatch.InsideShot = false;
            if (!OrganSystem.Enabled.Value || __instance == null ||
                bodyPartType == EBodyPart.Common)
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
                __state.TargetMultiplier = rules.DamageMultiplier;
                __state.EffectiveDamage = __state.OriginalDamage * __state.TargetMultiplier;
                __state.LocalShot = damageInfo.HaveOwner && damageInfo.Player.iPlayer.IsYourPlayer;
                __state.HitPoint = damageInfo.HitPoint;
                __state.HitNormal = damageInfo.HitNormal;
                __state.Direction = damageInfo.Direction;
                if (damageInfo.HitCollider != null)
                    __state.HitTransform = damageInfo.HitCollider.attachedRigidbody != null
                        ? damageInfo.HitCollider.attachedRigidbody.transform
                        : damageInfo.HitCollider.transform;
                if (__instance.ActiveHealthController != null &&
                    !__instance.ActiveHealthController.IsAlive)
                {
                    __state.CorpseShot = true;
                    return;
                }
                bool organHit = organ != null && organ.IntersectsShot(__instance,
                    damageInfo.HitPoint, damageInfo.Direction,
                    out __state.OrganIntersection, out __state.Distance);
                if (bodyPartType == EBodyPart.Head && rules.BrainEnabled &&
                    OrganSystem.LowerBrain.IntersectsShot(__instance,
                        damageInfo.HitPoint, damageInfo.Direction,
                        out Vector3 lowerIntersection, out float lowerDistance) &&
                    (!organHit || lowerDistance < __state.Distance))
                {
                    organHit = true;
                    __state.OrganIntersection = lowerIntersection;
                    __state.Distance = lowerDistance;
                }
                __state.Heart = bodyPartType == EBodyPart.Chest && organHit;
                __state.Brain = bodyPartType == EBodyPart.Head && organHit;
                Vector3 spineIntersection = damageInfo.HitPoint;
                __state.UpperSpine = rules.CervicalSpineEnabled &&
                    (bodyPartType == EBodyPart.Head ||
                    bodyPartType == EBodyPart.Chest) &&
                    OrganSystem.IntersectsUpperSpine(__instance, damageInfo.HitPoint,
                        damageInfo.Direction, out spineIntersection);
                if (__state.UpperSpine)
                {
                    __state.Brain = true;
                    __state.OrganIntersection = spineIntersection;
                    __state.Distance = Vector3.Distance(damageInfo.HitPoint, spineIntersection);
                }
                Vector3 thoracicIntersection = damageInfo.HitPoint;
                __state.ThoracicSpine = rules.ThoracicSpineEnabled &&
                    (bodyPartType == EBodyPart.Chest ||
                    bodyPartType == EBodyPart.Stomach) &&
                    OrganSystem.IntersectsThoracicSpine(__instance, damageInfo.HitPoint,
                        damageInfo.Direction, out thoracicIntersection);
                if (__state.ThoracicSpine && !__state.Brain)
                {
                    __state.OrganIntersection = thoracicIntersection;
                    __state.Distance = Vector3.Distance(damageInfo.HitPoint, thoracicIntersection);
                }
                __state.Bone = OrganSystem.IntersectsLimbBone(__instance, bodyPartType,
                    damageInfo.HitPoint, damageInfo.Direction, out __state.BoneIntersection);

                __state.Multiplier = OrganSystem.DirectDamagePercent.Value * __state.TargetMultiplier;
                float currentPart = __instance.ActiveHealthController != null
                    ? __instance.ActiveHealthController.GetBodyPartHealth(bodyPartType).Current
                    : float.MaxValue;
                damageInfo.Damage = Mathf.Min(damageInfo.Damage * __state.Multiplier,
                    Mathf.Max(0f, currentPart - 1f));
                __state.FinalDamage = damageInfo.Damage;
            }
            catch (Exception e) { Plugin.Log.LogError("[OrganHit] Classification failed: " + e); }
        }

        private static OrganDefinition ResolveOrgan(EBodyPart bodyPart, TargetRules rules)
        {
            if (bodyPart == EBodyPart.Chest && rules.HeartEnabled) return OrganSystem.Heart;
            if (bodyPart == EBodyPart.Head && rules.BrainEnabled) return OrganSystem.Brain;
            return null;
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance,
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
                trauma.RecordImpact(__state.HitPoint, __state.Direction,
                    __state.OrganIntersection, __state.Heart, __state.Brain,
                    armorStopped, __state.Bone, __state.BoneIntersection,
                    __state.HitTransform, __state.UpperSpine,
                    __state.ThoracicSpine);
                if (__state.CorpseShot)
                {
                    if (armorStopped)
                    {
                        LogDebug("[OrganHit] CORPSE HIT: armor stopped blood effect");
                        return;
                    }
                    trauma.PaintNativeBloodAtHit(__state.HitPoint, __state.HitNormal);
                    trauma.AddCorpseWound(__state.BodyPart, __state.EffectiveDamage);
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
                    trauma.AddFatalHeadBlood(__state.EffectiveDamage);
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
                else if (__state.Heart) trauma.AddHeartWound(__state.EffectiveDamage);
                else ApplyTreatableWound(trauma, __state, damageInfo.BleedBlock);

                LogHit(__state, trauma);
            }
            catch (Exception e) { Plugin.Log.LogError("[OrganHit] Trauma application failed: " + e); }
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
                health.AddEffect<SpinalFractureHealthEffect>(
                    spinalPart, 0f, null, null, null);
                LogDebug("[SpineHit] Spine intersected: " + spinalPart +
                    " spinal fracture applied");
            }
        }

        private static void ApplyTreatableWound(TraumaController trauma, HitState state,
            bool bleedBlocked)
        {
            if (bleedBlocked)
            {
                LogDebug("[OrganHit] Hit-level bleed blocker prevented treatable wound on " +
                    state.BodyPart);
                return;
            }
            trauma.AddTreatableWound(state.BodyPart, state.EffectiveDamage);
        }

        private static void LogHit(HitState state, TraumaController trauma)
        {
            if (!OrganSystem.DebugLogging.Value) return;
            Vector3 hitboxSize = state.BodyPart == EBodyPart.Head
                ? OrganSystem.Brain.HalfExtents * 2f
                : state.BodyPart == EBodyPart.Chest ? OrganSystem.Heart.HalfExtents * 2f : Vector3.zero;
            Plugin.Log.LogInfo(string.Format(
                "[OrganHit] {0} | ray travel={1:F3}m size={2} | source={3:F1} target-x={4:F2} direct={5:F1} (x{6:F3}) | chest-stacks={7}",
                GetHitLabel(state), state.Distance, hitboxSize, state.OriginalDamage,
                state.TargetMultiplier, state.FinalDamage, state.Multiplier,
                trauma.ChestStacks + " (effective " + trauma.EffectiveChestStacks.ToString("0.0") + ")"));
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
            if (OrganSystem.DebugLogging.Value) Plugin.Log.LogInfo(message);
        }

        private static Exception Finalizer(Exception __exception)
        {
            DeterministicFracturePatch.InsideShot = false;
            DeterministicFracturePatch.AllowNextFracture = false;
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
