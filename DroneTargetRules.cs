using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ULTRAKILL.Enemy;
using UnityEngine;

namespace UltraMisery
{
    internal static class DroneTargetRules
    {
        internal static bool IsLive(ITarget target)
        {
            if (target == null || (target is Object unityTarget && !unityTarget)) return false;
            GameObject obj = target.GameObject;
            if (!obj || !obj.activeInHierarchy) return false;
            if (target is UltraMiserySupportTarget && !LifestealRules.AiCombatAllowed) return false;
            return !target.isEnemy || (target.EID && !target.EID.dead);
        }
    }

    // The shared Drone component also drives Virtues/Providence: stale handle
    // protection is safe for all three, but the firing change below is Drone-only.
    [HarmonyPatch]
    internal static class DroneStaleTargetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "Update", "FixedUpdate", "SightCheck", "Shoot" })
                yield return AccessTools.Method(typeof(Drone), name);
        }

        private static void Prefix(EnemyIdentifier ___eid, ref TargetHandle ___droneTargetHandle,
            ref TargetData ___droneTargetData, ref Vector3 ___lastDimensionalTarget)
        {
            if (!UltraMiseryState.IsActive || ___droneTargetHandle == null) return;
            bool switchedEnemy = ___eid && ___eid.target != null && ___eid.target.isEnemy &&
                ___droneTargetHandle.target != null && ___droneTargetHandle.target.EID != ___eid.target.enemyIdentifier;
            if (!switchedEnemy && DroneTargetRules.IsLive(___droneTargetHandle.target)) return;
            ___droneTargetHandle = null;
            ___droneTargetData = default;
            ___lastDimensionalTarget = Vector3.zero;
            // Native SightCheck runs every 0.25s and acquires a new handle.
            // Do not call it here: that would multiply its Invoke loop.
        }
    }

    [HarmonyPatch(typeof(Drone), "ObstructionCheck", new[] { typeof(TargetData) })]
    internal static class OmniscientDroneFiringPatch
    {
        private static bool Prefix(Drone __instance, EnemyIdentifier ___eid, TargetData data, ref bool __result)
        {
            if (!EnemyAwarenessRules.CanAttack(__instance) || ___eid.enemyType != EnemyType.Drone) return true;
            if (ObstructedAttackRules.Approaching(___eid)) return true;
            // Acquisition already ignores occlusion. Match it at the separate
            // firing gate, without bypassing cooldown, charge or targetSpotted.
            __result = data.handle != null && DroneTargetRules.IsLive(data.handle.target);
            return false;
        }
    }
}
