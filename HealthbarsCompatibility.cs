using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class HealthbarsCompatibility
    {
        private const string HealthbarsEnemyPatchTypeName = "Healthbars.Patches.EnemyPatch";
        private const string HealthbarTypeName = "Healthbar";

        private static readonly HashSet<int> LoggedSkippedEnemies = new HashSet<int>();

        private static MethodInfo healthbarsEnemySpawned;
        private static MethodInfo enemyIdentifierStart;
        private static FieldInfo healthbarEnemyField;
        private static bool proxyInstalled;
        private static bool lifecycleGuardsInstalled;
        private static string originalPatchOwner;
        private static int originalPatchPriority = Priority.Normal;
        private static string[] originalPatchBefore = Array.Empty<string>();
        private static string[] originalPatchAfter = Array.Empty<string>();

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                return;
            }

            InstallLifecycleGuards(harmony);
            InstallSpawnProxy(harmony);
        }

        internal static IEnumerator InstallAfterPluginStartup(Harmony harmony)
        {
            // BepInEx normally loads Healthbars before UltraMisery, but waiting
            // one frame also covers profiles whose dependency order is reversed.
            yield return null;
            Install(harmony);
        }

        internal static void Reset()
        {
            LoggedSkippedEnemies.Clear();
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (!proxyInstalled || harmony == null || enemyIdentifierStart == null ||
                healthbarsEnemySpawned == null)
            {
                return;
            }

            MethodInfo proxy = AccessTools.Method(typeof(HealthbarsCompatibility),
                nameof(SafeHealthbarsEnemySpawned));
            if (proxy != null)
            {
                harmony.Unpatch(enemyIdentifierStart, proxy);
            }

            HarmonyMethod restored = new HarmonyMethod(healthbarsEnemySpawned)
            {
                priority = originalPatchPriority,
                before = originalPatchBefore,
                after = originalPatchAfter
            };
            new Harmony(string.IsNullOrEmpty(originalPatchOwner)
                ? "Healthbars"
                : originalPatchOwner).Patch(enemyIdentifierStart, postfix: restored);
            proxyInstalled = false;
        }

        private static void InstallSpawnProxy(Harmony harmony)
        {
            if (proxyInstalled)
            {
                return;
            }

            Type enemyPatchType = AccessTools.TypeByName(HealthbarsEnemyPatchTypeName);
            enemyIdentifierStart = AccessTools.Method(typeof(EnemyIdentifier), "Start");
            healthbarsEnemySpawned = enemyPatchType == null
                ? null
                : AccessTools.Method(enemyPatchType, "EnemySpawned");
            if (enemyIdentifierStart == null || healthbarsEnemySpawned == null)
            {
                return;
            }

            Patches patchInfo = Harmony.GetPatchInfo(enemyIdentifierStart);
            Patch registeredPatch = null;
            if (patchInfo != null)
            {
                foreach (Patch postfix in patchInfo.Postfixes)
                {
                    if (postfix.PatchMethod == healthbarsEnemySpawned)
                    {
                        registeredPatch = postfix;
                        break;
                    }
                }
            }

            if (registeredPatch == null)
            {
                return;
            }

            originalPatchOwner = registeredPatch.owner;
            originalPatchPriority = registeredPatch.priority;
            originalPatchBefore = registeredPatch.before ?? Array.Empty<string>();
            originalPatchAfter = registeredPatch.after ?? Array.Empty<string>();

            harmony.Unpatch(enemyIdentifierStart, healthbarsEnemySpawned);
            HarmonyMethod guardedProxy = new HarmonyMethod(
                AccessTools.Method(typeof(HealthbarsCompatibility), nameof(SafeHealthbarsEnemySpawned)))
            {
                priority = originalPatchPriority,
                before = originalPatchBefore,
                after = originalPatchAfter
            };
            harmony.Patch(enemyIdentifierStart, postfix: guardedProxy);
            proxyInstalled = true;
            Plugin.Log.LogInfo("Healthbars compatibility enabled: unsafe support and incomplete enemies will not receive health bars.");
        }

        private static void InstallLifecycleGuards(Harmony harmony)
        {
            if (lifecycleGuardsInstalled)
            {
                return;
            }

            Type healthbarType = AccessTools.TypeByName(HealthbarTypeName);
            if (healthbarType == null)
            {
                return;
            }

            MethodInfo start = AccessTools.Method(healthbarType, "Start");
            MethodInfo update = AccessTools.Method(healthbarType, "Update");
            healthbarEnemyField = AccessTools.Field(healthbarType, "enemy");
            MethodInfo guard = AccessTools.Method(typeof(HealthbarsCompatibility),
                nameof(HealthbarLifecyclePrefix));
            if (healthbarEnemyField == null || guard == null || start == null || update == null)
            {
                Plugin.Log.LogWarning("Healthbars was found, but its lifecycle API was not compatible with UltraMisery's safety guard.");
                return;
            }

            HarmonyMethod prefix = new HarmonyMethod(guard)
            {
                priority = Priority.First
            };
            harmony.Patch(start, prefix: prefix);
            harmony.Patch(update, prefix: prefix);
            lifecycleGuardsInstalled = true;
        }

        private static void SafeHealthbarsEnemySpawned(EnemyIdentifier __instance)
        {
            if (!__instance || healthbarsEnemySpawned == null)
            {
                return;
            }

            if (UltraMiseryState.IsActive && !CanReceiveHealthbar(__instance))
            {
                LogSkippedEnemy(__instance);
                return;
            }

            try
            {
                healthbarsEnemySpawned.Invoke(null, new object[] { __instance });
            }
            catch (TargetInvocationException exception)
            {
                Exception cause = exception.InnerException ?? exception;
                Plugin.Log.LogWarning($"Healthbars rejected {__instance.enemyType}; skipped its bar: {cause.GetType().Name}: {cause.Message}");
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Healthbars rejected {__instance.enemyType}; skipped its bar: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static bool HealthbarLifecyclePrefix(object __instance)
        {
            if (!UltraMiseryState.IsActive || __instance == null || healthbarEnemyField == null)
            {
                return true;
            }

            EnemyIdentifier enemy = healthbarEnemyField.GetValue(__instance) as EnemyIdentifier;
            if (CanReceiveHealthbar(enemy))
            {
                return true;
            }

            if (enemy)
            {
                LogSkippedEnemy(enemy);
            }

            Component component = __instance as Component;
            if (component && component.gameObject)
            {
                UnityEngine.Object.Destroy(component.gameObject);
            }

            return false;
        }

        private static bool CanReceiveHealthbar(EnemyIdentifier enemy)
        {
            if (!enemy || enemy.dead || enemy.puppet || !enemy.gameObject ||
                !enemy.gameObject.scene.IsValid() || !enemy.gameObject.scene.isLoaded)
            {
                return false;
            }

            if (enemy.enemyType == EnemyType.Idol || enemy.enemyType == EnemyType.Deathcatcher)
            {
                return false;
            }

            // Healthbars 1.4.0 dereferences both of these root components in
            // Start/Update without null checks. A converted decoration is not
            // considered initialized until it has the same contract.
            return enemy.GetComponent<Enemy>() && enemy.GetComponent<Collider>();
        }

        private static void LogSkippedEnemy(EnemyIdentifier enemy)
        {
            int id = enemy.GetInstanceID();
            if (LoggedSkippedEnemies.Add(id))
            {
                Plugin.Log.LogDebug($"Skipped unsafe Healthbars attachment for {enemy.enemyType} '{enemy.name}'.");
            }
        }
    }
}
