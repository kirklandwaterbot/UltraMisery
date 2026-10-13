using HarmonyLib;
using UnityEngine;
using ULTRAKILL.Enemy;
using ULTRAKILL.Portal;
using UnityEngine.AI;
using System.Runtime.CompilerServices;
using System.Reflection;

namespace UltraMisery
{
    internal static class AiAttackCorrections
    {
        private sealed class Burst { public Burst() { } internal bool Active; }
        private static readonly ConditionalWeakTable<Turret, Burst> Bursts = new ConditionalWeakTable<Turret, Burst>();
        internal static void SetBurst(Turret sentry, bool active) => Bursts.GetOrCreateValue(sentry).Active = active;
        internal static bool InBurst(Turret sentry) => UltraMiseryState.IsActive && Bursts.TryGetValue(sentry, out var state) && state.Active;
        internal static EnemyIdentifier Target(Component shooter)
        {
            if (!LifestealRules.AiCombatAllowed || !shooter) return null;
            EnemyIdentifier owner = shooter.GetComponent<EnemyIdentifier>();
            var target = owner && !owner.dead ? owner.target : null;
            return target != null && target.isValid && target.isEnemy ? target.enemyIdentifier : null;
        }
        internal static Vector3 Aim(EnemyIdentifier target, Component shooter)
        {
            Enemy body = target.GetComponent<Enemy>();
            Vector3 center = body && body.chest ? body.chest.transform.position :
                target.overrideCenter ? target.overrideCenter.position : target.transform.position;
            return LargeBossTargetRules.Aim(center, shooter);
        }
    }

    [HarmonyPatch(typeof(Streetcleaner), "Update")]
    internal static class StreetcleanerResumeNavigationPatch
    {
        private static void Prefix(EnemyIdentifier ___eid, NavMeshAgent ___nma, bool ___dead, bool ___dodging)
        {
            if (!UltraMiseryState.IsActive || ___dead || ___dodging || !___eid || ___eid.target == null || !___eid.target.isValid) return;
            if (___nma && ___nma.enabled && ___nma.isOnNavMesh && !___nma.isOnOffMeshLink) ___nma.isStopped = false;
        }
    }

    [HarmonyPatch(typeof(Guttertank), "UpdateLineOfSightTimer")]
    internal static class GuttertankDirectTargetRecoveryPatch
    {
        private static void Prefix(TargetHandle ___targetHandle, ref Vector3 ___lastDimensionalTarget)
        {
            if (UltraMiseryState.IsActive && ___targetHandle != null && ___targetHandle.portals.Count == 0 &&
                DroneTargetRules.IsLive(___targetHandle.target)) ___lastDimensionalTarget = Vector3.zero;
        }
    }
    [HarmonyPatch(typeof(Streetcleaner), "UpdateVision")]
    internal static class StreetcleanerDirectTargetRecoveryPatch
    {
        private static void Postfix(TargetHandle ___targetHandle, ref Vector3 ___lastDimensionalTarget)
        {
            if (UltraMiseryState.IsActive && ___targetHandle != null && ___targetHandle.portals.Count == 0 &&
                DroneTargetRules.IsLive(___targetHandle.target)) ___lastDimensionalTarget = Vector3.zero;
        }
    }
    [HarmonyPatch(typeof(Streetcleaner), "VisionSourcePosition", MethodType.Getter)]
    internal static class StreetcleanerVisionSafetyPatch
    {
        private static bool Prefix(Streetcleaner __instance, Enemy ___mach, ref Vector3 __result)
        {
            if (!UltraMiseryState.IsActive || (___mach && ___mach.chest)) return true;
            __result = __instance.transform.position + Vector3.up;
            return false;
        }
    }
    [HarmonyPatch(typeof(Turret), "AimAt")]
    internal static class SentryBodyAimPatch
    {
        private static void Prefix(Turret __instance, ref Vector3 position, ref Vector3 direction, ref float distance, PortalTraversalV2[] traversals)
        {
            EnemyIdentifier target = AiAttackCorrections.Target(__instance);
            if (!target || (traversals != null && traversals.Length != 0)) return;
            position = AiAttackCorrections.Aim(target, __instance);
            Vector3 delta = position - __instance.VisionSourcePosition;
            direction = delta.normalized;
            distance = delta.magnitude;
        }
    }
    [HarmonyPatch(typeof(Turret), "Shoot")]
    internal static class SentryShotAimPatch
    {
        private static void Prefix(Turret __instance, bool ___isBarrelPortalCrossed, Transform ___barrelTip, ref Quaternion ___shootRotation)
        {
            EnemyIdentifier target = AiAttackCorrections.Target(__instance);
            if (!target || ___isBarrelPortalCrossed || !___barrelTip) return;
            Vector3 origin = __instance.transform.position;
            origin.y = ___barrelTip.position.y;
            Vector3 delta = AiAttackCorrections.Aim(target, __instance) - origin;
            if (delta.sqrMagnitude > 0.001f) ___shootRotation = Quaternion.LookRotation(delta);
        }
    }
    [HarmonyPatch(typeof(Turret), "StopAction")]
    internal static class SentryKeepFiringStancePatch
    {
        private static bool Prefix(Turret __instance) => !AiAttackCorrections.InBurst(__instance) || !SentryEndlessBurstPatch.HasLivingTarget(__instance);
    }
    [HarmonyPatch(typeof(Turret), "Update")]
    internal static class SentryContinueAcrossTargetsPatch
    {
        private static void Postfix(Turret __instance, bool ___aiming, Animator ___anim, ref bool ___inAction)
        {
            if (!AiAttackCorrections.InBurst(__instance)) return;
            if (!SentryEndlessBurstPatch.HasLivingTarget(__instance))
            {
                AiAttackCorrections.SetBurst(__instance, false);
                __instance.CancelAim(false);
                ___inAction = false;
                return;
            }
            ___inAction = true;
            if (___anim) ___anim.SetBool("Aiming", true);
            if (!___aiming && !__instance.IsInvoking("PreReAim") && !__instance.IsInvoking("ReAim"))
                __instance.Invoke("PreReAim", 0.25f);
        }
    }
    [HarmonyPatch(typeof(Turret), "OnDisable")]
    internal static class SentryResetBurstPatch
    {
        private static void Postfix(Turret __instance) => AiAttackCorrections.SetBurst(__instance, false);
    }
    [HarmonyPatch(typeof(Minotaur), "RamStart")]
    internal static class MinotaurRamEnemyHitboxesPatch
    {
        private static void Postfix(Minotaur __instance, EnemyIdentifier ___eid, GameObject ___ramStuff)
        {
            if (!LifestealRules.AiCombatAllowed || !___ramStuff || !___eid) return;
            var sweep = __instance.GetComponent<UltraMiseryRamSweep>() ?? __instance.gameObject.AddComponent<UltraMiseryRamSweep>();
            sweep.Begin(__instance, ___eid, ___ramStuff);
            foreach (SwingCheck2 hit in ___ramStuff.GetComponentsInChildren<SwingCheck2>(true))
            {
                hit.eid = ___eid;
                hit.type = ___eid.enemyType;
                hit.startActive = true;
                hit.DamageStart();
            }
        }
    }

