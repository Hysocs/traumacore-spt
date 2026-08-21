using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using EFT;
using EFT.HealthSystem;
using EFT.UI.Health;
using TraumaCore.Features.DeathScreen.DamageTracking;

namespace TraumaCore.Features.DeathScreen.Tooltips
{
    internal static class BleedDamageTooltipBuilder
    {
        private static readonly ConditionalWeakTable<
            DamagePanel.BodyPartDamageList,
            BleedTooltipSummary> SummaryByDamageList = new();

        internal static void CaptureBleedSummary(
            DamagePanel.BodyPartDamageList damageList,
            EBodyPart bodyPart,
            IHealthController healthController)
        {
            if (damageList == null ||
                damageList.ResultType != DamageStats.EDamageResult.Regular)
                return;

            Profile profile = (healthController as ActiveHealthController)?.Player?.Profile;
            ValueStruct bodyPartHealth = healthController != null
                ? healthController.GetBodyPartHealth(bodyPart)
                : default;

            float lightDamage = 0f;
            float heavyDamage = 0f;
            float bleedTickSamples = 0f;
            foreach (DamageStats damage in damageList)
            {
                if (damage.Type == EDamageType.LightBleeding)
                {
                    lightDamage += damage.Amount;
                    bleedTickSamples += damage.ImpactsCount;
                }
                else if (damage.Type == EDamageType.HeavyBleeding)
                {
                    heavyDamage += damage.Amount;
                    bleedTickSamples += damage.ImpactsCount;
                }
            }

            float durationSeconds = 0f;
            float damagePerSecond = 0f;
            bool isRateEstimated = false;
            if (DeathScreenDamageTracker.TryGetRecordedDamage(
                profile,
                bodyPart,
                out DeathScreenDamageTracker.BodyPartDamageRecord recordedDamage))
            {
                lightDamage = recordedDamage.LightBleedDamage;
                heavyDamage = recordedDamage.HeavyBleedDamage;
                durationSeconds = recordedDamage.BleedDurationSeconds;
                damagePerSecond = recordedDamage.AverageBleedDamagePerSecond;
            }
            else if (bleedTickSamples > 0f)
            {
                durationSeconds = bleedTickSamples / 60f;
                damagePerSecond = (lightDamage + heavyDamage) / durationSeconds;
                isRateEstimated = true;
            }

            SummaryByDamageList.Remove(damageList);
            SummaryByDamageList.Add(damageList, new BleedTooltipSummary(
                bodyPart,
                bodyPartHealth.Current,
                bodyPartHealth.Maximum,
                lightDamage,
                heavyDamage,
                durationSeconds,
                damagePerSecond,
                isRateEstimated));
        }

        internal static bool TryBuildTooltip(
            List<DamageStats> damageList,
            out string tooltip)
        {
            tooltip = null;
            if (!(damageList is DamagePanel.BodyPartDamageList bodyPartDamage) ||
                !SummaryByDamageList.TryGetValue(
                    bodyPartDamage,
                    out BleedTooltipSummary summary) ||
                summary.TotalBleedDamage <= 0f)
                return false;

            StringBuilder tooltipBuilder = new StringBuilder(256);
            foreach (DamageStats damage in damageList)
                if (!DeathScreenDamageTracker.IsBleeding(damage.Type))
                    tooltipBuilder.AppendLine(damage.ToString());

            if (tooltipBuilder.Length > 0)
                tooltipBuilder.AppendLine();

            tooltipBuilder.Append("<b>BLEEDING REPORT - ")
                .Append(summary.BodyPart)
                .AppendLine("</b>");

            if (summary.MaximumHealth > 0f)
                tooltipBuilder.Append("Current health: ")
                    .Append(summary.CurrentHealth.ToString("0.#"))
                    .Append(" / ")
                    .Append(summary.MaximumHealth.ToString("0.#"))
                    .AppendLine(" HP");

            tooltipBuilder.Append("Total bleed drain: ")
                .Append(summary.TotalBleedDamage.ToString("0.#"))
                .AppendLine(" HP");

            if (summary.LightDamage > 0f)
                tooltipBuilder.Append("  Light/treatable wound drain: ")
                    .Append(summary.LightDamage.ToString("0.#"))
                    .AppendLine(" HP");

            if (summary.HeavyDamage > 0f)
                tooltipBuilder.Append("  Heavy/organ wound drain: ")
                    .Append(summary.HeavyDamage.ToString("0.#"))
                    .AppendLine(" HP");

            if (summary.DurationSeconds > 0f)
            {
                tooltipBuilder.Append(summary.IsRateEstimated
                        ? "Estimated drain time: "
                        : "Observed drain time: ")
                    .Append(summary.DurationSeconds.ToString("0.0"))
                    .AppendLine(" seconds");
                tooltipBuilder.Append(summary.IsRateEstimated
                        ? "Estimated drain rate: "
                        : "Average drain rate: ")
                    .Append(summary.DamagePerSecond.ToString("0.00"))
                    .Append(" HP/s");
            }
            else
            {
                tooltipBuilder.Append("Drain rate: unavailable for damage recorded before TraumaCore tracking");
            }

            tooltip = tooltipBuilder.ToString();
            return true;
        }

        private sealed class BleedTooltipSummary
        {
            public readonly EBodyPart BodyPart;
            public readonly float CurrentHealth;
            public readonly float MaximumHealth;
            public readonly float LightDamage;
            public readonly float HeavyDamage;
            public readonly float DurationSeconds;
            public readonly float DamagePerSecond;
            public readonly bool IsRateEstimated;
            public float TotalBleedDamage => LightDamage + HeavyDamage;

            public BleedTooltipSummary(
                EBodyPart bodyPart,
                float currentHealth,
                float maximumHealth,
                float lightDamage,
                float heavyDamage,
                float durationSeconds,
                float damagePerSecond,
                bool isRateEstimated)
            {
                BodyPart = bodyPart;
                CurrentHealth = currentHealth;
                MaximumHealth = maximumHealth;
                LightDamage = lightDamage;
                HeavyDamage = heavyDamage;
                DurationSeconds = durationSeconds;
                DamagePerSecond = damagePerSecond;
                IsRateEstimated = isRateEstimated;
            }
        }
    }

}
