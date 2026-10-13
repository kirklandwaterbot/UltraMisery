using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class AiCombatRules
    {
        internal static bool AllowsAiCombat(bool active, bool cheatsEnabled, bool enemiesAttackEachOther) =>
            active && cheatsEnabled && enemiesAttackEachOther;

        internal static EnemyType CollisionExclusionType(EnemyType original) =>
            LifestealRules.AiCombatAllowed ? (EnemyType)(-1) : original;

        internal static bool SupportRole(EnemyIdentifier eid) =>
            SupportEnemyCombatRules.IsSupport(eid) || eid.enemyType == EnemyType.Stalker;
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "AttackEnemies", MethodType.Getter)]
    internal static class StrictAiCombatTargetGatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EnemyIdentifier __instance, ref bool __result)
        {
            if (!UltraMiseryState.IsActive || AiCombatRules.SupportRole(__instance)) return;
            __result = LifestealRules.AiCombatAllowed && !ULTRAKILL.Cheats.BlindEnemies.Blind;
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Update")]
    internal static class RefreshAiCombatGatePatch
    {
        private static void Prefix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || __instance.dead) return;
            bool allowed = LifestealRules.AiCombatAllowed;
            __instance.immuneToFriendlyFire = !allowed;
            if (AiCombatRules.SupportRole(__instance)) return;
            // Flush cached offensive targets on either transition; never discard
            // an Idol's blessing target or a Stalker's sandification target.
            if (__instance.target != null && ((allowed && __instance.target.isPlayer) ||
                (!allowed && __instance.target.isEnemy)))
            {
                __instance.target = null;
                __instance.UpdateTarget();
            }
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "IgnorePlayer", MethodType.Getter)]
    internal static class AiCombatIgnorePlayerPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref bool __result)
        {
            if (LifestealRules.AiCombatAllowed) __result = true;
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "IsTypeFriendly")]
    internal static class AiSameSpeciesTargetPatch
    {
        private static void Postfix(EnemyIdentifier __instance, EnemyIdentifier owner, ref bool __result)
        {
            if (LifestealRules.AiCombatAllowed) __result = __instance == owner;
        }
    }

    [HarmonyPatch]
    internal static class AiHurtExceptionsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(EnemyIdentifier)
            .GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "CheckHurtException");
        private static void Postfix(ref bool __result)
        {
            if (LifestealRules.AiCombatAllowed) __result = false;
        }
    }

    // Only replace the same-species exclusion comparison, not the attacker's
    // actual type (used later for damage multipliers and Power reactions).
    [HarmonyPatch]
    internal static class AiSameSpeciesCollisionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Projectile), "Collided");
            yield return AccessTools.Method(typeof(SwingCheck2), "CheckEidCollision");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            bool projectile = __originalMethod.DeclaringType == typeof(Projectile);
            FieldInfo field = AccessTools.Field(__originalMethod.DeclaringType, projectile ? "safeEnemyType" : "type");
            int index = 0, replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field) && index++ == (projectile ? 1 : 0))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AiCombatRules), nameof(AiCombatRules.CollisionExclusionType)));
                    replaced++;
                }
            }
            if (replaced != 1) throw new InvalidOperationException("AI species collision hook changed: " + __originalMethod);
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class AiProjectileNoSelfHitPatch
    {
        private static bool Prefix(Projectile __instance, Collider other)
        {
            if (!LifestealRules.AiCombatAllowed) return true;
            EnemyIdentifier victim = FriendlyFireRules.GetEnemy(other);
            if (__instance.parried || __instance.playerBullet) return true;
            var tag = __instance.GetComponentInParent<UltraMiseryAttackOwner>();
            EnemyIdentifier owner = tag && tag.Owner ? tag.Owner : LifestealRules.Resolve(__instance);
            return !victim || victim != owner;
        }
    }

    // Enemy hitscan has its own two species gates, separate from Projectile.
    [HarmonyPatch]
    internal static class AiSameSpeciesBeamPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => typeof(RevolverBeam)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "PiercingShotCheck" || m.Name == "ExecuteHits");

        private static void Prefix(RevolverBeam __instance)
        {
            if (!LifestealRules.AiCombatAllowed || __instance.deflected ||
                (__instance.beamType != BeamType.Enemy && __instance.beamType != BeamType.MaliciousFace)) return;
            EnemyIdentifier owner = LifestealRules.Resolve(__instance);
            if (owner && !__instance.hitEids.Contains(owner)) __instance.hitEids.Add(owner);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo field = AccessTools.Field(typeof(RevolverBeam), "ignoreEnemyType");
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AiCombatRules), nameof(AiCombatRules.CollisionExclusionType)));
                    replaced++;
                }
            }
            if (replaced != 2) throw new InvalidOperationException("Enemy hitscan species gate changed: " + replaced);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class AiAttributedSelfDamageGuardPatch
    {
        private static bool Prefix(EnemyIdentifier __instance) =>
            !LifestealRules.AiCombatAllowed || !LifestealRules.Attacker || LifestealRules.Attacker != __instance;
    }

    [HarmonyPatch]
    internal static class AiSameSpeciesOtherRangedPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ContinuousBeam), "FixedUpdate");
            yield return AccessTools.Method(typeof(Nail), "HitEnemy");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            FieldInfo field = AccessTools.Field(__originalMethod.DeclaringType, "safeEnemyType");
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AiCombatRules), nameof(AiCombatRules.CollisionExclusionType)));
                    count++;
                }
            }
            if (count != (__originalMethod.DeclaringType == typeof(Nail) ? 1 : 2))
                throw new InvalidOperationException("Ranged species gate changed: " + __originalMethod);
        }
    }

    [HarmonyPatch(typeof(SwingCheck2), "CheckEidCollision")]
    internal static class AiMeleeNoSelfHitPatch
    {
        private static bool Prefix(SwingCheck2 __instance, EnemyIdentifier enid)
        {
            if (!LifestealRules.AiCombatAllowed) return true;
            if (__instance.eid == enid) return false;
            if (__instance.eid) __instance.eid.immuneToFriendlyFire = false;
            return true;
        }
    }
}
