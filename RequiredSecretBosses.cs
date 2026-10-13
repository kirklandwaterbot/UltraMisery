using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    // Serialized so native CheckPoint room clones retain their authored identity.
    // Enemy prefabs spawned by the Spawner Arm do not carry this component.
    internal sealed class UltraMiserySecretIdentity : MonoBehaviour
    {
        public int Identity;
    }

    internal static class RequiredSecretBosses
    {
        private sealed class Record
        {
            internal EnemyType Type;
            internal bool Defeated;
        }
        private static int sceneHandle = -1;
        private static bool captured;
        private static readonly Dictionary<int, Record> Records = new Dictionary<int, Record>();

        internal static int RequiredCount(string level, EnemyType type)
        {
            if (level == "Level 1-2" && (type == EnemyType.CancerousRodent || type == EnemyType.VeryCancerousRodent)) return 1;
            if (level == "Level 1-3" && type == EnemyType.Swordsmachine) return 2;
            if (level == "Level 4-3" && type == EnemyType.Mandalore) return 1;
            if (level == "Level 7-1" && type == EnemyType.BigJohnator) return 1;
            if (level == "Level 8-3" && type == EnemyType.Ferryman) return 2;
            return 0;
        }
        private static bool Relevant(EnemyIdentifier enemy)
        {
            if (!enemy) return false;
            return RequiredCount(SceneHelper.CurrentScene, enemy.enemyType) > 0;
        }
        private static void EnsureScene()
        {
            int current = SceneManager.GetActiveScene().handle;
            if (current == sceneHandle) return;
            sceneHandle = current;
            captured = false;
            Records.Clear();
        }
        internal static void Register(EnemyIdentifier enemy)
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || !Relevant(enemy)) return;
            EnsureScene();
            if (enemy.gameObject.scene.handle != sceneHandle) return;
            UltraMiserySecretIdentity identity = enemy.GetComponent<UltraMiserySecretIdentity>();
            if (!identity || !Records.TryGetValue(identity.Identity, out Record record)) return;
            // A newly started checkpoint clone replaces the previous attempt.
            record.Defeated = enemy.dead;
        }
        internal static void CaptureScene()
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom) return;
            EnsureScene();
            if (captured) return;
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            captured = true;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (EnemyIdentifier enemy in root.GetComponentsInChildren<EnemyIdentifier>(true))
                {
                    if (!Relevant(enemy)) continue;
                    var identity = enemy.GetComponent<UltraMiserySecretIdentity>();
                    if (!identity) identity = enemy.gameObject.AddComponent<UltraMiserySecretIdentity>();
                    identity.Identity = enemy.GetInstanceID();
                    Records[identity.Identity] = new Record { Type = enemy.enemyType, Defeated = enemy.dead };
                }
        }
        internal static void RecordDeath(EnemyIdentifier enemy)
        {
            var identity = enemy ? enemy.GetComponent<UltraMiserySecretIdentity>() : null;
            if (identity && enemy.gameObject.scene.handle == sceneHandle && Records.TryGetValue(identity.Identity, out Record record))
                record.Defeated = enemy.dead;
        }
        internal static bool AllDefeated()
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom) return true;
            EnsureScene();
            if (!captured) return false;
            var counts = new Dictionary<EnemyType, int>();
            foreach (Record record in Records.Values)
            {
                if (!record.Defeated) return false;
                counts.TryGetValue(record.Type, out int count);
                counts[record.Type] = count + 1;
            }
            foreach (EnemyType type in new[] { EnemyType.CancerousRodent, EnemyType.VeryCancerousRodent,
                EnemyType.Swordsmachine, EnemyType.Mandalore, EnemyType.BigJohnator, EnemyType.Ferryman })
            {
                counts.TryGetValue(type, out int count);
                if (count < RequiredCount(SceneHelper.CurrentScene, type)) return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(EnemyIdentifier), "Start")]
        private static class RegisterSpawnPatch
        {
            private static void Postfix(EnemyIdentifier __instance) => Register(__instance);
        }
        [HarmonyPatch(typeof(EnemyIdentifier), "ProcessDeath")]
        private static class RecordDeathPatch
        {
            private static void Prefix(EnemyIdentifier __instance)
            {
                if (UltraMiseryState.IsActive && !SceneHelper.IsPlayingCustom && __instance.GetComponent<UltraMiserySecretIdentity>()) __instance.dontCountAsKills = false;
            }
            private static void Postfix(EnemyIdentifier __instance) => RecordDeath(__instance);
        }
        [HarmonyPatch(typeof(StatsManager), "GetRanks")]
        private static class RequireSecretKillsGradePatch
        {
            private static void Postfix(StatsManager __instance, int[] ranksToCheck, bool addToRankScore, ref string __result)
            {
                if (!UltraMiseryState.IsActive || ranksToCheck != __instance.killRanks || __result != "<color=#FF0000>S</color>" || AllDefeated()) return;
                __result = "<color=#FF6A00>A</color>";
                if (addToRankScore) __instance.rankScore--;
            }
        }
    }
}
