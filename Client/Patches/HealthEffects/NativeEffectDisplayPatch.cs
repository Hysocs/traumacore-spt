using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraumaCore.Patches.HealthEffects
{
    internal static class NativeEffectLabels
    {
        private sealed class LabelState
        {
            internal float BruiseExpires;
            internal float ImpactShockExpires;
            internal float BleedExpires;
            internal float BleedDamagePerSecond;
        }

        private static readonly ConditionalWeakTable<IHealthEffect, LabelState>
            Labels = new ConditionalWeakTable<IHealthEffect, LabelState>();

        internal static void MarkBruised(IHealthEffect effect, float duration)
        {
            if (effect == null) return;
            LabelState labels = Labels.GetOrCreateValue(effect);
            labels.BruiseExpires = Mathf.Max(labels.BruiseExpires,
                Time.unscaledTime + Mathf.Max(0f, duration));
        }

        internal static void MarkImpactShock(IHealthEffect effect, float duration)
        {
            if (effect == null) return;
            LabelState labels = Labels.GetOrCreateValue(effect);
            labels.ImpactShockExpires = Mathf.Max(labels.ImpactShockExpires,
                Time.unscaledTime + Mathf.Max(0f, duration));
        }

        internal static void UpdateBleedPresentation(IHealthEffect effect,
            float timeLeft, float damagePerSecond)
        {
            if (effect == null) return;
            LabelState labels = Labels.GetOrCreateValue(effect);
            labels.BleedExpires = Time.unscaledTime + Mathf.Max(0f, timeLeft);
            labels.BleedDamagePerSecond = Mathf.Max(0f, damagePerSecond);
        }

        internal static List<SimpleBuffDescription> BuildBleedLabels(
            IHealthEffect effect)
        {
            if (effect == null || !Labels.TryGetValue(effect,
                out LabelState labels))
                return null;
            if (labels.BleedExpires <= Time.unscaledTime)
                return null;

            string effectName = effect is IHeavyBleeding
                ? "HeavyBleeding"
                : "LightBleeding";
            SimpleBuffDescription nameLabel =
                float.IsInfinity(labels.BleedExpires)
                    ? new BuffDescription(effectName, () => 0f,
                        float.PositiveInfinity)
                    : BuildTimedLabel(effectName, labels.BleedExpires);
            return new List<SimpleBuffDescription>
            {
                nameLabel,
                new SimpleBuffDescription(
                    $"Blood loss: {labels.BleedDamagePerSecond:0.0} HP/s")
            };
        }

        internal static List<SimpleBuffDescription> BuildActiveLabels(
            IHealthEffect effect)
        {
            if (effect == null || !Labels.TryGetValue(effect,
                out LabelState labels))
                return null;

            float now = Time.unscaledTime;
            List<SimpleBuffDescription> activeLabels =
                new List<SimpleBuffDescription>(2);
            if (labels.BruiseExpires > now)
                activeLabels.Add(BuildTimedLabel("Bruised",
                    labels.BruiseExpires));
            if (labels.ImpactShockExpires > now)
                activeLabels.Add(BuildTimedLabel("Impact shock",
                    labels.ImpactShockExpires));
            return activeLabels.Count > 0 ? activeLabels : null;
        }

        private static BuffDescription BuildTimedLabel(string text,
            float expires)
        {
            float duration = Mathf.Max(0.01f, expires - Time.unscaledTime);
            return new BuffDescription(text,
                () => duration - Mathf.Max(0f, expires - Time.unscaledTime),
                duration);
        }
    }

    public sealed class NativeEffectDisplayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(HealthHelper),
                nameof(HealthHelper.GetDisplayVariation));

        [PatchPostfix]
        private static void ReplaceNativeEffectText(IHealthEffect effect,
            ref EffectDescription[] __result)
        {
            if (effect == null || __result == null)
                return;

            List<SimpleBuffDescription> labels = null;
            if (effect is IFracture &&
                (effect.BodyPart == EBodyPart.Chest ||
                 effect.BodyPart == EBodyPart.Stomach))
            {
                labels = new List<SimpleBuffDescription>
                {
                    new SimpleBuffDescription("Spinal fracture")
                };
            }
            else if (effect is ILightBleeding || effect is IHeavyBleeding)
            {
                labels = NativeEffectLabels.BuildBleedLabels(effect);
            }
            else if (effect is IPain)
            {
                labels = NativeEffectLabels.BuildActiveLabels(effect);
            }

            if (labels == null)
                return;
            for (int i = 0; i < __result.Length; i++)
                if (__result[i] != null)
                    __result[i].Replace(new List<SimpleBuffDescription>(labels));
        }
    }
}
