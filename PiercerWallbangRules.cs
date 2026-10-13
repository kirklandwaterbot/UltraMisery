using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UltraMisery
{
    internal sealed class UltraMiseryPiercerCharge : MonoBehaviour { }
    [HarmonyPatch(typeof(Revolver), "Shoot")]
    internal static class PiercerChargedSpawnPatch
    {
        internal static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Revolver weapon)
        {
            GameObject shot = Object.Instantiate(prefab, position, rotation);
            if (UltraMiseryState.IsActive && weapon.gunVariation == 0 && prefab == weapon.revolverBeamSuper)
                shot.AddComponent<UltraMiseryPiercerCharge>();
            return shot;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo m && m.DeclaringType == typeof(Object) && m.Name == "Instantiate" &&
                    m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(GameObject) && m.GetParameters().Length == 3 &&
                    m.GetParameters()[1].ParameterType == typeof(Vector3) && m.GetParameters()[2].ParameterType == typeof(Quaternion))
                {
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, typeof(PiercerChargedSpawnPatch).GetMethod(nameof(Spawn), BindingFlags.Static | BindingFlags.NonPublic));
                    count++;
                }
                else yield return instruction;
            }
            if (count != 3) throw new InvalidOperationException("Revolver primary/charged/audio spawn layout changed: " + count);
        }
    }
    [HarmonyPatch(typeof(RevolverBeam), "Shoot")]
    internal static class PiercerWallbangPatch
    {
        private static void Prefix(RevolverBeam __instance, ref LayerMask ___pierceLayerMask,
            ref LayerMask ___ignoreEnemyTrigger, out int[] __state)
        {
            __state = null;
            if (!UltraMiseryState.IsActive || !__instance.GetComponent<UltraMiseryPiercerCharge>()) return;
            __state = new[] { (int)___pierceLayerMask, (int)___ignoreEnemyTrigger };
            int environment = LayerMaskDefaults.Get(LMD.Environment);
            ___pierceLayerMask &= ~environment;
            ___ignoreEnemyTrigger &= ~environment;
        }
        private static void Finalizer(ref LayerMask ___pierceLayerMask, ref LayerMask ___ignoreEnemyTrigger, int[] __state)
        {
            if (__state == null) return;
            ___pierceLayerMask = __state[0];
            ___ignoreEnemyTrigger = __state[1];
        }
    }
}
