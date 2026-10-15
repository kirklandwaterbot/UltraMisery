using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class FleshPrisonAiRules
    {
        internal static bool Applies(bool aiCombat, bool blind, EnemyType type) => aiCombat && !blind &&
            (type == EnemyType.FleshPrison || type == EnemyType.FleshPanopticon);
        internal static bool Active(EnemyIdentifier owner) => owner && !owner.dead &&
            Applies(LifestealRules.AiCombatAllowed, ULTRAKILL.Cheats.BlindEnemies.Blind, owner.enemyType);
        internal static bool Candidate(bool alive, bool active, bool self) => alive && active && !self;
        internal static bool Alive(EnemyIdentifier enemy) => enemy && !enemy.dead && enemy.gameObject.activeInHierarchy;
        internal static bool Valid(EnemyTarget target, EnemyIdentifier owner) => target != null && target.isValid &&
            target.isEnemy && Alive(target.enemyIdentifier) && target.enemyIdentifier != owner;

        internal static EnemyTarget Find(EnemyIdentifier owner, Vector3 from)
        {
            var tracker = MonoSingleton<EnemyTracker>.Instance;
            if (!tracker) return null;
            EnemyIdentifier best = null;
            float nearest = float.PositiveInfinity;
            foreach (var enemy in tracker.GetCurrentEnemies())
            {
                // No species, ignoredByEnemies, root-collider or distance exclusions.
                if (!enemy || !Candidate(!enemy.dead, enemy.gameObject.activeInHierarchy, enemy == owner)) continue;
                float distance = (enemy.transform.position - from).sqrMagnitude;
                if (distance >= nearest) continue;
                best = enemy; nearest = distance;
            }
            if (!best) return null;
            SupportEnemyCombatRules.EnsureVisionTarget(best);
            return new EnemyTarget(best);
        }
        internal static bool EnsureTarget(EnemyIdentifier owner)
        {
            if (!Valid(owner.target, owner)) owner.target = Find(owner, owner.transform.position);
            return Valid(owner.target, owner);
        }
        // Preserve live bursts/heals and scheduled callbacks; recover only an orphan.
        internal static int Recovery(bool action, bool healing, bool burst, bool scheduled) =>
            !action || burst || scheduled ? 0 : healing ? 2 : 1;
    }

    [HarmonyPatch(typeof(FleshPrison), "Update")]
    internal static class FleshPrisonTargetBalancePatch
    {
        private static void Prefix(FleshPrison __instance, EnemyIdentifier ___eid, ref bool ___inAction,
            bool ___healing, int ___currentProjectile, int ___projectileAmount)
        {
            if (!FleshPrisonAiRules.Active(___eid) || !FleshPrisonAiRules.EnsureTarget(___eid)) return;
            bool scheduled = __instance.IsInvoking("SpawnInsignia") || __instance.IsInvoking("SpawnBlackHole") ||
                __instance.IsInvoking("HomingProjectileAttack") || __instance.IsInvoking("SpawnFleshDrones") || __instance.IsInvoking("HealFromDrone");
            int recovery = FleshPrisonAiRules.Recovery(___inAction, ___healing, ___currentProjectile < ___projectileAmount, scheduled);
            if (recovery == 1) ___inAction = false;
            else if (recovery == 2) __instance.Invoke("SpawnFleshDrones", .1f);
        }
    }

    [HarmonyPatch]
    internal static class FleshPrisonDelayedAttackBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(FleshPrison), "SpawnInsignia");
            yield return WeaponPatchReflection.Method(typeof(FleshPrison), "SpawnBlackHole");
            yield return WeaponPatchReflection.Method(typeof(FleshPrison), "SpawnFleshDrones");
        }
        private static bool Prefix(FleshPrison __instance, EnemyIdentifier ___eid, MethodBase __originalMethod)
        {
            if (!FleshPrisonAiRules.Active(___eid) || FleshPrisonAiRules.EnsureTarget(___eid)) return true;
            // Keep the native action pending while the arena is empty. A later
            // enemy can resume it without leaving inAction/healing stuck forever.
            __instance.CancelInvoke(__originalMethod.Name);
            __instance.Invoke(__originalMethod.Name, .25f);
            return false;
        }
    }

    internal sealed class BlackHoleAiContacts
    {
        public BlackHoleAiContacts() { }
        internal bool Impact;
        internal readonly Dictionary<Collider, EnemyIdentifier> Hits = new Dictionary<Collider, EnemyIdentifier>();
    }
    internal static class BlackHoleAiRules
    {
        private static readonly ConditionalWeakTable<BlackHoleProjectile, BlackHoleAiContacts> Contacts = new ConditionalWeakTable<BlackHoleProjectile, BlackHoleAiContacts>();
        internal static BlackHoleAiContacts State(BlackHoleProjectile hole) => Contacts.GetOrCreateValue(hole);
        internal static bool Active(BlackHoleProjectile hole, out EnemyIdentifier owner)
        {
            owner = LifestealRules.Resolve(hole);
            return hole && hole.enemy && FleshPrisonAiRules.Active(owner);
        }
        internal static void Damage(BlackHoleProjectile hole, EnemyIdentifier owner, EnemyIdentifier victim, Collider hit, float damage)
        {
            if (!FleshPrisonAiRules.Alive(victim) || victim == owner) return;
            var previous = LifestealRules.Attacker;
            try
            {
                LifestealRules.Attacker = owner;
                if (hit && SupportEnemyCombatRules.BreakOnEnemyContact(hole, hit, damage)) return;
                victim.hitter = "enemy";
                victim.DeliverDamage(hit ? hit.gameObject : victim.gameObject, Vector3.zero,
                    hit ? hit.bounds.center : victim.transform.position, damage, false, 0f, null, false, false);
            }
            finally { LifestealRules.Attacker = previous; }
        }
    }

    [HarmonyPatch(typeof(BlackHoleProjectile), "FixedUpdate")]
    internal static class BlackHoleTargetBalancePatch
    {
        private static bool Prefix(BlackHoleProjectile __instance, Rigidbody ___rb)
        {
            if (!BlackHoleAiRules.Active(__instance, out var owner)) return true;
            if (!FleshPrisonAiRules.Valid(__instance.target, owner))
            {
                FleshPrisonAiRules.EnsureTarget(owner);
                __instance.target = owner.target;
            }
            if (FleshPrisonAiRules.Valid(__instance.target, owner)) return true;
            if (___rb) ___rb.velocity = Vector3.zero;
            return false; // Avoid a null/dead target read; resume when an enemy appears.
        }
    }

    [HarmonyPatch(typeof(BlackHoleProjectile), "OnTriggerEnter")]
    internal static class BlackHoleImpactBalancePatch
    {
        private static bool Prefix(BlackHoleProjectile __instance, Collider other, bool ___activated, bool ___collapsing)
        {
            if (!BlackHoleAiRules.Active(__instance, out var owner)) return true;
            var victim = FriendlyFireRules.GetEnemy(other);
            var state = BlackHoleAiRules.State(__instance);
            if (!___activated || ___collapsing || state.Impact || !FleshPrisonAiRules.Alive(victim) || victim == owner) return false;
            state.Impact = true; // One impact, even with several colliders this frame.
            // Use the native black-hole enemy explosion damage amount. Never run
            // its player-only hp/anti-health path when colliding with an enemy.
            BlackHoleAiRules.Damage(__instance, owner, victim, other, 100f);
            __instance.Explode();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class BlackHoleAuraContactBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(BlackHoleTrigger), "OnTriggerEnter");
            yield return WeaponPatchReflection.Method(typeof(BlackHoleTrigger), "OnTriggerExit");
        }
        private static bool Prefix(BlackHoleTrigger __instance, Collider other, MethodBase __originalMethod)
        {
            var hole = __instance.GetComponentInParent<BlackHoleProjectile>();
            if (!BlackHoleAiRules.Active(hole, out var owner)) return true;
            var contacts = BlackHoleAiRules.State(hole).Hits;
            if (__originalMethod.Name == "OnTriggerExit") contacts.Remove(other);
            else
            {
                var victim = FriendlyFireRules.GetEnemy(other);
                if (FleshPrisonAiRules.Alive(victim) && victim != owner) contacts[other] = victim;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BlackHoleProjectile), "ShootTargetLightning")]
    internal static class BlackHoleAuraDamageBalancePatch
    {
        private static bool Prefix(BlackHoleProjectile __instance)
        {
            if (!BlackHoleAiRules.Active(__instance, out var owner)) return true;
            if (!__instance.gameObject.activeInHierarchy) return false;
            var contacts = BlackHoleAiRules.State(__instance);
            var dead = new List<Collider>();
            var victims = new HashSet<EnemyIdentifier>();
            foreach (var pair in new List<KeyValuePair<Collider, EnemyIdentifier>>(contacts.Hits))
            {
                if (!pair.Key || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy || !FleshPrisonAiRules.Alive(pair.Value))
                { dead.Add(pair.Key); continue; }
                if (pair.Value == owner || !victims.Add(pair.Value)) continue;
                if (__instance.lightningBolt2)
                {
                    var effect = UnityEngine.Object.Instantiate(__instance.lightningBolt2, __instance.transform.position, __instance.transform.rotation);
                    var line = effect.GetComponent<LineRenderer>();
                    if (line) { line.SetPosition(0, __instance.transform.position); line.SetPosition(1, pair.Key.bounds.center); }
                }
                BlackHoleAiRules.Damage(__instance, owner, pair.Value, pair.Key, 1f);
            }
            foreach (var hit in dead) contacts.Hits.Remove(hit);
            __instance.CancelInvoke("ShootTargetLightning");
            __instance.Invoke("ShootTargetLightning", .5f);
            return false;
        }
    }
}
