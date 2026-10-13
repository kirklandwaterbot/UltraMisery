using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ULTRAKILL.Enemy;
using UnityEngine;

namespace UltraMisery
{
    internal static class EnemyAwarenessRules
    {
        internal static bool CanAttack(Component component)
        {
            if (!UltraMiseryState.IsActive || !component) return false;
            EnemyIdentifier eid = component.GetComponent<EnemyIdentifier>();
            return eid && !eid.dead && eid.enabled && eid.target != null && eid.target.isValid;
        }

        internal static bool AttackGround(bool grounded, Component component) => grounded || CanAttack(component);

        internal static bool ScannerObstructed(Ray ray, out RaycastHit hit, float distance, int mask)
        {
            hit = default;
            return !UltraMiseryState.IsActive && Physics.Raycast(ray, out hit, distance, mask);
        }
    }

    // Wrap only enemy acquisition queries, not coin ricochets, Idol beams or
    // Swordsmachine's breakable-surface probes. No scans or per-frame closures.
    [HarmonyPatch(typeof(VisionQuery), MethodType.Constructor, new[] { typeof(string), typeof(VisionQuery.TargetPredicate) })]
    internal static class OmniscientEnemyQueryPatch
    {
        private static void Postfix(VisionQuery __instance)
        {
            string name = __instance.Name;
            if (name != "Target" && name != "Nearest" && !name.EndsWith("Sight") &&
                name != "GuttertankTarget" && name != "MannequinShoot" && name != "SisyphusVision") return;
            object target = __instance.predicate.Target;
            EnemyScript script = target as EnemyScript;
            if (!script && target != null && target.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            {
                foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (typeof(EnemyScript).IsAssignableFrom(field.FieldType))
                    {
                        script = field.GetValue(target) as EnemyScript;
                        if (script) break;
                    }
            }
            if (!script) return;
            var original = __instance.predicate;
            EnemyIdentifier owner = script.GetComponent<EnemyIdentifier>();
            __instance.predicate = (TargetDataRef candidate) =>
            {
                if (!UltraMiseryState.IsActive) return original(candidate);
                if (!owner || owner.dead || owner.target == null || !owner.target.isValid ||
                    candidate.target == null || !candidate.target.GameObject || !candidate.target.GameObject.activeInHierarchy) return false;
                if (candidate.target.isEnemy && (!candidate.target.EID || candidate.target.EID.dead)) return false;
                if (!EnemyScript.CheckTarget(candidate, owner)) return false;
                // Keep omniscient acquisition, but stop pretending walls are
                // shootable forever. Portal-aware sight determines recovery.
                bool blocked = candidate.IsObstructed(script.VisionSourcePosition, LayerMaskDefaults.Get(LMD.Environment), false);
                if (ObstructedAttackRules.Observe(owner, blocked)) return false;
                return true;
            };
        }
    }

    [HarmonyPatch(typeof(EnemyScanner), "Update")]
    internal static class OmniscientEnemyScannerPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo raycast = AccessTools.Method(typeof(Physics), "Raycast", new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) });
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(raycast))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(EnemyAwarenessRules), nameof(EnemyAwarenessRules.ScannerObstructed));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("EnemyScanner sight hook changed: " + count);
        }
    }

    [HarmonyPatch(typeof(Turret), "IsTargetObstructed")]
    internal static class OmniscientSentryAimPatch
    {
        private static bool Prefix(Turret __instance, ref bool __result)
        {
            if (!EnemyAwarenessRules.CanAttack(__instance)) return true;
            if (ObstructedAttackRules.Approaching(__instance.GetComponent<EnemyIdentifier>())) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AirborneAttackDecisionPatch
    {
        // Read overrides are deliberately limited to attack decisions. Never
        // force the actual GroundCheck/Enemy fields: navigation still needs them.
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var entry in new[] {
                (typeof(ZombieProjectiles), "Update"), (typeof(ZombieProjectiles), "SlowUpdate"),
                (typeof(ZombieMelee), "Update"), (typeof(Turret), "Update"),
                (typeof(Gutterman), "AttackUpdate"), (typeof(Gutterman), "SetFiring"),
                (typeof(StatueBoss), "AttackCheck"), (typeof(Ferryman), "Update"),
                (typeof(Minotaur), "SlowUpdate"),
                (typeof(Sisyphus), "FixedUpdate") })
                yield return AccessTools.Method(entry.Item1, entry.Item2);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int ordinal = 0, patched = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                bool groundRead = instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field &&
                    ((field.DeclaringType == typeof(Enemy) && field.Name == "grounded") ||
                     (field.DeclaringType == typeof(GroundCheckEnemy) && field.Name == "onGround"));
                if (!groundRead) continue;
                int index = ordinal++;
                Type type = __originalMethod.DeclaringType;
                // Keep Ferryman animation/movement checks, Sentry running
                // animation, and Insurrectionist gravity/navigation checks real.
                if (type == typeof(Ferryman) && index != 2) continue;
                if (type == typeof(Turret) && index == 0) continue;
                if (type == typeof(Sisyphus) && index != 4) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, typeof(EnemyAwarenessRules).GetMethod(nameof(EnemyAwarenessRules.AttackGround), BindingFlags.Static | BindingFlags.NonPublic));
                patched++;
            }
            Type owner = __originalMethod.DeclaringType;
            int expected = owner == typeof(Turret) ? 4 : owner == typeof(Ferryman) ? 3 :
                owner == typeof(Sisyphus) ? 8 :
                owner == typeof(Gutterman) && __originalMethod.Name == "AttackUpdate" ? 2 : 1;
            if (patched == 0 || ordinal != expected)
                throw new InvalidOperationException("Airborne attack hook layout changed: " + __originalMethod + ": " + ordinal);
        }
    }

    // Mannequin has a separate airborne branch: its normal firing gate there
    // requires wall-clinging. Broaden only that branch, not the NavMesh checks.
    [HarmonyPatch(typeof(Mannequin), "SlowUpdate")]
    internal static class AirborneMannequinAttackPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Ldfld || !(instruction.operand is FieldInfo field) ||
                    field.DeclaringType != typeof(Mannequin) || field.Name != "clinging") continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, typeof(EnemyAwarenessRules).GetMethod(nameof(EnemyAwarenessRules.AttackGround), BindingFlags.Static | BindingFlags.NonPublic));
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Mannequin airborne firing branch changed: " + count);
        }
    }

}
