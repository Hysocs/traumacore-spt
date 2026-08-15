using System.Reflection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches
{
    public sealed class CustomArmorPenetrationPatch : ModulePatch
    {
        private const float DamagedArmorResistanceFloor = 0.65f;
        private const float EqualPenetrationBias = 4f;
        private const float ProbabilitySlope = 4f;
        private const float PenetratedDurabilityMultiplier = 0.65f;
        private const float PenetrationRetention = 0.65f;
        private const float MinimumDurabilityDamage = 0.25f;
        internal const float WeakSpotRadius = 0.045f;
        private const float MaximumWeakSpotMultiplier = 5f;
        private const int MaximumWeakSpotsPerArmor = 32;

        private sealed class ArmorWeakSpot
        {
            internal Transform Attachment;
            internal Vector3 LocalPosition;
            internal int BlockedHits;
        }

        private sealed class ArmorWeakSpotCollection
        {
            internal readonly List<ArmorWeakSpot> Spots = new List<ArmorWeakSpot>();
        }

        private static readonly ConditionalWeakTable<ArmorComponent, ArmorWeakSpotCollection>
            WeakSpots = new ConditionalWeakTable<ArmorComponent, ArmorWeakSpotCollection>();
        private static readonly List<ArmorWeakSpotCollection> DebugCollections =
            new List<ArmorWeakSpotCollection>();

        internal struct DebugWeakSpot
        {
            internal Vector3 Position;
            internal float Multiplier;
        }

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(ArmorComponent),
                nameof(ArmorComponent.ApplyDamage));

        [PatchPrefix]
        private static bool PatchPrefix(ArmorComponent __instance,
            ref DamageInfo damageInfo, List<ArmorComponent> armorComponents,
            ref float __result)
        {
            if (!OrganSystem.Enabled.Value || __instance == null ||
                (!damageInfo.DamageType.IsWeaponInduced() &&
                 damageInfo.DamageType != EDamageType.GrenadeFragment))
                return true;

            try
            {
                Player target = ResolveArmorWearer(__instance, damageInfo);
                if (target == null || !OrganSystem.GetTargetRules(target).ArmorPenetrationEnabled)
                    return true;

                float current = Mathf.Max(0f, __instance.Repairable.Durability);
                float maximum = Mathf.Max(0.01f, __instance.Repairable.MaxDurability);
                float durabilityRatio = Mathf.Clamp01(current / maximum);
                float penetration = Mathf.Max(0f, damageInfo.PenetrationPower);
                float fullRequirement = __instance.ArmorClass * 10f;
                float effectiveRequirement = fullRequirement * Mathf.Lerp(
                    DamagedArmorResistanceFloor, 1f, durabilityRatio);

                bool ricochet = damageInfo.DeflectedBy.HasValue;
                float chance = current <= 0.01f
                    ? 1f
                    : 1f / (1f + Mathf.Exp(-((penetration -
                        effectiveRequirement + EqualPenetrationBias) /
                        ProbabilitySlope)));
                float roll = UnityEngine.Random.value;
                bool penetrated = !ricochet &&
                    (current <= 0.01f || roll < chance);

                ArmorWeakSpot weakSpot = null;
                float weakSpotMultiplier = 1f;
                if (!penetrated && !ricochet)
                {
                    weakSpot = FindWeakSpot(__instance, damageInfo.HitPoint);
                    if (weakSpot != null)
                        weakSpotMultiplier = Mathf.Min(MaximumWeakSpotMultiplier,
                            weakSpot.BlockedHits + 1f);
                }

                float destructibility = 1f;
                GlobalConfiguration configuration =
                    Singleton<GlobalConfiguration>.Instance;
                if (configuration != null &&
                    configuration.ArmorMaterials.TryGetValue(
                        __instance.Template.ArmorMaterial, out var material))
                    destructibility = material.Destructibility;

                float durabilityDamage = penetration *
                    Mathf.Max(0f, damageInfo.ArmorDamage) *
                    Mathf.Max(0f, destructibility);
                if (penetrated)
                    durabilityDamage *= PenetratedDurabilityMultiplier;
                if (ricochet)
                    durabilityDamage *= 0.5f;
                durabilityDamage *= weakSpotMultiplier;
                durabilityDamage = Mathf.Min(current,
                    Mathf.Max(MinimumDurabilityDamage, durabilityDamage));

                if (durabilityDamage > 0f)
                    __instance.ApplyDurabilityDamage(
                        durabilityDamage, armorComponents);

                if (penetrated)
                {
                    damageInfo.BlockedBy = null;
                    damageInfo.PenetrationPower *= PenetrationRetention;
                }
                else
                {
                    damageInfo.BlockedBy = __instance.Item.Id;
                    damageInfo.Damage = 0f;
                    damageInfo.PenetrationPower = 0f;
                    if (!ricochet)
                        RecordBlockedHit(__instance, damageInfo.HitCollider,
                            damageInfo.HitPoint, weakSpot);
                }

                __result = durabilityDamage;
                if (OrganSystem.DebugLogging.Value)
                    Plugin.Log.LogInfo(string.Format(
                        "[ArmorPen] {0} | class={1} durability={2:0.0}/{3:0.0} ({4:P0}) " +
                        "pen={5:0.0} requirement={6:0.0} chance={7:P1} roll={8:P1} " +
                        "loss={9:0.00} weakSpot={10:0.0}x",
                        penetrated ? "PENETRATED" : ricochet ? "RICOCHET" : "STOPPED",
                        __instance.ArmorClass, current, maximum, durabilityRatio,
                        penetration, effectiveRequirement, chance, roll,
                        durabilityDamage, weakSpotMultiplier));
                return false;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError("[ArmorPen] Custom calculation failed; using EFT fallback: " + exception);
                return true;
            }
        }

        private static ArmorWeakSpot FindWeakSpot(ArmorComponent armor, Vector3 hitPoint)
        {
            if (!WeakSpots.TryGetValue(armor, out ArmorWeakSpotCollection collection))
                return null;

            float radiusSquared = WeakSpotRadius * WeakSpotRadius;
            for (int i = 0; i < collection.Spots.Count; i++)
            {
                ArmorWeakSpot spot = collection.Spots[i];
                if (spot.Attachment == null) continue;
                Vector3 worldPosition = spot.Attachment.TransformPoint(spot.LocalPosition);
                if ((worldPosition - hitPoint).sqrMagnitude <= radiusSquared)
                    return spot;
            }
            return null;
        }

        private static void RecordBlockedHit(ArmorComponent armor, Collider hitCollider,
            Vector3 hitPoint, ArmorWeakSpot existing)
        {
            if (existing != null)
            {
                existing.BlockedHits++;
                return;
            }

            Transform attachment = hitCollider != null ? hitCollider.transform : null;
            if (attachment == null) return;

            ArmorWeakSpotCollection collection;
            if (!WeakSpots.TryGetValue(armor, out collection))
            {
                collection = WeakSpots.GetOrCreateValue(armor);
                DebugCollections.Add(collection);
            }
            if (collection.Spots.Count >= MaximumWeakSpotsPerArmor)
                collection.Spots.RemoveAt(0);
            collection.Spots.Add(new ArmorWeakSpot
            {
                Attachment = attachment,
                LocalPosition = attachment.InverseTransformPoint(hitPoint),
                BlockedHits = 1
            });
        }

        internal static void CopyDebugWeakSpots(Player player, List<DebugWeakSpot> output)
        {
            if (player == null || output == null) return;
            for (int collectionIndex = DebugCollections.Count - 1;
                collectionIndex >= 0; collectionIndex--)
            {
                List<ArmorWeakSpot> spots = DebugCollections[collectionIndex].Spots;
                for (int spotIndex = spots.Count - 1; spotIndex >= 0; spotIndex--)
                {
                    ArmorWeakSpot spot = spots[spotIndex];
                    if (spot.Attachment == null)
                    {
                        spots.RemoveAt(spotIndex);
                        continue;
                    }
                    Player wearer = spot.Attachment.GetComponentInParent<Player>();
                    if (wearer != player) continue;
                    output.Add(new DebugWeakSpot
                    {
                        Position = spot.Attachment.TransformPoint(spot.LocalPosition),
                        Multiplier = Mathf.Min(MaximumWeakSpotMultiplier,
                            spot.BlockedHits + 1f)
                    });
                }
                if (spots.Count == 0)
                    DebugCollections.RemoveAt(collectionIndex);
            }
        }

        private static Player ResolveArmorWearer(ArmorComponent armor, DamageInfo damageInfo)
        {
            if (damageInfo.HitCollider != null)
            {
                Player player = damageInfo.HitCollider.GetComponentInParent<Player>();
                if (player != null) return player;
                if (damageInfo.HitCollider.attachedRigidbody != null)
                {
                    player = damageInfo.HitCollider.attachedRigidbody.GetComponentInParent<Player>();
                    if (player != null) return player;
                }
            }

            if (armor != null && armor.Item != null && armor.Item.Parent != null &&
                armor.Item.Parent.GetOwner() is Player.PlayerInventoryController inventory)
                return inventory.Player;
            return null;
        }
    }
}
