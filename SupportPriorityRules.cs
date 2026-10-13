using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiserySupportPriority : MonoBehaviour
    {
        private float nextCheck;
        private EnemyIdentifier priority;
        private static readonly System.Reflection.FieldInfo Puppets = AccessTools.Field(typeof(Deathcatcher), "deadCaughtEnemies");
        private static bool Alive(EnemyIdentifier enemy) => enemy && !enemy.dead && enemy.gameObject.activeInHierarchy;

        internal void Apply(EnemyIdentifier attacker)
        {
            if (!LifestealRules.AiCombatAllowed || ULTRAKILL.Cheats.BlindEnemies.Blind || attacker.dead || AiCombatRules.SupportRole(attacker))
            { priority = null; return; }
            if (priority && AiTargetRecoveryRules.Avoided(attacker, priority)) priority = null;
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.25f;
                EnemyIdentifier victim = Alive(priority) ? priority : attacker.target?.enemyIdentifier;
                if (!Alive(priority)) priority = null;
                // Follow protection chains (e.g. an Idol protected by another Idol).
                if (Alive(victim) && (victim.blessed || victim.puppet) && MonoSingleton<EnemyTracker>.Instance)
                {
                    EnemyIdentifier best = null;
                    float distance = float.PositiveInfinity;
                    foreach (EnemyIdentifier candidate in MonoSingleton<EnemyTracker>.Instance.GetCurrentEnemies())
                    {
                        if (!Alive(candidate) || candidate == attacker || candidate == victim || AiTargetRecoveryRules.Avoided(attacker, candidate)) continue;
                        bool protects = victim.blessed && candidate.TryGetComponent<Idol>(out Idol idol) && idol.target == victim;
                        if (!protects && victim.puppet && candidate.TryGetComponent<Deathcatcher>(out Deathcatcher catcher) && catcher.active)
                        {
                            var caught = Puppets.GetValue(catcher) as List<CaughtEnemy>;
                            if (caught != null)
                                foreach (CaughtEnemy entry in caught)
                                    if (entry.puppet && (victim.transform == entry.puppet.transform || victim.transform.IsChildOf(entry.puppet.transform)))
                                    { protects = true; break; }
                        }
                        float d = (candidate.transform.position - attacker.transform.position).sqrMagnitude;
                        if (protects && d < distance) { best = candidate; distance = d; }
                    }
                    if (best) priority = best;
                }
            }
            if (Alive(priority) && attacker.target?.enemyIdentifier != priority)
            {
                SupportEnemyCombatRules.EnsureVisionTarget(priority);
                // Use the real target so native navigation and attack queries
                // agree; no teleporting or forcing the NavMeshAgent state.
                attacker.target = new EnemyTarget(priority.transform);
            }
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "UpdateTarget")]
    internal static class SupportPriorityTargetPatch
    {
        private static void Postfix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || __instance.dead || AiCombatRules.SupportRole(__instance)) return;
            var priority = __instance.GetComponent<UltraMiserySupportPriority>();
            if (!priority && !LifestealRules.AiCombatAllowed) return;
            (priority ? priority : __instance.gameObject.AddComponent<UltraMiserySupportPriority>()).Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(FireZone), "OnTriggerStay")]
    internal static class StreetcleanerAiFirePatch
    {
        private static bool Prefix(FireZone __instance, Collider other)
        {
            if (!LifestealRules.AiCombatAllowed || __instance.source != FlameSource.Streetcleaner) return true;
            EnemyIdentifier victim = FriendlyFireRules.GetEnemy(other);
            if (!victim) return true; // Keep native player/environment handling.
            Streetcleaner cleaner = __instance.GetComponentInParent<Streetcleaner>();
            if (!cleaner || !cleaner.damaging || !cleaner.eid || cleaner.eid.dead || victim == cleaner.eid || victim.dead) return false;
            if (__instance.HurtCooldownCollection == null) __instance.HurtCooldownCollection = new HurtCooldownCollection();
            if (!__instance.HurtCooldownCollection.TryHurtCheckEnemy(victim, true)) return false;
            EnemyIdentifier previous = LifestealRules.Attacker;
            LifestealRules.Attacker = cleaner.eid;
            try
            {
                // Root colliders do not necessarily have a limb identifier or
                // Flammable. Deal contact damage without re-enabling ignition/oil.
                if (!SupportEnemyCombatRules.BreakOnEnemyContact(__instance, other, 1f))
                {
                    victim.hitter = "fire";
                    victim.DeliverDamage(other.gameObject, Vector3.zero, other.ClosestPoint(__instance.transform.position),
                        cleaner.eid.totalDamageModifier, false, 0f, null, false, false);
                }
            }
            finally { LifestealRules.Attacker = previous; }
            return false;
        }
    }
}
