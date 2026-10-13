using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class IdolAndDeathcatcherNoKnuckleblastPatch
    {
        private static bool Prefix(Explosion __instance, Collider other)
        {
            if (!UltraMiseryState.IsActive || !__instance.playerProjectileForceDirection.HasValue || !other)
            {
                return true;
            }

            // playerProjectileForceDirection is assigned only by Punch.BlastCheck
            // in the current game build, making it a reliable Knuckleblaster
            // shockwave marker. Direct punches use DeliverDamage and remain valid.
            return !other.GetComponentInParent<Idol>() &&
                   !other.GetComponentInParent<Deathcatcher>();
        }
    }

    [HarmonyPatch(typeof(Streetcleaner), "HandleCanisterHit")]
    internal static class StreetcleanerNoCanisterHitPatch
    {
        private static bool Prefix()
        {
            return !UltraMiseryState.IsActive;
        }
    }

    [HarmonyPatch(typeof(Streetcleaner), "CanisterExplosion")]
    internal static class StreetcleanerNoCanisterExplosionPatch
    {
        private static bool Prefix(Streetcleaner __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            __instance.canisterHit = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Mindflayer), "DeathExplosion")]
    internal static class MindflayerNoExplosionPatch
    {
        private static bool Prefix(Mindflayer __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            EnemyIdentifier enemy = __instance.GetComponent<EnemyIdentifier>();
            if (enemy)
            {
                for (int index = enemy.drillers.Count - 1; index >= 0; index--)
                {
                    if (enemy.drillers[index])
                    {
                        Object.Destroy(enemy.drillers[index].gameObject);
                    }
                }
            }

            if (__instance.tempBeam)
            {
                __instance.tempBeam.DetachAndTurnOff();
            }

            Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    [HarmonyPatch(typeof(Gutterman), "Explode")]
    internal static class GuttermanNoExplosionPatch
    {
        private static bool Prefix(Gutterman __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            if (!__instance.exploded)
            {
                __instance.exploded = true;
                Object.Destroy(__instance.gameObject);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Gutterman), "ShieldBreak")]
    internal static class GuttermanUnbreakableShieldPatch
    {
        // All native break sources converge here (punch/hammer, mines,
        // Mindflayer explosions and Guttertank melee). Skip before changing
        // the shield, animation, swingcheck, action state or style rewards.
        private static bool Prefix() => !UltraMiseryState.IsActive;
    }

    [HarmonyPatch(typeof(Grenade), "get_frozen")]
    internal static class GuttertankRocketFreezePatch
    {
        private static void Postfix(Grenade __instance, ref bool __result)
        {
            if (UltraMiseryState.IsActive && IsProtectedEnemyRocket(__instance))
            {
                __result = false;
            }
        }

        internal static bool IsProtectedEnemyRocket(Grenade grenade)
        {
            if (!grenade || !grenade.rocket)
                return false;

            EnemyIdentifier owner = grenade.originEnemy ? grenade.originEnemy : LifestealRules.Resolve(grenade);
            if (!owner) return false;
            if (ProtectedOwnerType(owner.enemyType))
                return true;

            // The standalone defence launcher can use a shared enemy identity.
            // Identify its authored prefab as well as the Earthmover owner type.
            for (Transform current = owner.transform; current; current = current.parent)
            {
                if (current.name.StartsWith("CentaurRocketLauncher", System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static bool ProtectedOwnerType(EnemyType type) => type == EnemyType.Guttertank ||
            type == EnemyType.Centaur || type == EnemyType.BigJohnator;
    }

    [HarmonyPatch(typeof(Grenade), "PlayerRideStart")]
    internal static class GuttertankRocketRidePatch
    {
        private static bool Prefix(Grenade __instance)
        {
            return !UltraMiseryState.IsActive || !GuttertankRocketFreezePatch.IsProtectedEnemyRocket(__instance);
        }
    }

    [HarmonyPatch(typeof(Turret), "Interrupt")]
    internal static class SentryNoInterruptPatch
    {
        private static bool Prefix()
        {
            return !UltraMiseryState.IsActive;
        }
    }

    [HarmonyPatch(typeof(Turret), "Shoot")]
    internal static class SentryEndlessBurstPatch
    {
        internal static bool HasLivingTarget(Turret sentry)
        {
            EnemyIdentifier enemy = sentry.GetComponent<EnemyIdentifier>();
            if (!enemy || enemy.dead) return false;
            if (LifestealRules.AiCombatAllowed && (enemy.target == null || !enemy.target.isValid ||
                (enemy.target.isEnemy && (!enemy.target.enemyIdentifier || enemy.target.enemyIdentifier.dead)))) enemy.UpdateTarget();
            if (enemy.target == null || !enemy.target.isValid || (enemy.target.isEnemy && (!enemy.target.enemyIdentifier || enemy.target.enemyIdentifier.dead))) return false;
            return !enemy.target.isPlayer || !MonoSingleton<NewMovement>.Instance || !MonoSingleton<NewMovement>.Instance.dead;
        }

        private static void Postfix(Turret __instance, Animator ___anim, ref bool ___inAction)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            // Keep one native re-aim callback, with no shot-count limit.
            __instance.CancelInvoke("PreReAim");
            __instance.CancelInvoke("ReAim");
            if (HasLivingTarget(__instance))
            {
                AiAttackCorrections.SetBurst(__instance, true);
                ___inAction = true;
                if (___anim) ___anim.SetBool("Aiming", true);
                __instance.Invoke("PreReAim", 0.25f);
            }
        }
    }

    [HarmonyPatch]
    internal static class SentryEndlessBurstTargetGuardPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Turret), "PreReAim");
            yield return AccessTools.Method(typeof(Turret), "ReAim");
            yield return AccessTools.Method(typeof(Turret), "Shoot");
            yield return AccessTools.Method(typeof(Turret), "StartWindup");
        }
        private static bool Prefix(Turret __instance)
        {
            if (!UltraMiseryState.IsActive) return true;
            if (SentryEndlessBurstPatch.HasLivingTarget(__instance)) return true;
            __instance.CancelAim(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(SwordsMachine), "Knockdown")]
    internal static class SwordsmachineNoKnockdownPatch
    {
        private static bool Prefix()
        {
            return !UltraMiseryState.IsActive;
        }
    }

    [HarmonyPatch(typeof(SwordsMachine), "Down")]
    internal static class SwordsmachineNoDownPatch
    {
        private static bool Prefix()
        {
            return !UltraMiseryState.IsActive;
        }
    }

    [HarmonyPatch(typeof(SwordsMachine), "SetSpeed")]
    internal static class SwordsmachineNearlyInstantRecoveryPatch
    {
        private static void Postfix(bool ___inPhaseChange, Animator ___anim)
        {
            if (!UltraMiseryState.IsActive || !___anim)
            {
                return;
            }

            // Normal gameplay knockdowns are blocked above. This removes the
            // remaining visible recovery hitch and compresses the mandatory
            // first-to-second-phase knockdown without skipping its events.
            ___anim.SetFloat("RecoverySpeedMultiplier", 30f);
            if (___inPhaseChange)
            {
                ___anim.speed = 25f;
            }
        }
    }

    [HarmonyPatch(typeof(SwordsMachine), "KnockdownEnd")]
    internal static class SwordsmachineRestoreSpeedAfterPhasePatch
    {
        private static void Postfix(SwordsMachine __instance)
        {
            if (UltraMiseryState.IsActive)
            {
                __instance.SetSpeed();
            }
        }
    }

    [HarmonyPatch(typeof(GabrielBase), "Start")]
    internal static class GabrielStartsInSecondPhasePatch
    {
        private static void Postfix(GabrielBase __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            // Enable the phase-two attack branches from the first actionable
            // frame without running the normal half-health juggle sequence.
            __instance.secondPhase = true;
            __instance.juggled = false;
            __instance.attackCooldown = 0f;
            __instance.readyTaunt = false;
            __instance.burstLength = (__instance.difficulty > 0) ? __instance.difficulty : 1;

            GabrielVoice voice = __instance.GetComponent<GabrielVoice>();
            if (voice)
            {
                voice.secondPhase = true;
            }
        }
    }

    [HarmonyPatch(typeof(GabrielBase), "UpdateCooldowns")]
    internal static class GabrielContinuousAttackPatch
    {
        private static void Prefix(GabrielBase __instance)
        {
            if (UltraMiseryState.IsActive)
            {
                // Prevent the stock cooldown branch from consuming readyTaunt.
                // UltraMisery plays the queued line below once the voice channel
                // is actually free, while Gabriel is allowed to keep attacking.
                __instance.attackCooldown = 0f;
            }
        }

        private static void Postfix(GabrielBase __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            __instance.attackCooldown = 0f;
            if (!__instance.readyTaunt)
            {
                return;
            }

            GabrielVoice voice = __instance.GetComponent<GabrielVoice>();
            if (!voice)
            {
                return;
            }

            AudioSource audio = voice.GetComponent<AudioSource>();
            if (audio && audio.isPlaying)
            {
                // Keep the taunt queued instead of losing it to a hurt or
                // phase-change line with higher voice priority.
                return;
            }

            AccessTools.Field(typeof(GabrielVoice), "priority")?.SetValue(voice, 0);
            voice.Taunt();
            __instance.readyTaunt = false;
        }
    }

    [HarmonyPatch(typeof(GabrielBase), "JuggleStart")]
    internal static class GabrielNoHalfHealthIntermissionPatch
    {
        private static bool Prefix(GabrielBase __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance.secondPhase)
            {
                return true;
            }

            __instance.juggled = false;
            __instance.inAction = false;
            __instance.attackCooldown = 0f;
            __instance.readyTaunt = false;
            __instance.burstLength = (__instance.difficulty > 0) ? __instance.difficulty : 1;
            GabrielVoice voice = __instance.GetComponent<GabrielVoice>();
            if (voice)
            {
                voice.PhaseChange();
            }
            __instance.EnrageNow();
            __instance.onSecondPhaseStart?.Invoke("");
            return false;
        }
    }

    [HarmonyPatch(typeof(ActivateNextWaveHP), "Update")]
    internal static class PanopticonMustDiePatch
    {
        private static bool Prefix(ActivateNextWaveHP __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance.target ||
                __instance.target.enemyType != EnemyType.FleshPanopticon)
            {
                return true;
            }

            // The stock P-2 transition fires at a health threshold. Hold that
            // transition until the Panopticon actually reaches its death state.
            return __instance.target.dead;
        }
    }

    [HarmonyPatch(typeof(FleshPrison), "Start")]
    internal static class PanopticonNoSisyphusBreakoutPatch
    {
        private static void Postfix(FleshPrison __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance.altVersion)
            {
                return;
            }

            // P-2 wires Sisyphus' premature breakout to Panopticon's
            // onFirstHeal event. Removing that event is the proven full-fight
            // approach used by UltraPain. The death-gated ActivateNextWaveHP
            // patch above still allows the authored transition after 0 HP.
            __instance.onFirstHeal = new UltrakillEvent();
            Plugin.Log.LogInfo("Disabled Flesh Panopticon's early Sisyphus breakout; full defeat is required.");
        }
    }

    [HarmonyPatch(typeof(StatueBoss), "Start")]
    internal static class CerberusGroupActivationPatch
    {
        private static void Postfix(StatueBoss __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            ActivateNextWave wave = __instance.GetComponentInParent<ActivateNextWave>();
            if (!wave)
            {
                return;
            }

            bool changed = false;
            foreach (EnemyIdentifier enemy in wave.GetComponentsInChildren<EnemyIdentifier>(true))
            {
                if (enemy.enemyType != EnemyType.Cerberus)
                {
                    continue;
                }

                enemy.dontCountAsKills = false;
                if (!enemy.gameObject.activeSelf)
                {
                    enemy.gameObject.SetActive(true);
                    changed = true;
                }
            }

            if (changed)
            {
                wave.CountEnemies();
            }
        }
    }
}
