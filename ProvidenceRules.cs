using System;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class DroneDeathDropRules
    {
        internal static void RemoveDrop(Drone drone)
        {
            if (!UltraMiseryState.IsActive || !drone)
            {
                return;
            }

            EnemyIdentifier enemy = drone.GetComponent<EnemyIdentifier>();
            if (enemy && (enemy.enemyType == EnemyType.Providence ||
                enemy.enemyType == EnemyType.Virtue))
            {
                // Remove extra spawned drops. Virtue's physical core is instead
                // the crashing Drone itself, handled separately after Death.
                drone.spawnOnDeath = Array.Empty<UnityEngine.AddressableAssets.AssetReference>();
            }
        }
    }

    [HarmonyPatch(typeof(Drone), "Start")]
    internal static class ProvidenceNoDeathDropStartPatch
    {
        private static void Postfix(Drone __instance)
        {
            DroneDeathDropRules.RemoveDrop(__instance);
        }
    }

    [HarmonyPatch(typeof(Drone), "Death")]
    internal static class ProvidenceNoDeathDropDeathPatch
    {
        private static void Prefix(Drone __instance)
        {
            DroneDeathDropRules.RemoveDrop(__instance);
        }

        private static void Postfix(Drone __instance, ref bool ___canInterruptCrash)
        {
            if (!UltraMiseryState.IsActive || !__instance || !__instance.crashing) return;
            EnemyIdentifier enemy = __instance.GetComponent<EnemyIdentifier>();
            if (!enemy || enemy.enemyType != EnemyType.Virtue) return;

            // Run after stock Death so insignias and normal death bookkeeping
            // are retained. The core is this body's five-second crashing state.
            // Destroy is deferred by Unity, so disable collision/rendering now.
            foreach (Collider collider in __instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in __instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            __instance.CancelInvoke("Explode");
            __instance.CancelInvoke("CanInterruptCrash");
            ___canInterruptCrash = true;
            // Stock Explode guards reentry with 'exploded' and performs cleanup,
            // including music/drillers. Retain its normal death burst.
            __instance.Explode();
        }
    }

    [HarmonyPatch(typeof(Drone), "Explode")]
    internal static class ProvidenceNoDeathDropExplodePatch
    {
        private static void Prefix(Drone __instance)
        {
            // Guard extra spawned drops, including forced/instant explosions.
            DroneDeathDropRules.RemoveDrop(__instance);
        }
    }
}
