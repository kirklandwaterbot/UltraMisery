using HarmonyLib;
using UnityEngine;
using ULTRAKILL.Enemy;
using ULTRAKILL.Portal;

namespace UltraMisery
{
    [HarmonyPatch(typeof(ZombieProjectiles), "ThrowProjectile")]
    internal static class StrayHomingLaunchPatch
    {
        internal static bool Applies(bool active, EnemyType type) => active && type == EnemyType.Stray;

        private static void Postfix(EnemyIdentifier ___eid, GameObject ___currentProjectile, TargetHandle ___targetHandle, Transform ___shootPos)
        {
            if (!___eid || !Applies(UltraMiseryState.IsActive, ___eid.enemyType) || !___currentProjectile) return;
            // Keep native portal-projected launch geometry. A muzzle projected
            // through a portal is no longer in the shooter's coordinate space.
            if ((___targetHandle != null && ___targetHandle.portals.Count != 0) || !___shootPos ||
                (___currentProjectile.transform.position - ___shootPos.position).sqrMagnitude > 0.01f) return;
            var markers = ___currentProjectile.GetComponentsInChildren<UltraMiseryHomingOrbReplacement>(true);
            if (markers.Length == 0) return;
            Vector3 aim;
            if (___targetHandle != null && DroneTargetRules.IsLive(___targetHandle.target))
            {
                TargetData data = MonoSingleton<PortalManagerV2>.Instance.TargetTracker.CalculateData(___targetHandle);
                aim = data.target.isPlayer ? data.headPosition : data.position;
            }
            else if (___eid.target != null && ___eid.target.isValid) aim = ___eid.target.headPosition;
            else return;
            Vector3 direction = aim - ___currentProjectile.transform.position;
            if (direction.sqrMagnitude < 0.0001f) return;
            // The original fast red shot uses predicted aim. A slower homing
            // throw starts at its actual target and then steers natively.
            Quaternion rotation = Quaternion.LookRotation(direction);
            ___currentProjectile.transform.rotation = rotation;
            foreach (var marker in markers)
            {
                Projectile shot = marker.GetComponent<Projectile>();
                if (!shot || shot.parried || shot.playerBullet || shot.decorative) continue;
                shot.transform.rotation = rotation;
                shot.predictiveHomingMultiplier = 0f;
                shot.targetHandle = ___targetHandle;
#pragma warning disable CS0612, CS0618
                shot.target = ___eid.target;
#pragma warning restore CS0612, CS0618
            }
            foreach (var spread in ___currentProjectile.GetComponentsInChildren<ProjectileSpread>(true))
            {
                spread.targetHandle = ___targetHandle;
#pragma warning disable CS0612, CS0618
                spread.target = ___eid.target;
#pragma warning restore CS0612, CS0618
            }
        }
    }

    [HarmonyPatch(typeof(ProjectileSpread), "Start")]
    internal static class HomingSpreadTargetInheritancePatch
    {
        private static void Postfix(ProjectileSpread __instance)
        {
            if (!UltraMiseryState.IsActive) return;
            // Runtime target references are not reliably copied by Unity's
            // Instantiate serialization. Assign them after native fan creation.
            foreach (var marker in __instance.GetComponentsInChildren<UltraMiseryHomingOrbReplacement>(true))
            {
                Projectile shot = marker.GetComponent<Projectile>();
                if (!shot || shot.parried || shot.playerBullet) continue;
                if (__instance.targetHandle != null) shot.targetHandle = __instance.targetHandle;
#pragma warning disable CS0612, CS0618
                if (__instance.target != null) shot.target = __instance.target;
#pragma warning restore CS0612, CS0618
            }
        }
    }
}
