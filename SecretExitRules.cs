using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class SecretExitRules
    {
        internal static bool Enabled => MissionRestartRules.Enforced && !SceneHelper.IsPlayingCustom &&
            !MonoSingleton<SandboxNavmesh>.Instance && IsCampaign(SceneHelper.CurrentScene);

        internal static bool IsCampaign(string scene) => !string.IsNullOrEmpty(scene) && scene.StartsWith("Level ", StringComparison.Ordinal);

        internal static string CanonicalScene(string scene)
        {
            if (string.IsNullOrWhiteSpace(scene)) return "";
            scene = scene.Trim().Replace('\\', '/');
            const string prefix = "Assets/Scenes/";
            if (scene.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) scene = scene.Substring(prefix.Length);
            if (scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) scene = scene.Substring(0, scene.Length - 6);
            if (scene.StartsWith("Level ", StringComparison.OrdinalIgnoreCase)) scene = "Level " + scene.Substring(6).ToUpperInvariant();
            return scene;
        }

        internal static bool IsSecretDestination(string scene)
        {
            scene = CanonicalScene(scene);
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
        private static bool Prefix(SecretMissionPit __instance, Collider other)
        {
            if (!SecretExitRules.Enabled) return true;
            // Discovery markers can sit outside the pit: only actual entry is
            // punished, while the existing unlock suppression remains in force.
            if (!__instance.halfUnlock && other && other.CompareTag("Player"))
                MissionRestartRules.Request("secret mission entrance");
            return false;
        }
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
        private static bool Prefix(FinalPit __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!SecretExitRules.Blocks(__instance)) return true;
            if (__originalMethod.Name == "SendInfo" ||
                (__args.Length > 0 && __args[0] is Collider other && other && other.CompareTag("Player")))
            {
                MissionRestartRules.Request("secret exit: " + __instance.targetLevelName);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class SecretExitScriptedChangePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "AbruptChangeLevel", "NormalChangeLevel", "PositionChangeLevel" })
                yield return typeof(AbruptLevelChanger).GetMethod(name);
        }
        private static bool Prefix(string levelname)
        {
            MissionRestartRules.ObserveLoad(levelname);
            if (!SecretExitRules.Enabled || !SecretExitRules.IsSecretDestination(levelname)) return true;
            MissionRestartRules.Request("scripted secret exit: " + levelname);
            return false;
        }
    }

    [HarmonyPatch(typeof(AbruptLevelChanger), "GoToLevel")]
    internal static class SecretExitNumberedChangePatch
    {
        private static bool Prefix(int missionNumber)
        {
            string destination = GetMissionName.GetSceneName(missionNumber);
            if (!SecretExitRules.Enabled || !SecretExitRules.IsSecretDestination(destination)) return true;
            MissionRestartRules.Request("numbered secret exit: " + destination);
            return false;
        }
    }
}
