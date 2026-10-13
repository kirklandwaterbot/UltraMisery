using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryMassCannon : MonoBehaviour { }

    internal static class BossAiAreaDamageRules
    {
        private sealed class Contacts { public Contacts() { } internal readonly HashSet<int> Victims = new HashSet<int>(); }
        private static readonly ConditionalWeakTable<PhysicalShockwave, Contacts> Hits = new ConditionalWeakTable<PhysicalShockwave, Contacts>();
        internal static bool SlamAllowed(bool aiCombat, EnemyType source) => aiCombat && source == EnemyType.Cerberus;
        internal static void DamageSlam(PhysicalShockwave wave, Collider col, EnemyIdentifier victim)
        {
            if (!SlamAllowed(LifestealRules.AiCombatAllowed, wave.enemyType) || !wave.enemy || !victim || victim.dead) return;
            EnemyIdentifier owner = LifestealRules.Resolve(wave);
            if (!owner || owner == victim || owner.enemyType != EnemyType.Cerberus || wave.damage <= 0) return;
            if (!Hits.GetOrCreateValue(wave).Victims.Add(victim.GetInstanceID())) return;
            EnemyIdentifier previous = LifestealRules.Attacker;
            try
            {
                LifestealRules.Attacker = owner;
                victim.hitter = "enemy";
                // Damage is already scaled by the native stomp spawning code.
                // Never apply its upward force or Sentry.Unlodge side effect.
                victim.DeliverDamage(col.gameObject, Vector3.zero, col.bounds.center,
                    wave.damage / 10f, false, 0f, null, false, false);
            }
            finally { LifestealRules.Attacker = previous; }
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class MassCannonAiExplosionPatch
    {
        internal struct State
        {
            internal bool Changed, Enemy, FriendlyFire;
            internal AffectedSubjects CanHit;
            internal List<EnemyType> Ignore;
        }
        private static bool Prefix(Explosion __instance, Collider other, out State __state)
        {
            __state = default;
            if (!LifestealRules.AiCombatAllowed || !__instance.GetComponentInParent<UltraMiseryMassCannon>()) return true;
            EnemyIdentifier victim = FriendlyFireRules.GetEnemy(other);
            if (!victim) return true; // Player contact remains entirely native.
            EnemyIdentifier owner = LifestealRules.Resolve(__instance);
            if (owner && victim == owner) return false;
            __state = new State { Changed = true, Enemy = __instance.enemy, FriendlyFire = __instance.friendlyFire,
                CanHit = __instance.canHit, Ignore = __instance.toIgnore };
            // Native enemy explosions hard-exclude Mass/Sisyphus. Only this
            // attributed cannon contact bypasses that exclusion, not resistances.
            __instance.enemy = false;
            __instance.friendlyFire = true;
            __instance.canHit = AffectedSubjects.All;
            __instance.toIgnore = __instance.toIgnore == null ? new List<EnemyType>() : new List<EnemyType>(__instance.toIgnore);
            __instance.toIgnore.RemoveAll(type => type == EnemyType.HideousMass);
            return true;
        }
        private static void Finalizer(Explosion __instance, State __state)
        {
            if (!__state.Changed || !__instance) return;
            __instance.enemy = __state.Enemy;
            __instance.friendlyFire = __state.FriendlyFire;
            __instance.canHit = __state.CanHit;
            __instance.toIgnore = __state.Ignore;
        }
    }
}
