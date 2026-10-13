using System;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class EnvironmentalImmunityRules
    {
        internal static bool PreventFallDeath(Enemy enemy)
        {
            if (!UltraMiseryState.IsActive || !enemy) return false;
            EnemyIdentifier eid = enemy.GetComponent<EnemyIdentifier>();
            if (!eid || eid.dead) return false;
            if (eid.enemyType != EnemyType.Power || SceneHelper.IsPlayingCustom) return true;
            Scene scene = enemy.gameObject.scene;
            // Only the campaign 8-3 Power keeps native fall/terminal-velocity rules.
            return !(scene.IsValid() && scene.isLoaded &&
                (string.Equals(scene.name, "Level 8-3", StringComparison.OrdinalIgnoreCase) ||
                 (scene.handle == SceneManager.GetActiveScene().handle &&
                  string.Equals(SceneHelper.CurrentScene, "Level 8-3", StringComparison.OrdinalIgnoreCase))));
        }
    }

    [HarmonyPatch(typeof(Enemy), "ShouldSplat")]
    internal static class EnemyLandingImmunityPatch
    {
        private static bool Prefix(Enemy __instance, ref bool __result)
        {
            if (!EnvironmentalImmunityRules.PreventFallDeath(__instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Enemy), "UpdateFallingState")]
    internal static class StatueLandingImmunityPatch
    {
        private static void Prefix(Enemy __instance, out bool? __state)
        {
            __state = null;
            if (!EnvironmentalImmunityRules.PreventFallDeath(__instance)) return;
            __state = __instance.noFallDamage;
            __instance.noFallDamage = true;
        }

        private static void Finalizer(Enemy __instance, bool? __state)
        {
            if (__instance && __state.HasValue) __instance.noFallDamage = __state.Value;
        }
    }

    [HarmonyPatch(typeof(Enemy), "UpdateFalling")]
    internal static class EnemyTerminalVelocityImmunityPatch
    {
        private static void Prefix(Enemy __instance, ref float ___maxFallSpeed,
            ref float ___terminalVelocityTimer, out float? __state)
        {
            __state = null;
            if (!EnvironmentalImmunityRules.PreventFallDeath(__instance)) return;
            __state = ___maxFallSpeed;
            // Disable only the terminal-velocity branch; landing, navigation,
            // gravity and void/kill-volume collision handling still run normally.
            ___maxFallSpeed = float.NegativeInfinity;
            ___terminalVelocityTimer = 0f;
            if (__instance.currentTerminalVelocityEffect)
                UnityEngine.Object.Destroy(__instance.currentTerminalVelocityEffect);
        }

        private static void Finalizer(ref float ___maxFallSpeed, float? __state)
        {
            if (__state.HasValue) ___maxFallSpeed = __state.Value;
        }
    }

    [HarmonyPatch(typeof(HurtZone), "DamageEnemy")]
    internal static class EnvironmentalHurtZoneImmunityPatch
    {
        private static bool Prefix(HurtZone __instance, EnemyIdentifier eid, ref bool __result)
        {
            if (!UltraMiseryState.IsActive || !eid || eid.dead) return true;
            // HurtZone also implements the player's held chainsaw and some
            // enemy attacks. They are not environmental hazards.
            if (__instance.sourceWeapon || __instance.damageType == EnviroDamageType.Chainsaw ||
                LifestealRules.Resolve(__instance)) return true;
            // HurtZone applies ordinary damage ticks, not authored instant kills.
            // DeathZone, Water's death handling and SpinZone stay untouched.
            __result = true; // Keep the native tick cooldown; do not retry every frame.
            return false;
        }
    }
}
