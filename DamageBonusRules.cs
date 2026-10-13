using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class DamageBonusRules
    {
        // Invert each bonus independently; never invert an existing resistance.
        internal static float InvertBonus(float factor) => factor > 1f ? 1f / factor : factor;
        internal static float InvertCritical(float critical, float limb)
        {
            if (critical <= 0f || limb <= 0f) return critical;
            // Native damage = base * (1 + limb * critical). Convert that WHOLE
            // factor, not critical alone, and retain the native damage pipeline.
            return (1f / (1f + limb * critical) - 1f) / limb;
        }
        internal static float Factor(float value) => UltraMiseryState.IsActive ? InvertBonus(value) : value;
        internal static readonly MethodInfo FactorMethod = typeof(DamageBonusRules).GetMethod(nameof(Factor), BindingFlags.Static | BindingFlags.NonPublic);
    }

    [HarmonyPatch]
    internal static class InvertedEnemyLocationalBonusPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in new[] { typeof(Enemy), typeof(Zombie), typeof(Machine), typeof(Statue) })
                yield return AccessTools.DeclaredMethod(type, "GetHurt");
        }
        private static void Prefix(GameObject target, ref float critMultiplier)
        {
            if (!UltraMiseryState.IsActive || !target) return;
            float limb = target.CompareTag("Head") ? 1f :
                (target.CompareTag("Limb") || target.CompareTag("EndLimb") ? 0.5f : 0f);
            critMultiplier = DamageBonusRules.InvertCritical(critMultiplier, limb);
        }
    }

    [HarmonyPatch(typeof(Nail), "GetFodderDamageMultiplier")]
    internal static class InvertedNailSpeciesBonusPatch
    {
        private static void Postfix(ref float __result) => __result = DamageBonusRules.Factor(__result);
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class InvertedEnemyWeaknessBonusPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool weaknessElement = false;
            int count = 0;
            var code = new List<CodeInstruction>(instructions);
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "weaknessMultipliers")
                    weaknessElement = true;
                bool clamp = weaknessElement && instruction.opcode == OpCodes.Ldelem_R4;
                if (clamp) weaknessElement = false;
                clamp |= instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo total && total.Name == "totalDamageTakenMultiplier";
                // Invert burning vulnerability, not the boss-resistance divisor.
                clamp |= instruction.opcode == OpCodes.Ldc_R4 && Equals(instruction.operand, 1.5f) &&
                    i + 1 < code.Count && code[i + 1].opcode == OpCodes.Mul;
                if (clamp)
                {
                    yield return new CodeInstruction(OpCodes.Call, DamageBonusRules.FactorMethod);
                    count++;
                }
            }
            if (count != 3) throw new InvalidOperationException("Enemy damage modifier IL changed: " + count);
        }
    }

    [HarmonyPatch]
    internal static class InvertedEnemyWeakSpotBonusPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Enemy), "CalculateDamage");
            yield return AccessTools.DeclaredMethod(typeof(Statue), "GetHurt");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i];
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo field && field.Name == "extraDamageMultiplier" &&
                    i + 1 < code.Count && code[i + 1].opcode == OpCodes.Mul)
                {
                    yield return new CodeInstruction(OpCodes.Call, DamageBonusRules.FactorMethod);
                    count++;
                }
            }
            if (count != (__originalMethod.DeclaringType == typeof(Statue) ? 3 : 1))
                throw new InvalidOperationException("Weak-spot modifier IL changed: " + __originalMethod + " " + count);
        }
    }

    // These bonuses are hardcoded rather than stored in enemy weakness tables.
    // Match multiplication operands only: never touch damage values, physics,
    // parry timing/rewards, resistance divisors or explosion falloff.
    [HarmonyPatch]
    internal static class InvertedAttackSpecificBonusPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Enemy), "HandleParrying");
            yield return AccessTools.DeclaredMethod(typeof(Zombie), "GetHurt");
            yield return AccessTools.DeclaredMethod(typeof(Machine), "GetHurt");
            yield return AccessTools.DeclaredMethod(typeof(Statue), "GetHurt");
            yield return AccessTools.Method(typeof(Explosion), "Collide");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            Type owner = __originalMethod.DeclaringType;
            bool explosion = owner == typeof(Explosion);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                yield return instruction;
                if (i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Mul) continue;
                bool convert = instruction.opcode == OpCodes.Ldc_R4 &&
                    (Equals(instruction.operand, 2f) ||
                    (owner != typeof(Zombie) && Equals(instruction.operand, 1.5f)));
                // Only the two enemy damage reads, not the environmental-gib
                // count/force reads later in Explosion.Collide.
                convert |= explosion && instruction.opcode == OpCodes.Ldfld &&
                    instruction.operand is FieldInfo field && field.Name == "enemyDamageMultiplier";
                if (!convert) continue;
                yield return new CodeInstruction(OpCodes.Call, DamageBonusRules.FactorMethod);
                count++;
            }
            int expected = explosion ? 4 : owner == typeof(Enemy) ? 2 : 1;
            if (count != expected)
                throw new InvalidOperationException("Attack bonus IL changed: " + __originalMethod + " " + count);
        }
    }

    [HarmonyPatch]
    internal static class InvertedAirborneHuskBonusPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(Enemy), "GetHurt");
            yield return AccessTools.DeclaredMethod(typeof(Zombie), "GetHurt");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!found && instruction.opcode == OpCodes.Ldc_R4 && Equals(instruction.operand, 1.5f))
                {
                    yield return new CodeInstruction(OpCodes.Call, DamageBonusRules.FactorMethod);
                    found = true;
                }
            }
            if (!found) throw new InvalidOperationException("Airborne husk modifier IL changed");
        }
    }
}
