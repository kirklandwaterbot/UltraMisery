using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal static class AiTargetRecoveryRules
    {
        private sealed class Watch
        {
            public Watch() { }
            internal EnemyIdentifier Target, Replacement;
            internal float LastDamage, FailedSince = -1f, NextCheck, HoldUntil;
            internal readonly Dictionary<EnemyIdentifier, float> Avoid = new Dictionary<EnemyIdentifier, float>();
        }
        private static readonly ConditionalWeakTable<EnemyIdentifier, Watch> Watches = new ConditionalWeakTable<EnemyIdentifier, Watch>();
        internal static bool TimedOut(float now, float lastDamage, float failedSince) =>
            now - lastDamage >= 12f || (failedSince >= 0f && now - failedSince >= 6f && now - lastDamage >= 6f);
        private static bool Alive(EnemyIdentifier e) => e && !e.dead && e.gameObject.activeInHierarchy;
        internal static bool Avoided(EnemyIdentifier owner, EnemyIdentifier candidate) =>
            Watches.TryGetValue(owner, out var w) && candidate && w.Avoid.TryGetValue(candidate, out float until) && Time.time < until;

        internal static void Damage(EnemyIdentifier owner, EnemyIdentifier victim, float amount)
        {
            if (amount > 0f && owner && victim && Watches.TryGetValue(owner, out var w) && w.Target == victim)
            { w.LastDamage = Time.time; w.FailedSince = -1f; }
        }

        internal static void Apply(EnemyIdentifier owner)
        {
            if (!LifestealRules.AiCombatAllowed || ULTRAKILL.Cheats.BlindEnemies.Blind || owner.dead || AiCombatRules.SupportRole(owner))
            { Watches.Remove(owner); return; }
            Watch w = Watches.GetOrCreateValue(owner);
            float now = Time.time;
            // Native scanners and support priority must not immediately restore
            // a target we just abandoned. Never hold onto a dead replacement.
            if (Alive(w.Replacement) && now < w.HoldUntil && Avoided(owner, owner.target?.enemyIdentifier))
                owner.target = new EnemyTarget(w.Replacement.transform);
            EnemyIdentifier current = owner.target?.enemyIdentifier;
            if (!Alive(current)) { w.Target = null; return; }
            if (w.Target != current)
            { w.Target = current; w.LastDamage = now; w.FailedSince = -1f; }
            if (now < w.NextCheck) return;
            w.NextCheck = now + .5f;
            NavMeshAgent agent = owner.GetComponent<NavMeshAgent>();
            // Disabled/airborne/stopped agents are normal during animations.
            // Only an attempted, settled, incomplete path is evidence of failure.
            bool failed = !owner.stationary && agent && agent.enabled && agent.isOnNavMesh &&
                !agent.isOnOffMeshLink && !agent.isStopped && !agent.pathPending &&
                agent.pathStatus != NavMeshPathStatus.PathComplete;
            if (!failed) w.FailedSince = -1f;
            else if (w.FailedSince < 0f) w.FailedSince = now;
            if (!TimedOut(now, w.LastDamage, w.FailedSince)) return;
            var tracker = MonoSingleton<EnemyTracker>.Instance;
            if (!tracker) return;
            EnemyIdentifier best = null;
            float distance = float.PositiveInfinity;
            foreach (EnemyIdentifier candidate in tracker.GetCurrentEnemies())
            {
                if (!Alive(candidate) || candidate == owner || candidate == current || candidate.ignoredByEnemies || Avoided(owner, candidate)) continue;
                float d = (candidate.transform.position - owner.transform.position).sqrMagnitude;
                if (d >= distance || !new EnemyTarget(candidate.transform).isValid) continue;
                best = candidate; distance = d;
            }
            // With no alternative, continue fighting rather than dropping aggro.
            if (!best) { w.NextCheck = now + 2f; return; }
            var expired = new List<EnemyIdentifier>();
            foreach (var entry in w.Avoid) if (!Alive(entry.Key) || entry.Value <= now) expired.Add(entry.Key);
            foreach (var key in expired) w.Avoid.Remove(key);
            w.Avoid[current] = now + 20f;
            w.Replacement = best; w.HoldUntil = now + 20f;
            w.Target = best; w.LastDamage = now; w.FailedSince = -1f;
            SupportEnemyCombatRules.EnsureVisionTarget(best);
            owner.target = new EnemyTarget(best.transform);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Update")]
    internal static class AiTargetRecoveryUpdatePatch
    {
        private static void Postfix(EnemyIdentifier __instance) => AiTargetRecoveryRules.Apply(__instance);
    }
}
