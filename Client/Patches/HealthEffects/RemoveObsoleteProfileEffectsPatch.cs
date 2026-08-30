using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.HealthEffects
{
    /// <summary>
    /// Removes effect records written by older TraumaCore versions before EFT
    /// attempts to construct effect types that no longer exist.
    /// </summary>
    public sealed class RemoveObsoleteProfileEffectsPatch : ModulePatch
    {
        private static readonly string[] ObsoleteEffectNames =
        {
            "BruisedHealthEffect",
            "HeartWoundHealthEffect",
            "HitPressureHealthEffect",
            "SpinalFractureHealthEffect"
        };

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Constructor(typeof(OfflineHealthController), new[]
            {
                typeof(Profile.HealthInfo),
                typeof(InventoryController),
                typeof(SkillManager),
                typeof(bool)
            });

        [PatchPrefix]
        private static void RemoveObsoleteEffects(Profile.HealthInfo profileHealth)
        {
            if (profileHealth?.BodyParts == null)
                return;

            int removedEffectCount = 0;
            foreach (KeyValuePair<EBodyPart, Profile.HealthInfo.BodyPartInfo> bodyPart
                in profileHealth.BodyParts)
            {
                Dictionary<string, Profile.HealthInfo.EffectInfo> effects =
                    bodyPart.Value?.Effects;
                if (effects == null)
                    continue;

                foreach (string effectName in ObsoleteEffectNames)
                {
                    if (effects.Remove(effectName))
                        removedEffectCount++;
                }
            }

            if (removedEffectCount > 0)
            {
                TraumaLog.Warning(
                    $"Removed {removedEffectCount} obsolete TraumaCore health effect record(s) from profile health.");
            }
        }
    }
}
