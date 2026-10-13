using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class InsurrectionistRules
    {
        internal static bool Applies(Component component) => UltraMiseryState.IsActive && component && component.GetComponent<Sisyphus>();
    }

    [HarmonyPatch(typeof(Sisyphus), "Knockdown")]
    internal static class InsurrectionistNoKnockdownPatch
    {
        private static bool Prefix(Sisyphus __instance)
        {
            // Preserve any death-specific animation path; living enemies cannot stagger.
            Enemy body = __instance.GetComponent<Enemy>();
            return !UltraMiseryState.IsActive || (body && body.health <= 0f);
        }
    }

    // Keep ordinary knockback immunity, but let native parry checks, rewards,
    // boulder deflection and GotParried recovery run without overrides.
    [HarmonyPatch(typeof(Enemy), "KnockBack")]
    internal static class InsurrectionistNoKnockbackPatch
    {
        private static bool Prefix(Enemy __instance) => !InsurrectionistRules.Applies(__instance);
    }
}
