using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    [HarmonyPatch]
    internal static class DroneHalfMovementSpeedPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Drone), "FixedUpdate");
            yield return AccessTools.Method(typeof(Drone), "Dodge", new[] { typeof(Vector3) });
        }

        private static void Prefix(EnemyIdentifier ___eid, out float? __state)
        {
            __state = null;
            if (!UltraMiseryState.IsActive || !___eid || ___eid.enemyType != EnemyType.Drone) return;
            // Scope the reduction to flight/physics and dodge calculations.
            // Attack cooldowns, animations, and projectile setup see the normal
            // modifier. Restore afterwards to avoid compounding on every tick.
            __state = ___eid.totalSpeedModifier;
            ___eid.totalSpeedModifier *= 0.5f;
        }

        private static void Finalizer(EnemyIdentifier ___eid, float? __state)
        {
            if (___eid && __state.HasValue) ___eid.totalSpeedModifier = __state.Value;
        }
    }
}
