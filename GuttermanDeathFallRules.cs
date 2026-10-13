using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryGuttermanDeathHazard : MonoBehaviour { }

    internal static class GuttermanDeathFallRules
    {
        internal static bool BlocksEnemies(Component hazard) => UltraMiseryState.IsActive && hazard &&
            hazard.GetComponentInParent<UltraMiseryGuttermanDeathHazard>();

        internal static void Mark(GameObject hazard)
        {
            if (hazard && !hazard.GetComponent<UltraMiseryGuttermanDeathHazard>())
                hazard.AddComponent<UltraMiseryGuttermanDeathHazard>();
        }
    }

    [HarmonyPatch]
    internal static class GuttermanDeathHazardSetupPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Mark before animation events activate either object, including
            // dead-on-spawn Sandbox corpses. Do not disable player collisions.
            foreach (string method in new[] { "Start", "Death", "FallStart", "FallOver" })
                yield return AccessTools.Method(typeof(Gutterman), method);
        }

        private static void Prefix(GameObject ___fallingKillTrigger, GameObject ___fallEffect)
        {
            if (!UltraMiseryState.IsActive) return;
            GuttermanDeathFallRules.Mark(___fallingKillTrigger);
            GuttermanDeathFallRules.Mark(___fallEffect);
        }
    }

    [HarmonyPatch(typeof(DeathZone), "GotHit")]
    internal static class GuttermanDeathZoneNoEnemyHitPatch
    {
        private static bool Prefix(DeathZone __instance, Collider other) =>
            !GuttermanDeathFallRules.BlocksEnemies(__instance) || !FriendlyFireRules.GetEnemy(other);
    }

    [HarmonyPatch(typeof(HurtZone), "DamageEnemy")]
    internal static class GuttermanDeathHurtZoneNoEnemyHitPatch
    {
        private static bool Prefix(HurtZone __instance, ref bool __result)
        {
            if (!GuttermanDeathFallRules.BlocksEnemies(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class GuttermanDeathImpactNoEnemyHitPatch
    {
        private static bool Prefix(Explosion __instance, Collider other) =>
            !GuttermanDeathFallRules.BlocksEnemies(__instance) || !FriendlyFireRules.GetEnemy(other);
    }
}
