using System;
using System.Reflection;
using HarmonyLib;

namespace UltraMisery
{
    internal static class AllEnemiesEnrageBridge
    {
        private const string PluginTypeName = "AllEnemiesEnragable.AllEnemiesEnragablePlugin";

        private static bool resolved;
        private static FieldInfo forceEnrageField;
        private static MethodInfo enrageEnemyMethod;

        internal static bool TryGetForceEnrage(out bool value)
        {
            Resolve();
            if (forceEnrageField == null)
            {
                value = false;
                return false;
            }

            value = (bool)forceEnrageField.GetValue(null);
            return true;
        }

        internal static bool TrySetForceEnrage(bool value)
        {
            Resolve();
            if (forceEnrageField == null)
            {
                return false;
            }

            forceEnrageField.SetValue(null, value);
            return true;
        }

        internal static bool TryEnrageEnemy(EnemyIdentifier enemy)
        {
            Resolve();
            if (enrageEnemyMethod == null || !enemy || enemy.dead)
            {
                return false;
            }

            try
            {
                enrageEnemyMethod.Invoke(null, new object[] { enemy });
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"All-enemy enrage failed for {enemy.enemyType}: {exception.GetBaseException().Message}");
                return false;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Type pluginType = AccessTools.TypeByName(PluginTypeName);
            if (pluginType == null)
            {
                Plugin.Log.LogWarning("AllEnemiesEnragable was not found; only enemies with native enrage support can be enraged.");
                return;
            }

            forceEnrageField = AccessTools.Field(pluginType, "ForceEnrage");
            enrageEnemyMethod = AccessTools.Method(pluginType, "EnrageEnemy", new[] { typeof(EnemyIdentifier) });

            if (forceEnrageField == null || enrageEnemyMethod == null)
            {
                Plugin.Log.LogWarning("AllEnemiesEnragable was found, but its force-enrage API was not compatible.");
                return;
            }

            Plugin.Log.LogInfo("Connected to AllEnemiesEnragable force-enrage API.");
        }
    }
}
