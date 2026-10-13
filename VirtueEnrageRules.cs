using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryVirtueInitialized : MonoBehaviour { }

    internal static class VirtueEnrageRules
    {
        internal static bool Applies(Drone drone)
        {
            if (!UltraMiseryState.IsActive || !drone || drone.crashing) return false;
            EnemyIdentifier enemy = drone.GetComponent<EnemyIdentifier>();
            return enemy && !enemy.dead && !enemy.puppet && enemy.enemyType == EnemyType.Virtue;
        }

        internal static void EnsureNativeRage(Drone drone)
        {
            // Start locates charge particles. Do not add rage particles before
            // that search, or they can be mistaken for the charging effect.
            if (!Applies(drone) || !drone.GetComponent<UltraMiseryVirtueInitialized>()) return;
            EnemyIdentifier enemy = drone.GetComponent<EnemyIdentifier>();
            UltraMiseryForcedEnrage forced = enemy.GetComponent<UltraMiseryForcedEnrage>() ??
                enemy.gameObject.AddComponent<UltraMiseryForcedEnrage>();
            forced.EnsureAudioPolicy(enemy);
            drone.CancelInvoke("Enrage");
            bool changed = !forced.NativeEnrageApplied;
            if (!drone.isEnraged)
            {
                if (forced.GenericEnrageApplied && !forced.NativeEnrageApplied) forced.CleanupPresentation();
                drone.Enrage();
                changed = true;
            }
            forced.NativeEnrageApplied = drone.isEnraged;
            forced.DirectEnrageApplied = drone.isEnraged;
            forced.MaintainPresentation(enemy);
            if (changed) enemy.UpdateBuffs(false, false);
        }
    }

    [HarmonyPatch(typeof(Drone), "Start")]
    internal static class VirtueSpawnNativeRagePatch
    {
        private static void Postfix(Drone __instance)
        {
            if (!__instance.GetComponent<UltraMiseryVirtueInitialized>())
                __instance.gameObject.AddComponent<UltraMiseryVirtueInitialized>();
            VirtueEnrageRules.EnsureNativeRage(__instance);
        }
    }

    [HarmonyPatch(typeof(Drone), "OnEnable")]
    internal static class VirtueReactivationNativeRagePatch
    {
        private static void Postfix(Drone __instance) => VirtueEnrageRules.EnsureNativeRage(__instance);
    }

    [HarmonyPatch(typeof(Drone), "UnEnrage")]
    internal static class VirtuePermanentNativeRagePatch
    {
        private static bool Prefix(Drone __instance) => !VirtueEnrageRules.Applies(__instance);
    }
}
