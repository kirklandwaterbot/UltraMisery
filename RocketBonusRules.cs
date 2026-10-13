using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class UltraMiseryRocketDamageCorrection : MonoBehaviour
    {
        internal float Factor = 1f;
    }

    internal static class RocketBonusRules
    {
        [ThreadStatic] internal static float CurrentCorrection;
        internal static GameObject Mark(GameObject spawned, Grenade grenade, bool super)
        {
            if (!UltraMiseryState.IsActive || !super || !grenade.rocket || !spawned || !grenade.explosion) return spawned;
            Explosion[] baseline = grenade.explosion.GetComponentsInChildren<Explosion>(true);
            Explosion[] boosted = spawned.GetComponentsInChildren<Explosion>(true);
            // Match authored child order; leave unmatched prefab layouts alone.
            if (baseline.Length != boosted.Length) return spawned;
            for (int i = 0; i < boosted.Length; i++)
            {
                if (baseline[i].damage <= 0 || boosted[i].damage <= baseline[i].damage) continue;
                float ratio = (float)baseline[i].damage / boosted[i].damage;
                // Existing hit already includes the bonus: divide by it twice
                // to replace multiplication by its reciprocal.
                boosted[i].gameObject.AddComponent<UltraMiseryRocketDamageCorrection>().Factor = ratio * ratio;
            }
            return spawned;
        }
    }

    [HarmonyPatch(typeof(Grenade), "Explode")]
    internal static class InvertedSuperRocketBonusPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" && method.ReturnType == typeof(GameObject))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Call, typeof(RocketBonusRules).GetMethod("Mark", BindingFlags.Static | BindingFlags.NonPublic));
                    count++;
                }
            }
            if (count != 3) throw new InvalidOperationException("Rocket explosion spawn IL changed: " + count);
        }
    }

    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class RocketDamageCorrectionScopePatch
    {
        private static void Prefix(Explosion __instance, out float __state)
        {
            __state = RocketBonusRules.CurrentCorrection;
            var correction = __instance.GetComponent<UltraMiseryRocketDamageCorrection>();
            RocketBonusRules.CurrentCorrection = UltraMiseryState.IsActive && correction ? correction.Factor : 1f;
        }
        private static void Finalizer(float __state) => RocketBonusRules.CurrentCorrection = __state;
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class ApplyRocketDamageCorrectionPatch
    {
        private static void Prefix(ref float multiplier)
        {
            if (UltraMiseryState.IsActive && RocketBonusRules.CurrentCorrection > 0f && RocketBonusRules.CurrentCorrection < 1f)
                multiplier *= RocketBonusRules.CurrentCorrection;
        }
    }
}
