using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class HomingOrbMotionRules
    {
        internal static bool CanTrack(bool active, bool decorative, bool hittingPlayer, bool undeflectable,
            bool held, bool launched) => active && !decorative && !hittingPlayer && !undeflectable && !held && launched;
        internal static bool Expired(float seconds) => seconds >= 2f;
        private static int reported;
        private static int scene = -1;
        internal static void Report(Projectile shot, string reason)
        {
            if (scene != shot.gameObject.scene.handle) { scene = shot.gameObject.scene.handle; reported = 0; }
            if (reported++ < 10)
                Plugin.Log.LogWarning($"Homing orb motion: {reason}; object={shot.name}, position={shot.transform.position}, targetHandle={shot.targetHandle != null}.");
        }
    }

    // Native Start initializes maxSpeed but not velocity. Homing Update returns
    // immediately without a target, leaving newly replaced shots motionless.
    [HarmonyPatch(typeof(Projectile), "Start")]
    internal static class HomingOrbLaunchPatch
    {
        private static void Postfix(Projectile __instance, Rigidbody ___rb, ref float ___maxSpeed)
        {
            if (!UltraMiseryState.IsActive || !___rb || __instance.decorative) return;
            var marker = __instance.GetComponent<UltraMiseryHomingOrbReplacement>();
            if (!marker || __instance.GetComponentInParent<EnemyIdentifier>()) return;
            if (___rb.isKinematic)
            {
                HomingOrbMotionRules.Report(__instance, "corrected kinematic replacement at launch");
                ___rb.isKinematic = false;
            }
            // Firing code can overwrite speed with red-orb values (35-45).
            // Restore the native homing launch speed instead of outrunning turns.
            __instance.speed = 10f;
            ___maxSpeed = 10f;
            // Leave steering/velocity to native homing Update when a target exists.
#pragma warning disable CS0612, CS0618
            if (__instance.targetHandle == null && (__instance.target == null || !__instance.target.isValid))
                HomingOrbMotionRules.Report(__instance, "replacement launched without a valid target");
#pragma warning restore CS0612, CS0618
            marker.ProgressPosition = __instance.transform.position;
            marker.StalledFor = 0f;
            marker.Launched = true;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Update")]
    internal static class HomingOrbStallCleanupPatch
    {
        private static void Postfix(Projectile __instance, bool ___active, bool ___hittingPlayer)
        {
            if (!UltraMiseryState.IsActive) return;
            var marker = __instance.GetComponent<UltraMiseryHomingOrbReplacement>();
            if (!marker) return;
            if (!HomingOrbMotionRules.CanTrack(___active, __instance.decorative, ___hittingPlayer,
                __instance.undeflectable, __instance.GetComponentInParent<EnemyIdentifier>(), marker.Launched))
            {
                marker.StalledFor = 0f;
                marker.ProgressPosition = __instance.transform.position;
                return;
            }
            if ((__instance.transform.position - marker.ProgressPosition).sqrMagnitude > 0.01f)
            {
                marker.ProgressPosition = __instance.transform.position;
                marker.StalledFor = 0f;
                return;
            }
            marker.StalledFor += Time.deltaTime;
            if (!HomingOrbMotionRules.Expired(marker.StalledFor)) return;
            HomingOrbMotionRules.Report(__instance, "removed launched orb stalled for 2 seconds");
            // Do not push blocked shots through walls, explode them on a target,
            // or interfere with native held/charging projectiles and axes.
            __instance.gameObject.SetActive(false);
            Object.Destroy(__instance.gameObject);
        }
    }
}
