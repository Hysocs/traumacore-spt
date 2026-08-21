using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.HealthEffects
{
    public sealed class LegacyHealthEffectCleanupPatch : ModulePatch
    {
        private static readonly string[] LegacyEffectNames =
        {
            "HeartWoundHealthEffect",
            "SpinalFractureHealthEffect"
        };

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Constructor(typeof(OfflineHealthController), new[]
            {
                typeof(Profile.HealthInfo), typeof(InventoryController),
                typeof(SkillManager), typeof(bool)
            });

        [PatchPrefix]
        private static void PatchPrefix(Profile.HealthInfo profileHealth)
        {
            RemoveLegacyEffects(profileHealth, true);
        }

        internal static int RemoveLegacyEffects(Profile.HealthInfo profileHealth,
            bool logRemoval)
        {
            if (profileHealth == null || profileHealth.BodyParts == null) return 0;
            int removed = 0;
            foreach (KeyValuePair<EBodyPart, Profile.HealthInfo.BodyPartInfo> bodyPart
                in profileHealth.BodyParts)
            {
                Dictionary<string, Profile.HealthInfo.EffectInfo> effects =
                    bodyPart.Value != null ? bodyPart.Value.Effects : null;
                if (effects == null) continue;
                for (int i = 0; i < LegacyEffectNames.Length; i++)
                    if (effects.Remove(LegacyEffectNames[i])) removed++;
            }
            if (removed > 0 && logRemoval)
                Plugin.Log.LogWarning("Removed " + removed +
                    " legacy persistent TraumaCore health effect(s) from profile health.");
            return removed;
        }
    }

}