    // Fast rams can cross thin enemy colliders between physics callbacks.
    // Sweep the native ram hitbox, then use native damage/parry/callback rules.
    internal sealed class UltraMiseryRamSweep : MonoBehaviour
    {
        private static readonly MethodInfo HitEnemy = AccessTools.Method(typeof(SwingCheck2), "CheckEidCollision");
        private readonly Collider[] overlaps = new Collider[64];
        private Minotaur minotaur;
        private EnemyIdentifier owner;
        private SwingCheck2[] checks;
        private Collider[] boxes;
        private Vector3 previous;
        internal void Begin(Minotaur source, EnemyIdentifier eid, GameObject ram)
        {
            minotaur = source; owner = eid;
            if (checks == null)
            {
                checks = ram.GetComponentsInChildren<SwingCheck2>(true);
                boxes = new Collider[checks.Length];
                for (int i = 0; i < checks.Length; i++) boxes[i] = checks[i].GetComponent<Collider>();
                if (checks.Length == 0) Plugin.Log.LogWarning("Minotaur ram has no native SwingCheck2 hitbox; inspect this prefab.");
            }
            previous = transform.position;
        }
        private void FixedUpdate()
        {
            if (!minotaur || !owner || owner.dead || !LifestealRules.AiCombatAllowed || minotaur.ramTimer <= 0f)
            { previous = transform.position; return; }
            Vector3 delta = transform.position - previous;
            previous = transform.position;
            for (int i = 0; i < checks.Length && minotaur.ramTimer > 0f; i++)
            {
                var check = checks[i]; var box = boxes[i];
                if (!check || !box || !box.enabled || !box.gameObject.activeInHierarchy) continue;
                Bounds swept = box.bounds;
                swept.Encapsulate(new Bounds(swept.center - delta, swept.size));
                int count = Physics.OverlapBoxNonAlloc(swept.center, swept.extents, overlaps, Quaternion.identity,
                    (1 << 10) | (1 << 12), QueryTriggerInteraction.Collide);
                for (int j = 0; j < count && minotaur.ramTimer > 0f; j++)
                {
                    Collider other = overlaps[j]; overlaps[j] = null;
                    EnemyIdentifier victim = FriendlyFireRules.GetEnemy(other);
                    if (!victim || victim == owner || victim.dead) continue;
                    if (Physics.Linecast(box.bounds.center, other.bounds.center, LayerMaskDefaults.Get(LMD.Environment), QueryTriggerInteraction.Ignore)) continue;
                    HitEnemy.Invoke(check, new object[] { victim, other });
                }
            }
        }
    }
}
