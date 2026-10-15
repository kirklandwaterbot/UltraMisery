using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class EnemyMineRules
    {
        internal static bool Blocked(bool active, EnemyType owner) => active &&
            (owner == EnemyType.Guttertank || owner == EnemyType.BigJohnator);
        internal static bool Blocked(Landmine mine)
        {
            if (!mine) return false;
            var owner = mine.originEnemy ? mine.originEnemy : LifestealRules.Resolve(mine);
            return owner && Blocked(UltraMiseryState.IsActive, owner.enemyType);
        }
    }
    [HarmonyPatch(typeof(Landmine), "Parry")]
    internal static class EnemyMineParryBalancePatch
    {
        private static bool Prefix(Landmine __instance) => !EnemyMineRules.Blocked(__instance);
    }
    [HarmonyPatch(typeof(Landmine), "Activate")]
    internal static class EnemyMineParryZoneBalancePatch
    {
        private static void Postfix(Landmine __instance, GameObject ___parryZone)
        {
            if (EnemyMineRules.Blocked(__instance) && ___parryZone) ___parryZone.SetActive(false);
        }
    }
    [HarmonyPatch(typeof(Punch), "TryParryProjectile")]
    internal static class EnemyMinePunchBalancePatch
    {
        private static bool Prefix(Transform target, ref bool __result)
        {
            if (!target || !EnemyMineRules.Blocked(target.GetComponent<Landmine>())) return true;
            __result = false; // No parry healing, hitstop, style or mine redirection.
            return false;
        }
    }
}
