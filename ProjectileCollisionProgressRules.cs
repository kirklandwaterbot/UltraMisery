using System;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    // Native FixedUpdate clips the position BEFORE Collided decides whether
    // the hit counts. An ignored hit must not keep clipping the next step.
    internal static class ProjectileCollisionProgressRules
    {
        [ThreadStatic] internal static Projectile Current;
        [ThreadStatic] internal static Vector3 StepStart;

        internal struct Scope
        {
            internal Projectile Previous;
            internal Vector3 PreviousStart;
        }

        internal static bool ShouldRestore(bool inPrecheck, bool active, bool hittingPlayer, bool unchanged)
            => inPrecheck && active && !hittingPlayer && unchanged;
    }

    [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
    internal static class ProjectilePrecheckScopePatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance, out ProjectileCollisionProgressRules.Scope __state)
        {
            __state = new ProjectileCollisionProgressRules.Scope
            {
                Previous = ProjectileCollisionProgressRules.Current,
                PreviousStart = ProjectileCollisionProgressRules.StepStart
            };
            ProjectileCollisionProgressRules.Current = UltraMiseryState.IsActive && __instance.precheckForCollisions ? __instance : null;
            ProjectileCollisionProgressRules.StepStart = __instance.transform.position;
        }

        // Finalizer also unwinds on exceptions; never leak a scope into another shot.
        private static void Finalizer(ProjectileCollisionProgressRules.Scope __state)
        {
            ProjectileCollisionProgressRules.Current = __state.Previous;
            ProjectileCollisionProgressRules.StepStart = __state.PreviousStart;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class ProjectileIgnoredHitProgressPatch
    {
        // Void prefixes still execute when another prefix suppresses Collided.
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Projectile __instance, out Vector3 __state)
            => __state = __instance.transform.position;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Projectile __instance, bool ___active, bool ___hittingPlayer, Vector3 __state)
        {
            if (!__instance) return;
            if (ProjectileCollisionProgressRules.ShouldRestore(
                ProjectileCollisionProgressRules.Current == __instance,
                ___active, ___hittingPlayer,
                (__instance.transform.position - __state).sqrMagnitude < 0.00000001f))
            {
                // No hit consumed or repositioned the projectile. Preserve its
                // velocity and let physics advance normally. In particular do
                // not undo the AI parry's deliberate position/direction change.
                __instance.transform.position = ProjectileCollisionProgressRules.StepStart;
            }
        }
    }
}
