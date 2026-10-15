using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    [HarmonyPatch(typeof(HookArm), "FixedUpdate")]
    internal static class WhiplashEnemyPullBalancePatch
    {
        internal static bool LightTarget(bool native, bool active, bool livingEnemy) =>
            native && !(active && livingEnemy);

        internal static bool Classify(bool native, EnemyIdentifier enemy) =>
            LightTarget(native, WeaponBalanceRules.Active, enemy && !enemy.dead);

        private static void Prefix(EnemyIdentifier ___caughtEid, ref bool ___lightTarget,
            ref GroundCheckEnemy ___enemyGroundCheck, ref Rigidbody ___enemyRigidbody)
        {
            // Handle a tether already active when UltraMisery is selected.
            // Release the light-enemy ground override before switching pull direction.
            if (!___lightTarget || Classify(true, ___caughtEid)) return;
            if (___enemyGroundCheck) ___enemyGroundCheck.StopForceOff();
            ___enemyGroundCheck = null;
            ___enemyRigidbody = null;
            ___lightTarget = false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var target = typeof(HookArm).GetField("caughtEid", BindingFlags.Instance | BindingFlags.NonPublic);
            if (target == null) throw new MissingFieldException(typeof(HookArm).FullName, "caughtEid");
            int assignments = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && instruction.operand is FieldInfo field &&
                    field.DeclaringType == typeof(HookArm) && field.Name == "lightTarget")
                {
                    // Apply before the initial capture and Drone reclassification can
                    // enter the native enemy-drag path. Keep the native heavy pull path.
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Ldfld, target);
                    yield return new CodeInstruction(OpCodes.Call,
                        WeaponPatchReflection.Method(typeof(WhiplashEnemyPullBalancePatch), "Classify"));
                    assignments++;
                }
                yield return instruction;
            }
            if (assignments != 4) throw new InvalidOperationException("Whiplash target classification changed: " + assignments);
        }
    }
}
