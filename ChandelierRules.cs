using System;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class ChandelierRules
    {
        internal static bool IsChandelierLabel(string value) => !string.IsNullOrEmpty(value) &&
            value.IndexOf("chandelier", StringComparison.OrdinalIgnoreCase) >= 0;
        internal static bool Matches(Component hazard)
        {
            if (!UltraMiseryState.IsActive || !hazard) return false;
            if (hazard is DeathZone zone && IsChandelierLabel(zone.deathType)) return true;
            // Limit recognition to the hazard's local hierarchy, not the level.
            Transform node = hazard.transform;
            for (int depth = 0; node && depth < 4; depth++, node = node.parent)
                if (IsChandelierLabel(node.name)) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DeathZone), "GotHit")]
    internal static class ChandelierEnemyDeathImmunityPatch
    {
        private static bool Prefix(DeathZone __instance, Collider other) =>
            !FriendlyFireRules.GetEnemy(other) || !ChandelierRules.Matches(__instance);
    }

    [HarmonyPatch(typeof(HurtZone), "DamageEnemy")]
    internal static class ChandelierEnemyDamageImmunityPatch
    {
        private static bool Prefix(HurtZone __instance, ref bool __result)
        {
            if (!ChandelierRules.Matches(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class ChandelierEnemyExplosionImmunityPatch
    {
        private static bool Prefix(Explosion __instance, Collider other) =>
            !FriendlyFireRules.GetEnemy(other) || !ChandelierRules.Matches(__instance);
    }
}
