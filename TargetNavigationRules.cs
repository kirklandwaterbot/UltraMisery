using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal sealed class UltraMiseryNavigationCache : MonoBehaviour
    {
        internal float NextCheck;
        internal Vector3 Requested;
        internal Vector3 Resolved;
        internal bool HasResult;
        internal readonly NavMeshPath Path = new NavMeshPath();
    }

    [HarmonyPatch(typeof(Enemy), "SetDestination")]
    internal static class TargetNavigationPatch
    {
        private static void Prefix(Enemy __instance, ref Vector3 position)
        {
            Vector3 approach = LargeBossTargetRules.Approach(position, __instance);
            if (approach != position) { position = approach; return; }
            if (!EnemyAwarenessRules.CanAttack(__instance) || !__instance.nma ||
                !__instance.nma.enabled || !__instance.nma.isOnNavMesh || __instance.stopped) return;
            EnemyIdentifier enemy = __instance.GetComponent<EnemyIdentifier>();
            // Leave scripted retreat, charge, patrol and phase destinations alone.
            if ((position - enemy.target.position).sqrMagnitude > 16f) return;
            var cache = __instance.GetComponent<UltraMiseryNavigationCache>() ??
                __instance.gameObject.AddComponent<UltraMiseryNavigationCache>();
            if (Time.time < cache.NextCheck)
            {
                if (cache.HasResult && (position - cache.Requested).sqrMagnitude < 1f) position = cache.Resolved;
                return;
            }
            cache.NextCheck = Time.time + 0.5f;
            cache.Requested = position;
            cache.HasResult = false;
            // Search a wider local landing point when a jumping target is not
            // inside the native one-metre NavMesh sampling radius. No warping.
            for (int radius = 1; radius <= 8; radius *= 2)
            {
                if (!NavMesh.SamplePosition(position, out NavMeshHit hit, radius, __instance.nma.areaMask)) continue;
                if (!__instance.nma.CalculatePath(hit.position, cache.Path) || cache.Path.status != NavMeshPathStatus.PathComplete) continue;
                cache.Resolved = hit.position;
                cache.HasResult = true;
                position = hit.position;
                break;
            }
        }
    }
}
