using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class MannequinRecoveryRules
    {
        internal sealed class Watch
        {
            public Watch() { }
            internal float Age, Stalled, Progress;
            internal int State;
        }
        private static readonly ConditionalWeakTable<Mannequin, Watch> Watches = new ConditionalWeakTable<Mannequin, Watch>();
        internal static void Reset(Mannequin enemy) => Watches.Remove(enemy);
        internal static bool ShouldRecover(float age, float stalled, bool finished, bool transition, bool supportedAction) =>
            supportedAction && !transition && age >= 8f && (finished || stalled >= 3f);

        internal static void Tick(Mannequin enemy, EnemyIdentifier eid, Animator animator)
        {
            if (!UltraMiseryState.IsActive || !eid || eid.dead || !enemy.inAction ||
                !animator || !animator.isActiveAndEnabled || animator.speed <= 0f)
            { Reset(enemy); return; }
            var watch = Watches.GetOrCreateValue(enemy);
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool progressed = state.fullPathHash != watch.State || Mathf.Abs(state.normalizedTime - watch.Progress) > 0.0001f;
            watch.Age += Time.deltaTime;
            watch.Stalled = progressed ? 0f : watch.Stalled + Time.deltaTime;
            watch.State = state.fullPathHash;
            watch.Progress = state.normalizedTime;
            bool supported = enemy.mostRecentAction == "Melee Attack" || enemy.mostRecentAction == "Projectile Attack" ||
                (enemy.mostRecentAction == "Landing" && !enemy.jumping);
            if (!ShouldRecover(watch.Age, watch.Stalled, state.normalizedTime >= 1f,
                animator.IsInTransition(0), supported)) return;
            string action = enemy.mostRecentAction;
            Reset(enemy);
            // Native cleanup closes melee damage, removes the charge projectile,
            // clears inAction and picks behavior. Leave real cooldowns intact.
            enemy.CancelActions(true);
            eid.UpdateTarget();
            if (!enemy.IsInvoking("SlowUpdate")) enemy.Invoke("SlowUpdate", 0f);
            Plugin.Log.LogInfo($"Recovered stalled Mannequin {enemy.GetInstanceID()}: action={action}, targetValid={eid.target != null && eid.target.isValid}.");
        }
    }

    [HarmonyPatch(typeof(Mannequin), "Update")]
    internal static class MannequinStalledAttackPatch
    {
        private static void Postfix(Mannequin __instance, EnemyIdentifier ___eid, Animator ___anim) =>
            MannequinRecoveryRules.Tick(__instance, ___eid, ___anim);
    }

    [HarmonyPatch(typeof(Mannequin), "StopAction", new[] { typeof(bool) })]
    internal static class MannequinCompletedAttackPatch
    {
        private static void Postfix(Mannequin __instance) => MannequinRecoveryRules.Reset(__instance);
    }
}
