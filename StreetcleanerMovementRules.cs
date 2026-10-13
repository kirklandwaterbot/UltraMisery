using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal static class StreetcleanerMovementRules
    {
        internal static bool CanGrip(bool active, bool dead, bool dodging, bool grounded,
            bool navigating, bool traversing, bool physicsDriven) =>
            active && !dead && !dodging && grounded && navigating && !traversing && !physicsDriven;
    }

    [HarmonyPatch(typeof(Streetcleaner), "GetSpeed")]
    internal static class StreetcleanerResponsiveWalkingPatch
    {
        private static void Postfix(ref EnemyMovementData __result)
        {
            if (UltraMiseryState.IsActive)
                __result.acceleration = Mathf.Max(__result.acceleration, __result.speed * 20f);
        }
    }

    [HarmonyPatch(typeof(Streetcleaner), "FixedUpdate")]
    internal static class StreetcleanerNoGroundGlidePatch
    {
        private static void Postfix(Enemy ___mach, NavMeshAgent ___nma, Rigidbody ___rb, bool ___dead, bool ___dodging)
        {
            if (!___mach || !___nma || !___rb) return;
            bool navigating = ___nma.enabled && ___nma.isOnNavMesh;
            if (!StreetcleanerMovementRules.CanGrip(UltraMiseryState.IsActive, ___dead, ___dodging,
                ___mach.gc && ___mach.gc.onGround, navigating,
                ___mach.isTraversingPortalLink || (navigating && ___nma.isOnOffMeshLink), !___rb.isKinematic)) return;
            if (___nma.pathPending) return;
            // Keep native routes, avoidance and speed, but remove lingering
            // sideways/braking velocity. Dodge and knockback remain physical.
            bool stopped = ___nma.isStopped || !___nma.hasPath ||
                ___nma.remainingDistance <= ___nma.stoppingDistance + 0.05f;
            ___nma.velocity = stopped ? Vector3.zero : ___nma.desiredVelocity;
        }
    }
}
