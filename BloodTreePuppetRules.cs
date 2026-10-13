using HarmonyLib;

namespace UltraMisery
{
    [HarmonyPatch(typeof(EnemyIdentifier), "PuppetSpawn")]
    internal static class BloodTreePuppetSpawnPatch
    {
        private static void Prefix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance || !__instance.sandified)
            {
                return;
            }

            // Stock ULTRAKILL intentionally kills a Puppet if PuppetSpawn runs
            // while it is sandified. Blood-tree and Deathcatcher respawns can
            // inherit UltraMisery's global Sanded flag before becoming Puppets,
            // so clear it before the stock spawn lifecycle evaluates that rule.
            __instance.Desandify(false);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Sandify")]
    internal static class BloodTreePuppetSandifyPatch
    {
        private static bool Prefix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance || !__instance.puppet)
            {
                return true;
            }

            // EnemyIdentifier.Sandify calls InstaKill for every Puppet. Puppets
            // are infinite progression fodder for Hungry Suicide Trees, so they
            // must be the one Sanded exception or 7-3/8-3 cannot be completed.
            if (__instance.sandified)
            {
                __instance.Desandify(false);
            }

            return false;
        }
    }
}
