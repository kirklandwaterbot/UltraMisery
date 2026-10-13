using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class SceneRules
    {
        internal static IEnumerator ApplyAfterLoad(Scene scene)
        {
            // Let the level finish constructing its pickups and StatsManager first.
            yield return null;

            if (!UltraMiseryState.IsActive || !scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            RemoveForbiddenPickups(scene, true);

            // Secret exits are guarded at unlock AND transition entrypoints by
            // SecretExitRules, including newly cloned checkpoint rooms.

            GameObject sanitizerObject = new GameObject("UltraMisery Scene Sanitizer");
            SceneManager.MoveGameObjectToScene(sanitizerObject, scene);
            sanitizerObject.AddComponent<UltraMiserySceneSanitizer>();

            yield return MarkedForDeathRules.ActivateAfterLoad(scene);
        }

        internal static int RemoveForbiddenPickups(Scene scene, bool includeInactive)
        {
            if (!UltraMiseryState.IsActive || !scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            bool sandboxScene = IsSandboxScene(scene);
            HashSet<GameObject> targets = new HashSet<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Bonus bonus in root.GetComponentsInChildren<Bonus>(includeInactive))
                {
                    // Bonus is the stock component used by blue secret soul orbs
                    // and red super-charge/blood orbs.
                    targets.Add(bonus.gameObject);
                }

                foreach (Soap soap in root.GetComponentsInChildren<Soap>(includeInactive))
                {
                    if (!sandboxScene)
                    {
                        targets.Add(soap.gameObject);
                    }
                }

                // 8-1's pickup is identified through the shared item system and
                // is not reliably present as a started Soap component at load time.
                foreach (ItemIdentifier item in root.GetComponentsInChildren<ItemIdentifier>(includeInactive))
                {
                    if (!sandboxScene && (item.itemType == ItemType.Soap ||
                        item.gameObject.name.IndexOf("soap", System.StringComparison.OrdinalIgnoreCase) >= 0)
                       )
                    {
                        targets.Add(item.gameObject);
                    }
                }

                foreach (DualWieldPickup pickup in root.GetComponentsInChildren<DualWieldPickup>(includeInactive))
                {
                    targets.Add(pickup.gameObject);
                }
            }

            int removed = 0;
            foreach (GameObject target in targets)
            {
                if (!target)
                {
                    continue;
                }

                // Sandbox soap is a player-created Spawner Arm object (including
                // objects restored from a Sandbox save), not a level pickup.
                if (sandboxScene && IsSoapObject(target))
                {
                    continue;
                }

                if (IsSoapObject(target))
                {
                    Plugin.Log.LogInfo($"Removed Soap pickup '{target.name}' from {scene.name}.");
                }

                Object.Destroy(target);
                removed++;
            }

            return removed;
        }

        internal static bool IsSandboxScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return false;
            }

            SandboxSaver saver = Object.FindObjectOfType<SandboxSaver>();
            return saver && saver.gameObject.scene == scene;
        }

        private static bool IsSoapObject(GameObject target)
        {
            return target && (target.GetComponent<Soap>() ||
                (target.TryGetComponent(out ItemIdentifier item) && item.itemType == ItemType.Soap) ||
                target.name.IndexOf("soap", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    internal sealed class UltraMiserySceneSanitizer : MonoBehaviour
    {
        private IEnumerator Start()
        {
            WaitForSecondsRealtime delay = new WaitForSecondsRealtime(0.5f);
            while (UltraMiseryState.IsActive && gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                SceneRules.RemoveForbiddenPickups(gameObject.scene, true);
                yield return delay;
            }
        }
    }

    [HarmonyPatch(typeof(Bonus), "Start")]
    internal static class SoulAndBloodOrbStartPatch
    {
        private static bool Prefix(Bonus __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    [HarmonyPatch(typeof(Soap), "Start")]
    internal static class SoapStartPatch
    {
        private static bool Prefix(Soap __instance)
        {
            if (!UltraMiseryState.IsActive ||
                SceneRules.IsSandboxScene(__instance.gameObject.scene))
            {
                return true;
            }

            Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemIdentifier), "CreateCopy")]
    internal static class SoapCopyPatch
    {
        private static void Postfix(ref ItemIdentifier __result)
        {
            if (!UltraMiseryState.IsActive || !__result ||
                SceneRules.IsSandboxScene(__result.gameObject.scene) ||
                (__result.itemType != ItemType.Soap && !__result.GetComponent<Soap>()))
            {
                return;
            }

            Object.Destroy(__result.gameObject);
            __result = null;
        }
    }

    [HarmonyPatch(typeof(DualWieldPickup), "OnTriggerEnter")]
    internal static class DualWieldPickupPatch
    {
        private static bool Prefix()
        {
            return !UltraMiseryState.IsActive;
        }
    }
}
