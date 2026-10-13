using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal static class GuttermanAimRules
    {
        internal static float Spread(float min, float max) => UltraMiseryState.IsActive ? 0f : UnityEngine.Random.Range(min, max);
        internal static Quaternion Turn(Quaternion from, Quaternion to, float degrees) =>
            Quaternion.RotateTowards(from, to, degrees * (UltraMiseryState.IsActive ? 1.5f : 1f));
        internal static Quaternion Aim(Quaternion from, Quaternion to, float amount) =>
            Quaternion.Lerp(from, to, UltraMiseryState.IsActive ? 1f - Mathf.Pow(1f - Mathf.Clamp01(amount), 1.5f) : amount);
        internal static Vector3 Track(Vector3 from, Vector3 to, float distance) =>
            Vector3.MoveTowards(from, to, distance * (UltraMiseryState.IsActive ? 1.5f : 1f));
    }

    internal sealed class UltraMiseryGuttermanTurnRate : MonoBehaviour
    {
        private NavMeshAgent agent;
        private float original;
        private bool boosted;
        private void Awake() { agent = GetComponent<NavMeshAgent>(); }
        private void Update()
        {
            if (!agent) return;
            if (UltraMiseryState.IsActive && !boosted)
            {
                original = agent.angularSpeed;
                agent.angularSpeed = original * 1.5f;
                boosted = true;
            }
            else if (!UltraMiseryState.IsActive) Restore();
        }
        private void Restore() { if (agent && boosted) agent.angularSpeed = original; boosted = false; }
        private void OnDisable() => Restore();
    }

    [HarmonyPatch(typeof(Gutterman), "Start")]
    internal static class GuttermanTurnRateSetupPatch
    {
        private static void Postfix(Gutterman __instance)
        {
            if (!__instance.GetComponent<UltraMiseryGuttermanTurnRate>())
                __instance.gameObject.AddComponent<UltraMiseryGuttermanTurnRate>();
        }
    }

    [HarmonyPatch]
    internal static class GuttermanAccurateFastAimPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "FixedUpdate", "AttackUpdate", "LateUpdate" })
                yield return AccessTools.Method(typeof(Gutterman), name);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method)
                {
                    string replacement = null;
                    if (__originalMethod.Name == "FixedUpdate" && method.DeclaringType == typeof(UnityEngine.Random) && method.Name == "Range" && method.ReturnType == typeof(float)) replacement = "Spread";
                    if (method.DeclaringType == typeof(Quaternion) && method.Name == "RotateTowards") replacement = "Turn";
                    if (method.DeclaringType == typeof(Quaternion) && method.Name == "Lerp") replacement = "Aim";
                    if (method.DeclaringType == typeof(Vector3) && method.Name == "MoveTowards") replacement = "Track";
                    if (replacement != null)
                    {
                        instruction.operand = typeof(GuttermanAimRules).GetMethod(replacement, BindingFlags.NonPublic | BindingFlags.Static);
                        count++;
                    }
                }
                yield return instruction;
            }
            int expected = __originalMethod.Name == "FixedUpdate" ? 5 : __originalMethod.Name == "AttackUpdate" ? 2 : 3;
            if (count != expected) throw new InvalidOperationException("Gutterman aim IL changed: " + __originalMethod + " " + count);
        }
    }
}
