using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class AttackRecoveryRules
    {
        internal sealed class Timer { public Timer() { } internal float Age; }
        private static readonly ConditionalWeakTable<Component, Timer> Timers = new ConditionalWeakTable<Component, Timer>();
        internal static void Reset(Component component) => Timers.Remove(component);

        internal static bool Expired(Component component, bool busy, Animator animator)
        {
            if (!UltraMiseryState.IsActive || !busy || !component || !component.gameObject.activeInHierarchy)
            {
                Reset(component);
                return false;
            }
            // Animation-time budget, not a forced shorter attack cooldown.
            // Allows real long animations but cannot leave a missed animation
            // event holding the sentinel cooldown or inAction flag indefinitely.
            Timer timer = Timers.GetOrCreateValue(component);
            if (animator && animator.enabled && animator.speed > 0f)
                timer.Age += Time.deltaTime * animator.speed;
            if (timer.Age < 8f) return false;
            Reset(component);
            return true;
        }

        internal static void StopAudioIfPresent(AudioSource source)
        {
            if (source || !UltraMiseryState.IsActive) source.Stop();
        }
    }

    [HarmonyPatch]
    internal static class FreshAirborneAttackAnimationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZombieProjectiles), "Swing");
            yield return AccessTools.Method(typeof(ZombieMelee), "Swing");
            yield return AccessTools.Method(typeof(Turret), "StartWindup");
        }
        private static void Prefix(EnemyScript __instance, Animator ___anim)
        {
            if (!EnemyAwarenessRules.CanAttack(__instance)) return;
            AttackRecoveryRules.Reset(__instance);
            Enemy body = __instance.GetComponent<Enemy>();
            if (!body || body.grounded || !___anim) return;
            // Native OnFall cleanup is restored. Once a NEW attack is eligible,
            // release the falling animation latch without falsifying physics.
            ___anim.ResetTrigger("StartFalling");
            ___anim.SetBool("Falling", false);
        }
    }

    [HarmonyPatch(typeof(ZombieProjectiles), "Update")]
    internal static class ProjectileHuskAttackRecoveryPatch
    {
        private static void Postfix(ZombieProjectiles __instance, EnemyIdentifier ___eid, Animator ___anim)
        {
            if (!___eid || ___eid.dead) { AttackRecoveryRules.Reset(__instance); return; }
            if (!AttackRecoveryRules.Expired(__instance, __instance.swinging, ___anim)) return;
            __instance.DamageEnd();
            __instance.CancelAttack();
            __instance.SwingEnd(); // Restore navigation and a normal cooldown.
        }
    }

    [HarmonyPatch(typeof(ZombieMelee), "Update")]
    internal static class MeleeHuskAttackRecoveryPatch
    {
        private static void Postfix(ZombieMelee __instance, EnemyIdentifier ___eid, Animator ___anim, bool ___inAction)
        {
            if (!___eid || ___eid.dead) { AttackRecoveryRules.Reset(__instance); return; }
            if (!AttackRecoveryRules.Expired(__instance, ___inAction || __instance.damaging, ___anim)) return;
            __instance.DamageEnd();
            __instance.CancelAttack();
            __instance.SwingEnd();
        }
    }

    [HarmonyPatch(typeof(MaliciousFace), "Update")]
    internal static class MaliciousFaceMissingChargeAudioPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo stop = AccessTools.Method(typeof(AudioSource), "Stop", Type.EmptyTypes);
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(stop))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(AttackRecoveryRules), nameof(AttackRecoveryRules.StopAudioIfPresent));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Malicious Face charge-audio hook changed: " + count);
        }
    }

    [HarmonyPatch(typeof(SwordsMachine), "HeatSwordThrow")]
    internal static class SwordsmachineLostThrowTargetPatch
    {
        private static bool Prefix(SwordsMachine __instance, EnemyIdentifier ___eid, Animator ___anim)
        {
            if (!UltraMiseryState.IsActive || !___eid || ___eid.dead) return true;
            if (___eid.target != null && ___eid.target.isValid) return true;
            ___eid.UpdateTarget();
            if (___eid.target != null && ___eid.target.isValid) return true;
            __instance.DamageStop();
            __instance.CoolSword();
            __instance.StopAction();
            if (___anim) ___anim.SetTrigger("AnimationCancel");
            return false;
        }
    }
}
