using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class FriendlyFireRules
    {
        internal static bool SuppressEnemyDamage => UltraMiseryState.IsActive && !LifestealRules.AiCombatAllowed;
        [System.ThreadStatic]
        private static int enemyAttackScopeDepth;

        internal static EnemyIdentifier GetEnemy(Collider collider)
        {
            if (!collider)
            {
                return null;
            }

            // Resolve outward from the hitbox, stopping at the nearest unit.
            // An outer/shared assembly's limb link must not override a nested
            // defence unit. Explicit links on the hitbox still take precedence.
            for (Transform node = collider.transform; node; node = node.parent)
            {
                EnemyIdentifierIdentifier limb = node.GetComponent<EnemyIdentifierIdentifier>();
                if (limb && limb.eid) return limb.eid;
                EnemyIdentifier unit = node.GetComponent<EnemyIdentifier>();
                if (unit) return unit;
            }
            return null;
        }

        internal static void EnterEnemyAttackScope()
        {
            enemyAttackScopeDepth++;
        }

        internal static void ExitEnemyAttackScope()
        {
            if (enemyAttackScopeDepth > 0)
            {
                enemyAttackScopeDepth--;
            }
        }

        internal static bool IsEnemyAttackScope => enemyAttackScopeDepth > 0;

        internal static bool IsAttributedEnemyHit(EnemyIdentifier victim)
        {
            EnemyIdentifier attacker = LifestealRules.Attacker;
            return attacker && victim && attacker != victim;
        }

        internal static bool IsEnemyDamageMarker(string hitter)
        {
            return string.Equals(hitter, "enemy", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hitter, "ffexplosion", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasEnemyOwner(Component component, EnemyIdentifier victim = null)
        {
            if (!component)
            {
                return false;
            }

            EnemyIdentifier owner = component.GetComponentInParent<EnemyIdentifier>();
            return owner && owner != victim;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class EnemyProjectileFriendlyFirePatch
    {
        private static bool Prefix(Projectile __instance, Collider other)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage || __instance.playerBullet || __instance.parried)
            {
                return true;
            }

            // Do not rely on the victim's Start/Postfix having run. Delayed
            // subwave enemies can be struck during their activation frame.
            return !FriendlyFireRules.GetEnemy(other);
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class EnemyExplosionFriendlyFirePatch
    {
        private static bool Prefix(Explosion __instance, Collider other)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage || (!__instance.enemy && !__instance.friendlyFire))
            {
                return true;
            }

            return !FriendlyFireRules.GetEnemy(other);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "FallOnEnemy")]
    internal static class EnemyFallFriendlyFirePatch
    {
        private static bool Prefix(EnemyIdentifier eid)
        {
            return !FriendlyFireRules.SuppressEnemyDamage || !eid;
        }
    }

    // Final fallback for stock and modded enemy attacks that call the common
    // damage entry point without passing through Projectile/Explosion.
    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class EnemyDamageEntryFriendlyFirePatch
    {
        private static bool Prefix(EnemyIdentifier __instance)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage || !__instance)
            {
                return true;
            }

            if (!FriendlyFireRules.IsEnemyAttackScope &&
                !FriendlyFireRules.IsAttributedEnemyHit(__instance) &&
                !FriendlyFireRules.IsEnemyDamageMarker(__instance.hitter))
            {
                return true;
            }

            __instance.hitterAttributes.Clear();
            __instance.hitter = string.Empty;
            return false;
        }
    }

    // Some enemy attacks bypass DeliverDamage and call the body directly.
    [HarmonyPatch(typeof(Enemy), "GetHurt")]
    internal static class EnemyBodyFriendlyFirePatch
    {
        private static bool Prefix(Enemy __instance)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage) return true;
            EnemyIdentifier victim = __instance.GetComponent<EnemyIdentifier>();
            return !FriendlyFireRules.IsAttributedEnemyHit(victim) &&
                !FriendlyFireRules.IsEnemyAttackScope &&
                (!victim || !FriendlyFireRules.IsEnemyDamageMarker(victim.hitter));
        }
    }

    // Enemy melee hitboxes have their own collision pipeline. This method is
    // only entered for an enemy target, so it is safe to suppress wholesale
    // when an EnemyIdentifier owns the hitbox.
    [HarmonyPatch(typeof(SwingCheck2), "CheckEidCollision")]
    internal static class EnemyMeleeFriendlyFirePatch
    {
        private static bool Prefix(SwingCheck2 __instance, EnemyIdentifier enid)
        {
            return !FriendlyFireRules.SuppressEnemyDamage || !__instance.eid || !enid || __instance.eid == enid;
        }
    }

    [HarmonyPatch(typeof(PhysicalShockwave), "CheckCollision")]
    internal static class EnemyShockwaveFriendlyFirePatch
    {
        internal static bool Suppress(bool active, bool enemyVictim) => active && enemyVictim;

        private static bool Prefix(PhysicalShockwave __instance, Collider col)
        {
            // PhysicalShockwave still launches enemies when damage is zero.
            // Skip the entire enemy contact, including Sentry.Unlodge, for all
            // owners and AI-combat settings. Portal replicas enter here too.
            EnemyIdentifier victim = col ? FriendlyFireRules.GetEnemy(col) : null;
            if (!Suppress(UltraMiseryState.IsActive, victim)) return true;
            BossAiAreaDamageRules.DamageSlam(__instance, col, victim);
            return false;
        }
    }

    [HarmonyPatch(typeof(Drone), "HandleCollisionWithEnemy")]
    internal static class CrashingDroneFriendlyFirePatch
    {
        private static bool Prefix()
        {
            return !FriendlyFireRules.SuppressEnemyDamage;
        }
    }

    [HarmonyPatch(typeof(VirtueInsignia), "OnTriggerEnter")]
    internal static class VirtueInsigniaFriendlyFirePatch
    {
        private static bool Prefix(VirtueInsignia __instance, Collider other)
        {
            return !FriendlyFireRules.SuppressEnemyDamage || !FriendlyFireRules.GetEnemy(other);
        }
    }

    // A hostile Hideous Mass spear can be authored to target an enemy. Keep
    // the player's deflected-spear punish intact while blocking the hostile
    // enemy-targeted path.
    [HarmonyPatch(typeof(MassSpear), "HurtEnemy")]
    internal static class MassSpearFriendlyFirePatch
    {
        private static void Prefix(bool ___deflected, out bool __state)
        {
            __state = FriendlyFireRules.SuppressEnemyDamage && !___deflected;
            if (__state)
            {
                FriendlyFireRules.EnterEnemyAttackScope();
            }
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                FriendlyFireRules.ExitEnemyAttackScope();
            }
        }
    }

    // These attacks hurt the player directly but damage caught enemies through
    // EnemyIdentifier.DeliverDamage. A scoped guard removes only the latter.
    [HarmonyPatch(typeof(MinosPrime), "AirRaycastAttack")]
    internal static class MinosPrimeAreaFriendlyFirePatch
    {
        private static void Prefix(out bool __state)
        {
            __state = FriendlyFireRules.SuppressEnemyDamage;
            if (__state)
            {
                FriendlyFireRules.EnterEnemyAttackScope();
            }
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                FriendlyFireRules.ExitEnemyAttackScope();
            }
        }
    }

    [HarmonyPatch(typeof(SisyphusPrime), "DropAttackActivate")]
    internal static class SisyphusPrimeAreaFriendlyFirePatch
    {
        private static void Prefix(out bool __state)
        {
            __state = FriendlyFireRules.SuppressEnemyDamage;
            if (__state)
            {
                FriendlyFireRules.EnterEnemyAttackScope();
            }
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                FriendlyFireRules.ExitEnemyAttackScope();
            }
        }
    }

    [HarmonyPatch(typeof(HurtZone), "DamageEnemy")]
    internal static class EnemyOwnedHurtZoneFriendlyFirePatch
    {
        private static bool Prefix(HurtZone __instance, EnemyIdentifier eid, ref bool __result)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage || !eid || !FriendlyFireRules.HasEnemyOwner(__instance, eid))
            {
                return true;
            }

            // Report the cooldown tick as handled so a continuously-overlapping
            // enemy-owned zone does not retry this blocked hit every frame.
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(FireZone), "OnTriggerStay")]
    internal static class StreetcleanerFireFriendlyFirePatch
    {
        private static bool Prefix(FireZone __instance, Collider other)
        {
            if (!FriendlyFireRules.SuppressEnemyDamage || !FriendlyFireRules.GetEnemy(other))
            {
                return true;
            }

            return !__instance.GetComponentInParent<Streetcleaner>();
        }
    }
}
