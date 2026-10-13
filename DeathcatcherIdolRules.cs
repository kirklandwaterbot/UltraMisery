using HarmonyLib;

namespace UltraMisery
{
    [HarmonyPatch(typeof(Deathcatcher), "Start")]
    internal static class DeathcatcherNoIdolRevivalSetupPatch
    {
        private static void Postfix(Deathcatcher __instance)
        {
            if (UltraMiseryState.IsActive)
                __instance.canRespawnIdols = false;
        }
    }

    [HarmonyPatch(typeof(Deathcatcher), "EnemyDeath")]
    internal static class DeathcatcherNoIdolRevivalPatch
    {
        private static bool Prefix(EnemyIdentifier eid)
        {
            return !UltraMiseryState.IsActive || !eid || eid.enemyType != EnemyType.Idol;
        }
    }
}
