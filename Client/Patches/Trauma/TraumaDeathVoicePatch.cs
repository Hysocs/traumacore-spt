using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraumaCore.Patches.Trauma
{
    public sealed class TraumaDeathVoicePatch : ModulePatch
    {
        private const float PreferredPhraseChance = 0.75f;
        private const float HeadDeathPhraseChance = 0.35f;
        [System.ThreadStatic] private static bool _replaceNativeVoice;
        [System.ThreadStatic] private static EPhraseTrigger _replacementPhrase;

        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player), nameof(Player.OnDead));

        [PatchPrefix]
        private static void PatchPrefix(Player __instance)
        {
            TraumaController trauma = __instance != null
                ? __instance.GetComponent<TraumaController>() : null;
            if (!OrganSystem.Enabled.Value || trauma == null ||
                (!trauma.TraumaDeathVoicePending && !trauma.HeadDeathVoicePending) ||
                __instance.Speaker == null)
                return;

            if (trauma.HeadDeathVoicePending)
            {
                if (UnityEngine.Random.value >= HeadDeathPhraseChance) return;
                _replaceNativeVoice = true;
                _replacementPhrase = EPhraseTrigger.OnDeath;
                return;
            }

            _replaceNativeVoice = true;
            EPhraseTrigger preferred = trauma.HeartDeathVoicePending
                ? EPhraseTrigger.OnDeath : EPhraseTrigger.OnAgony;
            EPhraseTrigger alternate = trauma.HeartDeathVoicePending
                ? EPhraseTrigger.OnAgony : EPhraseTrigger.OnDeath;
            _replacementPhrase = UnityEngine.Random.value < PreferredPhraseChance
                ? preferred : alternate;
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance)
        {
            if (!_replaceNativeVoice || __instance == null || __instance.Speaker == null)
                return;
            try
            {
                __instance.Speaker.Play(_replacementPhrase,
                    __instance.HealthStatus, true, null);
            }
            catch (System.Exception exception)
            {
                TraumaLog.Error("[DeathVoice] Failed to play forced death phrase: " +
                    exception);
            }
        }

        [PatchFinalizer]
        private static void PatchFinalizer()
        {
            _replaceNativeVoice = false;
            _replacementPhrase = EPhraseTrigger.None;
        }

        internal static bool ReplacingNativeVoice
        { get { return _replaceNativeVoice; } }
    }

    public sealed class SuppressReplacedDeathVoicePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() =>
            AccessTools.Method(typeof(Player),
                nameof(Player.ShouldVocalizeDeath));

        [PatchPostfix]
        private static void PatchPostfix(ref bool __result)
        {
            if (TraumaDeathVoicePatch.ReplacingNativeVoice)
                __result = false;
        }
    }
}
