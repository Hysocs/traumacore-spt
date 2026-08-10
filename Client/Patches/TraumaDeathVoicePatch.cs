using EFT;
using HarmonyLib;

namespace TraumaCore.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
    internal static class TraumaDeathVoicePatch
    {
        private const float PreferredPhraseChance = 0.75f;
        private const float HeadDeathPhraseChance = 0.35f;
        [System.ThreadStatic] private static bool _replaceNativeVoice;
        [System.ThreadStatic] private static EPhraseTrigger _replacementPhrase;

        private static void Prefix(Player __instance)
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

        private static void Postfix(Player __instance)
        {
            if (!_replaceNativeVoice || __instance == null || __instance.Speaker == null)
                return;
            try
            {
                // Player.OnDead has finished its own sound handling, so this
                // cannot be immediately cancelled by Speaker.Shut().
                __instance.Speaker.Play(_replacementPhrase,
                    __instance.HealthStatus, true, null);
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogError("[DeathVoice] Failed to play forced death phrase: " +
                    exception);
            }
        }

        private static void Finalizer()
        {
            _replaceNativeVoice = false;
            _replacementPhrase = EPhraseTrigger.None;
        }

        internal static bool ReplacingNativeVoice
        { get { return _replaceNativeVoice; } }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ShouldVocalizeDeath))]
    internal static class SuppressReplacedDeathVoicePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (TraumaDeathVoicePatch.ReplacingNativeVoice)
                __result = false;
        }
    }
}
