using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class BoundaryRules
    {
        private static int sceneHandle = -1;
        private static bool crossed;
        internal static bool InvalidRun => sceneHandle == SceneManager.GetActiveScene().handle && crossed;
        internal static void Reset()
        {
            sceneHandle = SceneManager.GetActiveScene().handle;
            crossed = false;
        }
        internal static bool ShouldRecord(bool active, bool custom, bool sandbox, bool cheats, bool alive, bool playerVolume)
            => active && !custom && !sandbox && !cheats && alive && playerVolume;

        [HarmonyPatch(typeof(OutOfBounds), "OnTriggerEnter")]
        private static class AuthoredBoundaryPatch
        {
            private static void Prefix(OutOfBounds __instance, Collider other)
            {
                var player = MonoSingleton<NewMovement>.Instance;
                var cheats = MonoSingleton<CheatsController>.Instance;
                if (!other || !other.CompareTag("Player") ||
                    !ShouldRecord(UltraMiseryState.IsActive, SceneHelper.IsPlayingCustom,
                        MonoSingleton<SandboxNavmesh>.Instance, cheats && cheats.cheatsEnabled,
                        player && player.hp > 0 && !player.levelOver,
                        __instance.targets != AffectedSubjects.EnemiesOnly)) return;
                if (sceneHandle != SceneManager.GetActiveScene().handle) Reset();
                if (crossed) return;
                crossed = true;
                Plugin.Log.LogWarning("UltraMisery: player entered authored OutOfBounds volume without cheats; this run cannot receive P rank. Volume: " + __instance.name);
                // Let the stock volume perform its recovery. Never infer bounds
                // from height, NavMesh, missing floor or movement speed.
            }
        }
    }
}
