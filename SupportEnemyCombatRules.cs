using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using System.Threading;
using ULTRAKILL.Enemy;
using ULTRAKILL.Portal;
using UnityEngine;

namespace UltraMisery
{
    internal static class SupportEnemyCombatRules
    {
        [ThreadStatic] internal static EnemyIdentifier BreakableVictim;

        internal static void EnsureVisionTarget(EnemyIdentifier eid)
        {
            if (UltraMiseryState.IsActive && eid && !eid.dead &&
                !eid.GetComponent<Enemy>() && !eid.GetComponent<UltraMiserySupportTarget>())
                eid.gameObject.AddComponent<UltraMiserySupportTarget>();
        }

        internal static bool IsSupport(EnemyIdentifier eid) => eid &&
            (eid.enemyType == EnemyType.Idol || eid.enemyType == EnemyType.Deathcatcher);

        internal static bool BreakOnEnemyContact(Component attack, Collider collider, float damage)
        {
            if (!LifestealRules.AiCombatAllowed || damage <= 0f) return false;
            EnemyIdentifier victim = FriendlyFireRules.GetEnemy(collider);
            EnemyIdentifier attacker = LifestealRules.Resolve(attack);
            if (!IsSupport(victim) || victim.dead || !attacker || attacker == victim) return false;
            // Support prefabs do not all use a Body-tagged limb collider, so
            // several native attacks never reach DeliverDamage for them.
            if (victim.TryGetComponent<Idol>(out Idol idol)) idol.Death();
            else if (victim.TryGetComponent<Deathcatcher>(out Deathcatcher catcher)) catcher.Death();
            else return false;
            return true;
        }

        internal static bool IgnoredByEnemyScanner(EnemyIdentifier eid)
        {
            if (!eid) return true;
            if (!LifestealRules.AiCombatAllowed) return eid.ignoredByEnemies;
            return eid.dead || !eid.gameObject.activeInHierarchy;
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class AiSupportProjectileContactPatch
    {
        private static bool Prefix(Projectile __instance, Collider other, bool ___active)
        {
            if (!___active || !SupportEnemyCombatRules.BreakOnEnemyContact(__instance, other,
                __instance.damage * __instance.enemyDamageMultiplier)) return true;
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class AiSupportExplosionContactPatch
    {
        private static bool Prefix(Explosion __instance, Collider other) =>
            !SupportEnemyCombatRules.BreakOnEnemyContact(__instance, other,
                __instance.damage * __instance.enemyDamageMultiplier);
    }

    [HarmonyPatch(typeof(SwingCheck2), "CheckCollision")]
    internal static class AiSupportMeleeContactPatch
    {
        private static bool Prefix(SwingCheck2 __instance, Collider other) =>
            !__instance.damaging || !SupportEnemyCombatRules.BreakOnEnemyContact(__instance, other,
                __instance.enemyDamage > 0 ? __instance.enemyDamage : __instance.damage);
    }

    // These support objects need not have an Enemy body, which normally
    // registers ITarget. Supply just the aiming data, never a synthetic body.
    internal sealed class UltraMiserySupportTarget : MonoBehaviour, ITarget
    {
        private EnemyIdentifier eid;
        private Collider hitbox;
        private Rigidbody body;
        private CancellationTokenSource registration;
        private Vector3 center;
        private Quaternion cachedRotation = Quaternion.identity;
        private int cachedId;
        private void Awake()
        {
            cachedId = GetInstanceID();
            eid = GetComponent<EnemyIdentifier>();
            hitbox = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
            body = GetComponent<Rigidbody>();
            UpdateCachedTransformData();
        }
        private void Update()
        {
            if (!eid || eid.dead || !LifestealRules.AiCombatAllowed || SupportEnemyCombatRules.IgnoredByEnemyScanner(eid))
            {
                Unregister();
                return;
            }
            if (registration != null) return;
            if (!MonoSingleton<PortalManagerV2>.TryGetInstance(out PortalManagerV2 portals)) return;
            registration = new CancellationTokenSource();
            portals.TargetTracker.RegisterTarget(this, registration.Token);
        }
        private void Unregister()
        {
            if (registration == null) return;
            registration.Cancel();
            registration.Dispose();
            registration = null;
        }
        private void OnDisable() => Unregister();
        private void OnDestroy() => Unregister();
        public int Id => cachedId;
        public TargetType Type => TargetType.ENEMY;
        public EnemyIdentifier EID => eid;
        public GameObject GameObject => this ? gameObject : null;
        public Rigidbody Rigidbody => body;
        public Transform Transform => this ? transform : null;
        public Vector3 Position => center;
        public Vector3 HeadPosition => center;
        public void UpdateCachedTransformData()
        {
            // Cancellation removes tracker entries later; existing handles can
            // still ask a destroyed adapter for data during that interval.
            if (!this) return;
            center = hitbox ? hitbox.bounds.center : transform.position;
            cachedRotation = transform.rotation;
        }
        public void SetData(ref TargetData data)
        {
            data.position = data.realPosition = data.headPosition = data.realHeadPosition = center;
            data.rotation = cachedRotation;
            data.velocity = body ? body.velocity : Vector3.zero;
        }
    }

    [HarmonyPatch]
    internal static class AiCanTargetSupportEnemiesPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EnemyScanner), "CanBeTargeted");
            yield return AccessTools.Method(typeof(EnemyScanner), "Update");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            FieldInfo ignored = AccessTools.Field(typeof(EnemyIdentifier), "ignoredByEnemies");
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, ignored))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(SupportEnemyCombatRules), nameof(SupportEnemyCombatRules.IgnoredByEnemyScanner));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Support target scanner hook changed: " + count);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class AiSupportDamageScopePatch
    {
        private static bool Prefix(EnemyIdentifier __instance, float multiplier, GameObject sourceWeapon,
            out EnemyIdentifier __state)
        {
            __state = SupportEnemyCombatRules.BreakableVictim;
            EnemyIdentifier attacker = LifestealRules.Attacker;
            SupportEnemyCombatRules.BreakableVictim = LifestealRules.AiCombatAllowed &&
                SupportEnemyCombatRules.IsSupport(__instance) && !__instance.dead && multiplier > 0f &&
                !sourceWeapon && attacker && attacker != __instance ? __instance : null;
            if (SupportEnemyCombatRules.BreakableVictim == __instance &&
                __instance.enemyType == EnemyType.Deathcatcher &&
                __instance.TryGetComponent<Deathcatcher>(out Deathcatcher catcher))
            {
                // Native DeliverDamage refuses dormant Deathcatchers. Use its
                // actual death routine, including revival-list and kill cleanup.
                catcher.Death();
                return false;
            }
            return true;
        }

        private static void Finalizer(EnemyIdentifier __state) => SupportEnemyCombatRules.BreakableVictim = __state;
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "HitterCanKillIdol")]
    internal static class AiCanBreakSupportEnemiesPatch
    {
        private static void Postfix(EnemyIdentifier __instance, ref bool __result)
        {
            // Permit only a positive, attributed enemy hit, not player gunfire,
            // hazards or an unrelated/stale hitter string. The native damage
            // path retains active-state checks and performs all death cleanup.
            if (SupportEnemyCombatRules.BreakableVictim == __instance && LifestealRules.AiCombatAllowed)
                __result = true;
        }
    }
}
