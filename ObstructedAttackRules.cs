using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal static class ObstructedAttackRules
    {
        internal sealed class State
        {
            public State() { }
            internal Transform Target;
            internal float BlockedSince = -1f;
            internal float NextRoute;
            internal bool Approaching;
            internal float RetryAfter;
        }
        private static readonly ConditionalWeakTable<EnemyIdentifier, State> States = new ConditionalWeakTable<EnemyIdentifier, State>();
        internal static bool Timeout(float since, float now) => since >= 0f && now - since >= 3f;
        internal static bool CanApproach(bool stationary, bool hasBody, bool agentEnabled, bool onMesh, bool traversing)
            => !stationary && hasBody && agentEnabled && onMesh && !traversing;
        internal static bool Approaching(EnemyIdentifier owner) => UltraMiseryState.IsActive && owner &&
            States.TryGetValue(owner, out State state) && state.Approaching && Time.time - state.BlockedSince < 6f && owner.target != null &&
            owner.target.isValid && owner.target.trackedTransform == state.Target;

        internal static bool Observe(EnemyIdentifier owner, bool blocked)
        {
            State state = States.GetOrCreateValue(owner);
            NavMeshAgent agent = owner.GetComponent<NavMeshAgent>();
            bool canRoute = CanApproach(owner.stationary, owner.GetComponent<Enemy>(),
                agent && agent.enabled, agent && agent.enabled && agent.isOnNavMesh,
                agent && agent.enabled && agent.isOnNavMesh && agent.isOnOffMeshLink);
            Transform target = owner.target?.trackedTransform;
            if (state.Target != target || !blocked || !canRoute)
            {
                state.Target = target;
                state.BlockedSince = -1f;
                state.Approaching = false;
                state.RetryAfter = 0f;
            }
            if (!blocked || !target || !canRoute || Time.time < state.RetryAfter) return false;
            // A failed route must never suppress attacks indefinitely. Allow
            // native attacks for six seconds before another approach attempt.
            if (state.BlockedSince >= 0f && Time.time - state.BlockedSince >= 6f)
            {
                state.Approaching = false;
                state.BlockedSince = -1f;
                state.RetryAfter = Time.time + 6f;
                return false;
            }
            if (state.BlockedSince < 0f) state.BlockedSince = Time.time;
            state.Approaching = Timeout(state.BlockedSince, Time.time);
            return state.Approaching;
        }

        internal static void Route(EnemyIdentifier owner)
        {
            if (!Approaching(owner) || owner.dead || owner.stationary) return;
            State state = States.GetOrCreateValue(owner);
            if (Time.time < state.NextRoute) return;
            state.NextRoute = Time.time + 0.5f;
            NavMeshAgent agent = owner.GetComponent<NavMeshAgent>();
            Enemy body = owner.GetComponent<Enemy>();
            // Never fake grounding, enable an airborne agent or interrupt a
            // scripted traversal. Flying enemies retain native search movement.
            if (!body || !agent || !agent.enabled || !agent.isOnNavMesh || agent.isOnOffMeshLink) return;
            body.SetDestination(LargeBossTargetRules.Approach(owner.target.position, body));
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Update")]
    internal static class ObstructedEnemyApproachPatch
    {
        private static void Postfix(EnemyIdentifier __instance) => ObstructedAttackRules.Route(__instance);
    }
}
