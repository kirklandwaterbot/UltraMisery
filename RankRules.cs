using HarmonyLib;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UltraMisery
{
    internal static class RankTimeAllowance
    {
        internal static float ForEvaluation(float seconds, bool active, bool timeCategory) =>
            active && timeCategory ? seconds * 0.5f : seconds;
    }

    [HarmonyPatch(typeof(StatsManager), "GetRanks")]
    internal static class DoubleRankTimeAllowancePatch
    {
        private static void Prefix(StatsManager __instance, int[] ranksToCheck, bool reverse, ref float value)
        {
            // Both the live stats panel and final results use this method.
            // Scale only the evaluation value: the timer, recorded completion
            // time and authored thresholds stay untouched, so reloads cannot
            // compound the allowance and other difficulties remain vanilla.
            value = RankTimeAllowance.ForEvaluation(value, UltraMiseryState.IsActive,
                reverse && object.ReferenceEquals(ranksToCheck, __instance.timeRanks));
        }
    }

    [HarmonyPatch(typeof(StyleHUD), "AddPoints")]
    internal static class RemovedAutomaticStyleBonusesPatch
    {
        private static bool loggedEnraged;
        private static bool loggedHereComesTheBoom;

        private static bool Prefix(
            StyleHUD __instance,
            string pointID,
            string prefix,
            string postfix)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            if (string.Equals(pointID, "ultrakill.friendlyfire", System.StringComparison.OrdinalIgnoreCase))
                return false;

            string localized = __instance ? __instance.GetLocalizedName(pointID ?? string.Empty) : string.Empty;
            string canonical = Canonicalize((pointID ?? string.Empty) + " " +
                                            (localized ?? string.Empty) + " " +
                                            (prefix ?? string.Empty) + " " +
                                            (postfix ?? string.Empty));

            if (canonical.Contains("FRIENDLYFIRE")) return false;

            if (canonical.Contains("HERECOMESTHEBOOM"))
            {
                if (!loggedHereComesTheBoom)
                {
                    loggedHereComesTheBoom = true;
                    Plugin.Log.LogInfo("Removed the +HERE COMES THE BOOM style bonus in UltraMisery.");
                }

                return false;
            }

            if (canonical.Contains("ENRAGED"))
            {
                if (!loggedEnraged)
                {
                    loggedEnraged = true;
                    Plugin.Log.LogInfo("Removed the +ENRAGED style bonus in UltraMisery.");
                }

                return false;
            }

            return true;
        }

        private static string Canonicalize(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }

            return builder.ToString();
        }
    }

    [HarmonyPatch(typeof(StatsManager), "GetFinalRank")]
    internal static class POrNoRankPatch
    {
        private static void Postfix(StatsManager __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            if (__instance.rankScore == 12 && !BoundaryRules.InvalidRun && SecretBossRules.AreAllDefeated())
            {
                return;
            }

            // -1 and an underscore are the game's own "no rank" representation.
            __instance.rankScore = -1;
            if (!__instance.fr)
            {
                return;
            }

            __instance.fr.SetRank("<color=#FFFFFF>_</color>");
            Transform rankParent = __instance.fr.totalRank.transform.parent;
            if (rankParent)
            {
                Image background = rankParent.GetComponent<Image>();
                if (background)
                {
                    background.color = Color.green;
                }
            }
        }
    }

    internal static class SecretBossRules
    {
        internal static bool AreAllDefeated()
        {
            return RequiredSecretBosses.AllDefeated();
        }

    }
}
