using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryMortarClearance : MonoBehaviour
    {
        private Projectile shot;
        private Collider[] shells;
        private readonly List<(Collider Shot, Collider Unit)> changedPairs = new List<(Collider, Collider)>();
        internal void Bind(EnemyIdentifier owner)
        {
            shot = GetComponent<Projectile>();
            shells = GetComponentsInChildren<Collider>(true);
            Restore();
            Collider[] launcher = owner.GetComponentsInChildren<Collider>(true);
            foreach (Collider shell in shells)
                foreach (Collider part in launcher)
                    if (shell && part && shell != part && EarthmoverDefenceRules.HitsOwner(owner, part) &&
                        !Physics.GetIgnoreCollision(shell, part))
                    {
                        Physics.IgnoreCollision(shell, part, true);
                        changedPairs.Add((shell, part));
                    }
        }
        private void Restore()
        {
            foreach (var pair in changedPairs)
                if (pair.Shot && pair.Unit) Physics.IgnoreCollision(pair.Shot, pair.Unit, false);
            changedPairs.Clear();
        }
        private void Update()
        {
            var tag = GetComponent<UltraMiseryAttackOwner>();
            if (!UltraMiseryState.IsActive || !shot || shot.parried || shot.friendly || shot.playerBullet || (tag && tag.ReflectedByEnemy))
            {
                Restore();
                Destroy(this);
            }
        }
        private void OnDestroy() => Restore();
    }

    internal static class EarthmoverDefenceRules
    {
        internal static bool IsDefence(EnemyIdentifier enemy)
        {
            if (!enemy) return false;
            if (enemy.enemyType == EnemyType.Centaur || enemy.GetComponentInChildren<MortarLauncher>(true)) return true;
            for (Transform current = enemy.transform; current; current = current.parent)
                if (current.name.StartsWith("Centaur", System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool HitsOwner(EnemyIdentifier owner, Collider other)
        {
            return owner && other && FriendlyFireRules.GetEnemy(other) == owner;
        }
    }

    // Follow position without inheriting the enormous defence prefab scale.
    internal sealed class UltraMiseryDefenceRageSize : MonoBehaviour
    {
        private Transform anchor;
        internal void Bind(Transform target)
        {
            anchor = target;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one * 0.5f;
            LateUpdate();
        }
        private void LateUpdate()
        {
            if (!anchor) { Destroy(gameObject); return; }
            transform.position = anchor.position;
            transform.localScale = Vector3.one * 0.5f;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class MortarOwnerCollisionPatch
    {
        private static bool Prefix(Projectile __instance, Collider other)
        {
            if (!UltraMiseryState.IsActive || __instance.parried || __instance.friendly || __instance.playerBullet) return true;
            var attribution = __instance.GetComponentInParent<UltraMiseryAttackOwner>();
            if (attribution && attribution.ReflectedByEnemy) return true;
            EnemyIdentifier owner = LifestealRules.Resolve(__instance);
            if (!EarthmoverDefenceRules.HitsOwner(owner, other)) return true;
            return !owner.GetComponentInChildren<MortarLauncher>(true);
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class MortarOwnerExplosionPatch
    {
        private static bool Prefix(Explosion __instance, Collider other)
        {
            if (!UltraMiseryState.IsActive) return true;
            var attribution = __instance.GetComponentInParent<UltraMiseryAttackOwner>();
            if (attribution && attribution.ReflectedByEnemy) return true;
            EnemyIdentifier owner = LifestealRules.Resolve(__instance);
            if (!EarthmoverDefenceRules.HitsOwner(owner, other)) return true;
            return !owner.GetComponentInChildren<MortarLauncher>(true);
        }
    }
}
