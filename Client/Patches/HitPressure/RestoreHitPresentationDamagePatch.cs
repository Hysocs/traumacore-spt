using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches.HitPressure
{
    internal static class HitPresentationDamageContext
    {
        [ThreadStatic]
        private static bool _hasOriginalDamage;

        [ThreadStatic]
        private static EBodyPart _bodyPart;

        [ThreadStatic]
        private static float _originalDamage;

        internal static void Capture(EBodyPart bodyPart, float damage)
        {
            _bodyPart = bodyPart;
            _originalDamage = damage;
            _hasOriginalDamage = damage > 0f;
        }

        internal static bool TryGetDamage(
            EBodyPart bodyPart,
            EDamageType damageType,
            out float damage)
        {
            damage = 0f;
            if (!_hasOriginalDamage ||
                _bodyPart != bodyPart ||
                !damageType.IsEnemyDamage())
                return false;

            damage = _originalDamage;
            return true;
        }

        internal static void Clear()
        {
            _hasOriginalDamage = false;
            _bodyPart = EBodyPart.Common;
            _originalDamage = 0f;
        }
    }

    public sealed class RestoreHitPresentationDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(EffectsController),
                nameof(EffectsController.OnPlayerDamaged),
                new[]
                {
                    typeof(float),
                    typeof(EBodyPart),
                    typeof(EDamageType),
                    typeof(float),
                    typeof(MaterialType)
                });

        [PatchPrefix]
        private static void PatchPrefix(
            ref float damage,
            EBodyPart bodyPart,
            EDamageType type,
            float damageReducedByArmor)
        {
            if (HitPresentationDamageContext.TryGetDamage(
                bodyPart,
                type,
                out float originalDamage))
                damage = Mathf.Max(0f, originalDamage - damageReducedByArmor);
        }
    }
}
