using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    [HarmonyPatch(typeof(EnemyIdentifier), "ProcessDeath")]
    internal static class RemoveGuttermanCorpsePatch
    {
        private static void Postfix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance.dead || __instance.enemyType != EnemyType.Gutterman) return;
            // Destroy is deferred until after the complete damage/death call stack.
            // Native ProcessDeath has already dispatched events and counted the kill.
            foreach (Collider collider in __instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Renderer renderer in __instance.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            Object.Destroy(__instance.gameObject);
        }
    }

    [HarmonyPatch(typeof(Gutterman), "Start")]
    internal static class RemovePreDeadGuttermanPatch
    {
        private static void Postfix(Gutterman __instance)
        {
            if (UltraMiseryState.IsActive && __instance.dead) Object.Destroy(__instance.gameObject);
        }
    }

    // Preserve native death bookkeeping; remove only the corpse's presentation
    // and collision, including colliders enabled later by animation events.
    internal sealed class UltraMiseryHiddenGuttermanCorpse : MonoBehaviour
    {
        private Renderer[] renderers;
        private Collider[] colliders;
        private bool[] rendererStates;
        private bool[] colliderStates;
        private EnemyIdentifier enemy;
        private void Awake()
        {
            enemy = GetComponent<EnemyIdentifier>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            rendererStates = System.Array.ConvertAll(renderers, item => item.enabled);
            colliderStates = System.Array.ConvertAll(colliders, item => item.enabled);
        }
        private void LateUpdate()
        {
            if (enemy && !enemy.dead)
            {
                for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].enabled = rendererStates[i];
                for (int i = 0; i < colliders.Length; i++) if (colliders[i]) colliders[i].enabled = colliderStates[i];
                Destroy(this);
                return;
            }
            foreach (Renderer item in renderers) if (item) item.enabled = false;
            foreach (Collider item in colliders) if (item) item.enabled = false;
        }
        internal void HideNow()
        {
            foreach (Renderer item in renderers) if (item) item.enabled = false;
            foreach (Collider item in colliders) if (item) item.enabled = false;
        }
    }

    [HarmonyPatch(typeof(Gutterman), "Death")]
    internal static class HideGuttermanCorpsePatch
    {
        private static void Prefix(Gutterman __instance)
        {
            if (UltraMiseryState.IsActive && !__instance.GetComponent<UltraMiseryHiddenGuttermanCorpse>())
                __instance.gameObject.AddComponent<UltraMiseryHiddenGuttermanCorpse>();
        }
        private static void Postfix(Gutterman __instance)
        {
            if (!UltraMiseryState.IsActive) return;
            var cleanup = __instance.GetComponent<UltraMiseryHiddenGuttermanCorpse>() ??
                __instance.gameObject.AddComponent<UltraMiseryHiddenGuttermanCorpse>();
            cleanup.HideNow();
        }
    }

    [HarmonyPatch]
    internal static class GroundWaveMissingTargetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "Update", "FixedUpdate", "TrackTick" })
                yield return AccessTools.Method(typeof(GroundWave), name);
        }
        private static bool Prefix(GroundWave __instance)
        {
            if (!UltraMiseryState.IsActive || (__instance.target != null && __instance.target.trackedTransform)) return true;
            // Do not repeatedly skip Update while leaving an immortal damage object.
            __instance.CancelInvoke();
            __instance.gameObject.SetActive(false);
            Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
