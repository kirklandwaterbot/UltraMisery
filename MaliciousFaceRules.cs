using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class MaliciousFaceRules
    {
        internal static void EnsureNativeRage(MaliciousFace face)
        {
            if (!UltraMiseryState.IsActive || !face) return;
            EnemyIdentifier enemy = face.GetComponent<EnemyIdentifier>();
            if (!enemy || enemy.dead) return;
            UltraMiseryForcedEnrage forced = enemy.GetComponent<UltraMiseryForcedEnrage>() ??
                enemy.gameObject.AddComponent<UltraMiseryForcedEnrage>();
            forced.EnsureAudioPolicy(enemy);
            if (!face.isEnraged)
            {
                // A generic effect created earlier must not coexist with native rage.
                if (forced.GenericEnrageApplied && !forced.NativeEnrageApplied)
                    forced.CleanupPresentation();
                face.Enrage();
            }
            forced.NativeEnrageApplied = face.isEnraged;
            forced.DirectEnrageApplied = face.isEnraged;
            forced.MaintainPresentation(enemy);
            // Remove any previously applied generic modifier during reconciliation.
            enemy.UpdateBuffs(false, false);
        }
    }

    [HarmonyPatch(typeof(MaliciousFace), "Start")]
    internal static class MaliciousFaceSpawnRagePatch
    {
        // Awake/Start initialize the native fields; finish before its first Update.
        private static void Postfix(MaliciousFace __instance) => MaliciousFaceRules.EnsureNativeRage(__instance);
    }

    [HarmonyPatch(typeof(MaliciousFace), "UnEnrage")]
    internal static class MaliciousFacePermanentRagePatch
    {
        private static bool Prefix(MaliciousFace __instance)
        {
            return !EnemyRules.ShouldPreventNativeUnEnrage(__instance);
        }
    }
}
