using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace UltraMisery
{
    internal static class SecretExitRules
    {
        internal static bool Enabled => UltraMiseryState.IsActive && !SceneHelper.IsPlayingCustom &&
            !MonoSingleton<SandboxNavmesh>.Instance && IsCampaign(SceneHelper.CurrentScene);

        internal static bool IsCampaign(string scene) => !string.IsNullOrEmpty(scene) && scene.StartsWith("Level ", StringComparison.Ordinal);

        internal static bool IsSecretDestination(string scene)
        {
            if (string.IsNullOrWhiteSpace(scene)) return false;
            scene = scene.Trim();
            // Native addressable scene names, including future S/Prime slots.
            return scene.Length == 9 && scene.StartsWith("Level ", StringComparison.OrdinalIgnoreCase) &&
                ((scene[6] >= '0' && scene[6] <= '9' && scene[7] == '-' && char.ToUpperInvariant(scene[8]) == 'S') ||
                 (char.ToUpperInvariant(scene[6]) == 'P' && scene[7] == '-' && scene[8] >= '1' && scene[8] <= '9'));
        }

        internal static bool Blocks(FinalPit pit) => Enabled &&
            (IsSecretDestination(pit.targetLevelName) || pit.GetComponent<SecretMissionPit>());
    }

    // SecretMissionPit only updates save/unlock flags. It does NOT handle the
    // elevator transition; disabling it alone left the actual exit operational.
    [HarmonyPatch(typeof(SecretMissionPit), "OnTriggerEnter")]
    internal static class SecretExitUnlockPatch
    {
        private static bool Prefix() => !SecretExitRules.Enabled;
    }

    [HarmonyPatch]
    internal static class SecretExitPitPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "OnTriggerEnter", "OnTriggerStay", "SendInfo" })
                yield return typeof(FinalPit).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }
        // Block before the pit disables player control, stops the timer or
        // schedules SendInfo. Also applies to checkpoint-created pit copies.
        private static bool Prefix(FinalPit __instance) => !SecretExitRules.Blocks(__instance);
    }

    [HarmonyPatch]
    internal static class SecretExitScriptedChangePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "AbruptChangeLevel", "NormalChangeLevel", "PositionChangeLevel" })
                yield return typeof(AbruptLevelChanger).GetMethod(name);
        }
        private static bool Prefix(string levelname) => !SecretExitRules.Enabled || !SecretExitRules.IsSecretDestination(levelname);
    }

    [HarmonyPatch(typeof(AbruptLevelChanger), "GoToLevel")]
    internal static class SecretExitNumberedChangePatch
    {
        private static bool Prefix(int missionNumber) => !SecretExitRules.Enabled ||
            !SecretExitRules.IsSecretDestination(GetMissionName.GetSceneName(missionNumber));
    }
}
