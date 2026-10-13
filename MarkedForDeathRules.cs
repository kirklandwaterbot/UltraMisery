using System;
using System.Collections;
using HarmonyLib;
using ULTRAKILL.Cheats;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class MarkedForDeathRules
    {
        private static int reportedScene = -1;
        private static float nextCheck;
        internal static void Reset() { reportedScene = -1; nextCheck = 0f; }

        internal static bool IsSevenThree(Scene scene)
        {
            if (SceneHelper.IsPlayingCustom || !scene.IsValid() || !scene.isLoaded) return false;
            if (scene.handle == SceneManager.GetActiveScene().handle &&
                string.Equals(SceneHelper.CurrentScene, "Level 7-3", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(scene.name, "Level 7-3", StringComparison.OrdinalIgnoreCase)) return true;
            StatsManager stats = MonoSingleton<StatsManager>.Instance;
            return stats && stats.gameObject.scene.handle == scene.handle && stats.levelNumber == 28;
        }

        internal static bool Applies(EnemyIdentifier enemy) => UltraMiseryState.IsActive && enemy &&
            !enemy.dead && IsSevenThree(enemy.gameObject.scene);

        internal static IEnumerator ActivateAfterLoad(Scene scene) { yield return null; Tick(); }

        internal static void Tick()
        {
            if (!UltraMiseryState.IsActive || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            Scene scene = SceneManager.GetActiveScene();
            if (reportedScene == scene.handle || !IsSevenThree(scene)) return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (EnemyIdentifier enemy in root.GetComponentsInChildren<EnemyIdentifier>(true)) ApplyToEnemy(enemy);
            reportedScene = scene.handle;
            Plugin.Log.LogInfo("7-3 default player targeting enabled, including delayed waves and Puppets. Terminal interaction is optional.");
        }

        internal static void ApplyToEnemy(EnemyIdentifier enemy)
        {
            if (!Applies(enemy) || LifestealRules.AiCombatAllowed) return;
            // Same targeting preferences used by the native YES action.
            enemy.PlayerMarkedForDeath();
            enemy.ignorePlayer = false;
            // Discard a cached enemy/tree target instead of waiting for it to expire.
            if (!BlindEnemies.Blind && !EnemyIgnorePlayer.Active && !EnemiesHateEnemies.Active &&
                MonoSingleton<PlayerTracker>.Instance &&
                (enemy.target == null || !enemy.target.isPlayer || !enemy.target.isValid))
                enemy.target = EnemyTarget.TrackPlayer();
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "UpdateTarget")]
    internal static class SevenThreeMarkedTargetPatch
    {
        private static void Prefix(EnemyIdentifier __instance) => MarkedForDeathRules.ApplyToEnemy(__instance);
        private static void Postfix(EnemyIdentifier __instance) => MarkedForDeathRules.ApplyToEnemy(__instance);
    }

    // Vision queries also read these properties. Relationship/madness overrides
    // otherwise take precedence over the three fields changed by the YES action.
    [HarmonyPatch(typeof(EnemyIdentifier), "IgnorePlayer", MethodType.Getter)]
    internal static class SevenThreeIgnorePlayerPatch
    {
        private static void Postfix(EnemyIdentifier __instance, ref bool __result)
        {
            if (MarkedForDeathRules.Applies(__instance) && !LifestealRules.AiCombatAllowed && !BlindEnemies.Blind && !EnemyIgnorePlayer.Active)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "AttackEnemies", MethodType.Getter)]
    internal static class SevenThreeAttackPlayerPatch
    {
        private static void Postfix(EnemyIdentifier __instance, ref bool __result)
        {
            if (MarkedForDeathRules.Applies(__instance) && !LifestealRules.AiCombatAllowed) __result = false;
        }
    }
}
