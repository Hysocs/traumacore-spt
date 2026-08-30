using System;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches.Trauma
{
    internal static class PostArmorDamageContext
    {
        [ThreadStatic] private static bool _isPending;
        [ThreadStatic] private static float _damageMultiplier;
        [ThreadStatic] private static float _fragmentDamageMultiplier;
        [ThreadStatic] private static float _maximumDamage;
        [ThreadStatic] private static float _postArmorDamage;
        [ThreadStatic] private static bool _hasPostArmorDamage;
        [ThreadStatic] private static Player _targetPlayer;

        internal static void Capture(Player targetPlayer, float damageMultiplier,
            float fragmentDamageBonus, float originalDamage,
            float maximumDamage)
        {
            _isPending = true;
            _hasPostArmorDamage = false;
            _targetPlayer = targetPlayer;
            _damageMultiplier = Mathf.Max(0f, damageMultiplier);
            _fragmentDamageMultiplier = originalDamage > 0f
                ? 1f + Mathf.Max(0f, fragmentDamageBonus / originalDamage)
                : 1f;
            _maximumDamage = Mathf.Max(0f, maximumDamage);
        }

        internal static void Apply(Player targetPlayer,
            ref DamageInfo damageInfo)
        {
            if (!_isPending || targetPlayer != _targetPlayer)
                return;
            float postArmorDamage = Mathf.Max(0f, damageInfo.Damage);
            _postArmorDamage = postArmorDamage * _fragmentDamageMultiplier;
            _hasPostArmorDamage = true;
            damageInfo.Damage = Mathf.Min(
                _postArmorDamage * _damageMultiplier,
                _maximumDamage);
            _isPending = false;
        }

        internal static bool TryTakePostArmorDamage(Player targetPlayer,
            out float damage)
        {
            damage = 0f;
            if (!_hasPostArmorDamage || targetPlayer != _targetPlayer)
                return false;
            damage = _postArmorDamage;
            Clear();
            return true;
        }

        internal static void Clear()
        {
            _isPending = false;
            _damageMultiplier = 0f;
            _fragmentDamageMultiplier = 0f;
            _maximumDamage = 0f;
            _postArmorDamage = 0f;
            _hasPostArmorDamage = false;
            _targetPlayer = null;
        }
    }

    public sealed class ApplyPostArmorDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player),
                nameof(Player.ProceedDamageThroughArmor),
                new[]
                {
                    typeof(DamageInfo).MakeByRefType(),
                    typeof(EBodyPartColliderType),
                    typeof(EArmorPlateCollider),
                    typeof(bool)
                });

        [PatchPostfix]
        private static void ApplyTraumaDamage(Player __instance,
            ref DamageInfo damageInfo)
        {
            PostArmorDamageContext.Apply(__instance, ref damageInfo);
        }
    }
}
