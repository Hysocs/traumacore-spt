using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

using BleedingEffect = EFT.HealthSystem.ActiveHealthController.Bleeding;
using Berserk = EFT.HealthSystem.ActiveHealthController.Berserk;

namespace TraumaCore.Patches.Bleeding
{
    public sealed class BleedKillCreditPatch : ModulePatch
    {
        private static readonly FieldInfo LastAggressorField =
            AccessTools.Field(typeof(Player), "LastAggressor");

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(
                typeof(BleedingEffect),
                nameof(BleedingEffect.BleedHealth));

        [PatchPrefix]
        private static bool PatchPrefix(BleedingEffect __instance)
        {
            try
            {
                ActiveHealthController healthController = __instance.HealthController;
                if (healthController == null || healthController.Player == null)
                    return true;

                if (healthController.FindActiveEffect<Berserk>() != null)
                    return true;

                Player victim = healthController.Player;
                IPlayer lastAggressor = LastAggressorField?.GetValue(victim) as IPlayer;

                if (lastAggressor == null)
                    return true;

                float damagePerBodyPart = __instance.float_15;
                if (damagePerBodyPart <= 0f)
                    return true;

                DamageInfo bleedDamageInfo = __instance.DamageInfo;
                bool isBleedDamage =
                    bleedDamageInfo.DamageType == EDamageType.LightBleeding ||
                    bleedDamageInfo.DamageType == EDamageType.HeavyBleeding;
                if (!isBleedDamage)
                    return true;

                IObserverToPlayerBridge aggressorBridge = Singleton<GameWorld>
                    .Instance
                    .GetEverExistedBridgeByProfileID(lastAggressor.ProfileId);
                if (aggressorBridge == null)
                    return true;

                DamageInfo damageInfoWithAttacker = bleedDamageInfo;
                damageInfoWithAttacker.Player = aggressorBridge;

                float totalDamageApplied = 0f;
                foreach (EBodyPart realBodyPart in HealthHelper.RealBodyParts)
                {
                    totalDamageApplied += healthController.ApplyDamage(realBodyPart, damagePerBodyPart, damageInfoWithAttacker);
                }

                float damagePerSecond = -totalDamageApplied / __instance.float_16;
                if (Math.Abs(__instance.float_19 - damagePerSecond) >= float.Epsilon)
                {
                    __instance.SetHealthRatesPerSecond(
                        damagePerSecond / healthController.DamageMultiplier,
                        __instance.float_20,
                        0f,
                        0f);
                }

                return false;
            }
            catch (Exception exception)
            {
                TraumaLog.Error(
                    $"[BleedKillCreditPatch] Error: {exception}");
                return true;
            }
        }
    }
}
