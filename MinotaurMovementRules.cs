using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    [HarmonyPatch(typeof(Minotaur), "GetSpeed")]
    internal static class MinotaurResponsiveWalkingPatch
    {
        private static void Postfix(ref EnemyMovementData __result)
        {
            if (!UltraMiseryState.IsActive) return;
            // Reach/stop walking speed in roughly 0.05 seconds. Native speed
            // and animation/radiance multipliers are left untouched.
            __result.acceleration = Mathf.Max(__result.acceleration, __result.speed * 20f);
        }
    }

    [HarmonyPatch(typeof(Minotaur), "FixedUpdate")]
    internal static class MinotaurNoGroundGlidePatch
    {
        private static void Prefix(Enemy ___mach, bool ___dead, bool ___moveForward,
            float ___ramTimer, float ___moveBreakSpeed, ref float ___moveSpeed)
        {
            if (!UltraMiseryState.IsActive || ___dead || !___mach || !___mach.gc ||
                !___mach.gc.onGround || !___moveForward || ___ramTimer > 0f || ___moveBreakSpeed <= 0f) return;
            // This branch is the scripted deceleration after ram/parry/slam,
            // not the live charge or hammer lunge. Remove its residual glide.
            ___moveSpeed = 0f;
        }

        private static void Postfix(Enemy ___mach, NavMeshAgent ___nma, Rigidbody ___rb,
            bool ___dead, bool ___moveForward, float ___ramTimer, float ___moveBreakSpeed)
        {
            if (!UltraMiseryState.IsActive || ___dead || !___mach || !___mach.gc || !___mach.gc.onGround) return;
            if (___moveForward)
            {
                if (___ramTimer <= 0f && ___moveBreakSpeed > 0f && ___rb && !___rb.isKinematic)
                    ___rb.velocity = new Vector3(0f, ___rb.velocity.y, 0f);
                return;
            }
            if (!___nma || !___nma.enabled || !___nma.isOnNavMesh || ___nma.isOnOffMeshLink || ___nma.pathPending) return;
            // Eliminate agent inertia, not just Rigidbody/animation-event drift.
            // desiredVelocity retains native pathfinding and obstacle avoidance.
            bool stopped = ___nma.isStopped || !___nma.hasPath ||
                ___nma.remainingDistance <= ___nma.stoppingDistance + 0.05f;
            ___nma.velocity = stopped ? Vector3.zero : ___nma.desiredVelocity;
        }
    }

    [HarmonyPatch]
    internal static class MinotaurCleanStopPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string method in new[] { "StopMoving", "StopAction", "HammerSwingStop", "RamBonk" })
                yield return AccessTools.Method(typeof(Minotaur), method);
        }
        private static void Postfix(Enemy ___mach, Rigidbody ___rb, NavMeshAgent ___nma, bool ___dead, bool ___moveForward)
        {
            if (!UltraMiseryState.IsActive || ___dead || ___moveForward || !___mach ||
                !___mach.gc || !___mach.gc.onGround) return;
            if (___rb && !___rb.isKinematic)
                ___rb.velocity = new Vector3(0f, ___rb.velocity.y, 0f);
            if (___nma && ___nma.enabled && ___nma.isOnNavMesh)
                ___nma.velocity = Vector3.zero;
        }
    }
}
