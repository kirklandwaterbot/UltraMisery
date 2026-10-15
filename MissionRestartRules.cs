using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Logic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class MissionRestartRules
    {
        private static int requestedHandle = -1;
        private static string lastRegularMission;
        private static string allowedSecretLoad;
        internal static bool CheatsEnabled => MonoSingleton<CheatsController>.TryGetInstance(out var cheats) && cheats.cheatsEnabled;
        internal static bool Enforced => UltraMiseryState.IsActive && !CheatsEnabled;
        private static readonly Dictionary<string, string> quotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Level 0-1", "PIPE CLIP LIVES" },
            { "Level 0-2", "WHAT'S UPDOOR?" },
            { "Level 1-S", "LOOKS LIKE YOU'RE AT WIT'S END" },
            { "Level 2-4", "OFF THE BEATEN TRACK" },
            { "Level 3-1", "YUP, THAT'S A CAVITY" },
            { "Level 4-3", "THE FILTH IS GONE, BUT THE MEMORY REMAINS" }
        };

        internal static string NormalizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "";
            message = Regex.Replace(message, "<[^>]*>", "");
            message = message.Replace('$', ' ').Replace('\u2019', '\'').Replace('\u2018', '\'')
                .Replace('\u201c', '"').Replace('\u201d', '"');
            return Regex.Replace(message, @"\s+", " ").Trim();
        }

        internal static bool IsForbiddenQuote(string scene, string message)
        {
            if (!quotes.TryGetValue(SecretExitRules.CanonicalScene(scene), out string quote)) return false;
            string normalized = NormalizeMessage(message);
            // Exact authored quote, with optional quotation marks/attribution.
            // Unrelated messages containing one of these phrases do not count.
            foreach (string text in new[] { quote, "\"" + quote + "\"" })
                if (normalized.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(text + " T. HAKITA", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool ShouldRestartForQuote(bool active, bool custom, bool cheats, string scene, string message)
            => active && !custom && !cheats && IsForbiddenQuote(scene, message);

        internal static bool IsRegularMission(string scene)
        {
            scene = SecretExitRules.CanonicalScene(scene);
            return scene.Length == 9 && scene.StartsWith("Level ", StringComparison.OrdinalIgnoreCase) &&
                scene[6] >= '0' && scene[6] <= '9' && scene[7] == '-' &&
                ((scene[8] >= '1' && scene[8] <= '9') || char.ToUpperInvariant(scene[8]) == 'E');
        }

        internal static string RestartTarget(string current, string previous)
        {
            if (IsRegularMission(current)) return SecretExitRules.CanonicalScene(current);
            if (IsRegularMission(previous)) return SecretExitRules.CanonicalScene(previous);
            return "Level 0-1";
        }

        internal static bool ShouldRejectLoad(bool active, bool cheats, string destination)
            => active && !cheats && SecretExitRules.IsSecretDestination(destination);

        internal static void ObserveLoad(string destination) => TrackLoad(CheatsEnabled, destination);

        internal static void TrackLoad(bool cheats, string destination)
        {
            // Cheats may reset during loading. Honor an entry authorized before
            // that reset, instead of rejecting it at the new scene's callback.
            allowedSecretLoad = cheats && SecretExitRules.IsSecretDestination(destination)
                ? SecretExitRules.CanonicalScene(destination) : null;
        }

        internal static bool ConsumeAllowedEntry(string scene)
        {
            bool allowed = allowedSecretLoad != null && string.Equals(allowedSecretLoad, SecretExitRules.CanonicalScene(scene), StringComparison.Ordinal);
            allowedSecretLoad = null;
            return allowed;
        }

        internal static string RuntimeRestartTarget(string current)
            => RestartTarget(current, lastRegularMission ?? SceneHelper.LastScene);

        internal static void Request(string reason)
        {
            if (!Enforced || !Plugin.Instance) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || requestedHandle == scene.handle) return;
            requestedHandle = scene.handle;
            Plugin.Log?.LogWarning("UltraMisery: full mission restart requested: " + reason);
            Plugin.Instance.StartCoroutine(RestartWhenReady(scene.handle));
        }

        private static IEnumerator RestartWhenReady(int sceneHandle)
        {
            // Leave the current Unity trigger/message callback before unloading.
            yield return null;
            while (SceneHelper.PendingScene != null)
            {
                if (!Enforced) { if (requestedHandle == sceneHandle) requestedHandle = -1; yield break; }
                if (SceneManager.GetActiveScene().handle != sceneHandle) yield break;
                yield return null;
            }
            if (!Enforced || SceneManager.GetActiveScene().handle != sceneHandle)
            {
                if (requestedHandle == sceneHandle) requestedHandle = -1;
                yield break;
            }
            string target = RuntimeRestartTarget(SceneManager.GetActiveScene().name);
            ClearTransferPosition();
            // A full scene reload discards all checkpoint clones and run state.
            yield return SceneHelper.LoadSceneAsync(target, false);
            ResetMapStores();
        }

        internal static void ClearTransferPosition()
        {
            // PositionChangeLevel may have created a persistent transfer object.
            // A punishment restart must use the authored mission spawn instead.
            foreach (PlayerPosInfo position in UnityEngine.Object.FindObjectsOfType<PlayerPosInfo>())
                UnityEngine.Object.Destroy(position.gameObject);
        }

        private static void ResetMapStores()
        {
            if (MonoSingleton<MapVarManager>.Instance) MonoSingleton<MapVarManager>.Instance.ResetStores();
        }

        internal static IEnumerator FinishRedirect(Coroutine load)
        {
            yield return load;
            ResetMapStores();
        }

        internal static bool OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return false;
            requestedHandle = -1;
            if (IsRegularMission(scene.name)) lastRegularMission = SecretExitRules.CanonicalScene(scene.name);
            bool allowedEntry = ConsumeAllowedEntry(scene.name);
            if (allowedEntry || !ShouldRejectLoad(UltraMiseryState.IsActive, CheatsEnabled, scene.name)) return false;
            // Safety net for loaders that bypass SceneHelper altogether. Wait for
            // PendingScene to clear before requesting another native scene load.
            Request("secret scene loaded: " + scene.name);
            return true;
        }
    }

    [HarmonyPatch(typeof(HudMessage), "PlayMessage")]
    internal static class ForbiddenQuoteRestartPatch
    {
        private static bool Prefix(HudMessage __instance, bool hasToBeEnabled, bool ___activated)
        {
            if (__instance.deactivating || (___activated && !__instance.notOneTime) ||
                (hasToBeEnabled && (!__instance.gameObject.activeInHierarchy || !__instance.enabled))) return true;
            if (!MissionRestartRules.ShouldRestartForQuote(UltraMiseryState.IsActive, SceneHelper.IsPlayingCustom,
                MissionRestartRules.CheatsEnabled, SceneHelper.CurrentScene, __instance.message)) return true;
            MissionRestartRules.Request("out-of-bounds quote: " + MissionRestartRules.NormalizeMessage(__instance.message));
            return false;
        }
    }

    [HarmonyPatch(typeof(SceneHelper), "LoadSceneAsync")]
    internal static class SecretSceneLoadRestartPatch
    {
        private static void Prefix(ref string sceneName, ref bool noBlocker, out bool __state)
        {
            __state = false;
            if (SceneHelper.PendingScene != null) return;
            MissionRestartRules.ObserveLoad(sceneName);
            if (!MissionRestartRules.ShouldRejectLoad(UltraMiseryState.IsActive, MissionRestartRules.CheatsEnabled, sceneName)) return;
            string requested = sceneName;
            sceneName = MissionRestartRules.RuntimeRestartTarget(SceneManager.GetActiveScene().name);
            noBlocker = false;
            MissionRestartRules.ClearTransferPosition();
            __state = true;
            Plugin.Log?.LogWarning("UltraMisery: secret load " + requested + " replaced with mission restart " + sceneName);
        }

        private static void Postfix(Coroutine __result, bool __state)
        {
            if (__state && __result != null && Plugin.Instance)
                Plugin.Instance.StartCoroutine(MissionRestartRules.FinishRedirect(__result));
        }
    }
}
