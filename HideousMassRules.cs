using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class HideousMassPhaseRules
    {
        internal static bool EnterFinalPhase(Mass mass, Animator animator, bool battleMode,
            bool inAction, bool inSemiAction)
        {
            if (!UltraMiseryState.IsActive || !mass || mass.isDead || mass.crazyMode || !animator)
                return false;

            // Preserve intro/transform animations. Once they finish, ChooseAction
            // retries this transition before any normal attacks or spear windup.
            if (inAction || inSemiAction) return true;
            if (battleMode)
            {
                mass.ToScout();
                return true;
            }

            // The native low-health branch sets this animator parameter. The
            // flip animation then calls CrazyReady, which sets crazyMode and
            // schedules both rapid-fire streams. Calling CrazyReady here would
            // skip the flip and risk scheduling duplicate firing loops.
            if (!animator.GetBool("Crazy"))
                animator.SetBool("Crazy", true);
            return true;
        }
    }

    [HarmonyPatch(typeof(Mass), "Start")]
    internal static class HideousMassSpawnFinalPhasePatch
    {
        private static void Postfix(Mass __instance, Animator ___anim, bool ___battleMode,
            bool ___inAction, bool ___inSemiAction)
        {
            HideousMassPhaseRules.EnterFinalPhase(__instance, ___anim, ___battleMode, ___inAction, ___inSemiAction);
        }
    }

    [HarmonyPatch(typeof(Mass), "ChooseAction")]
    internal static class HideousMassFinalPhasePriorityPatch
    {
        private static bool Prefix(Mass __instance, Animator ___anim, bool ___battleMode,
            bool ___inAction, bool ___inSemiAction)
        {
            return !HideousMassPhaseRules.EnterFinalPhase(__instance, ___anim, ___battleMode, ___inAction, ___inSemiAction);
        }
    }
}
