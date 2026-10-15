using System.Collections;
using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("com.eternalUnion.angryLevelLoader", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "kirk.ultrakill.ultramisery";
        public const string PluginName = "UltraMisery";
        // BepInEx 5 parses this value as System.Version. Pre-release suffixes
        // such as "-dev" cause the entire plugin type to be skipped.
        public const string PluginVersion = "0.2.8";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private Harmony harmony;
        private bool forcingGlobalBuffs;
        private bool previousForceSand;
        private bool previousForceRadiance;
        private bool previousForceEnrage;
        private bool capturedForceEnrage;
        private float nextEnemySweep;
        private float nextDifficultyUiSweep;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony = new Harmony(PluginGuid);
            LevelOverrides.Integration.Install();
            PatchAllSafely();
            AngryCompatibility.Install(harmony);
            HealthbarsCompatibility.Install(harmony);
            StartCoroutine(HealthbarsCompatibility.InstallAfterPluginStartup(harmony));
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logger.LogInfo($"UltraMisery {PluginVersion} loaded. Rules are active while UltraMisery is selected.");
            Logger.LogInfo("Shotgun primary balance: shared 1-second gate inside primary fire; no equip delay.");
            Logger.LogInfo("Shotgun runtime trace build 028-trace1: observing actual pellet spawns and both clocks.");
        }

        private void Update()
        {
            MarkedForDeathRules.Tick();
            if (Time.unscaledTime >= nextDifficultyUiSweep)
            {
                nextDifficultyUiSweep = Time.unscaledTime + 0.5f;
                DifficultySelectionUi.ReconcileVisibleButton();
            }

            bool active = UltraMiseryState.IsActive;
            if (active)
            {
                if (!forcingGlobalBuffs)
                {
                    previousForceSand = OptionsManager.forceSand;
                    previousForceRadiance = OptionsManager.forceRadiance;
                    capturedForceEnrage = AllEnemiesEnrageBridge.TryGetForceEnrage(out previousForceEnrage);
                    forcingGlobalBuffs = true;
                    Logger.LogInfo("Forcing Sanded, Radiant, and universal Enrage internally without enabling cheats.");
                }

                OptionsManager.forceSand = true;
                OptionsManager.forceRadiance = true;
                // AllEnemiesEnragable 1.0.0 currently throws against the
                // revamped EnemyIdentifier API. UltraMisery applies and
                // maintains universal enrage itself in EnemyRules instead.

                // Catch enemies activated from delayed subwaves or spawned by
                // another mod without passing through the usual Start hook.
                if (Time.unscaledTime >= nextEnemySweep)
                {
                    nextEnemySweep = Time.unscaledTime + 0.5f;
                    StatueEncounterRules.ActivateNearPlayer();
                    foreach (EnemyIdentifier enemy in UnityEngine.Object.FindObjectsOfType<EnemyIdentifier>())
                    {
                        EnemyRules.EnsureApplied(enemy);
                    }
                }
            }
            else if (forcingGlobalBuffs)
            {
                RestoreGlobalBuffFlags();
                nextEnemySweep = 0f;
            }
        }

        private void PatchAllSafely()
        {
            int patched = 0;
            int failed = 0;

            // Patch classes independently. A game update can rename one target;
            // that should not prevent the core enemy-buff patch from loading.
            foreach (Type type in GetType().Assembly.GetTypes())
            {
                if (!Attribute.IsDefined(type, typeof(HarmonyPatch), false))
                {
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    patched++;
                }
                catch (Exception exception)
                {
                    failed++;
                    Logger.LogError($"Patch class {type.FullName} failed: {exception}");
                }
            }

            Logger.LogInfo($"UltraMisery patch registration finished: {patched} succeeded, {failed} failed.");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            LevelOverrides.Integration.Shutdown();
            RestoreGlobalBuffFlags();
            HealthbarsCompatibility.Uninstall(harmony);
            harmony?.UnpatchSelf();
        }

        private void RestoreGlobalBuffFlags()
        {
            if (!forcingGlobalBuffs)
            {
                return;
            }

            OptionsManager.forceSand = previousForceSand;
            OptionsManager.forceRadiance = previousForceRadiance;
            if (capturedForceEnrage)
            {
                AllEnemiesEnrageBridge.TrySetForceEnrage(previousForceEnrage);
            }

            capturedForceEnrage = false;
            forcingGlobalBuffs = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LevelOverrides.Integration.BeforeSceneStart();
            // A new mission resets recovery; additive/temporary active-scene changes do not.
            if (mode == LoadSceneMode.Single) ShotgunBalance.Shared.Reset();
            ShotgunRuntimeTrace.Reset("scene " + scene.name + ", " + mode);
            bool rejectedSecretScene = MissionRestartRules.OnSceneLoaded(scene, mode);
            MarkedForDeathRules.Reset();
            BoundaryRules.Reset();
            UltraMiseryEnrageAudioGate.Reset();
            StatueEncounterRules.Reset();
            HealthbarsCompatibility.Reset();
            DifficultySelectionUi.ResetReconciliation();
            nextDifficultyUiSweep = 0f;

            if (UltraMiseryState.IsActive && !rejectedSecretScene)
            {
                // Before Start: CheckPoint.Start clones its room templates.
                RequiredSecretBosses.CaptureScene();
                UltraMiseryEnrageAudioGate.BindScene(scene);
                StatueEncounterRules.CacheScene(scene);
                StartCoroutine(SceneRules.ApplyAfterLoad(scene));
            }
        }

        internal void QueueEnemyRules(EnemyIdentifier enemy)
        {
            StartCoroutine(EnemyRules.ApplyAfterEnemyStart(enemy));
        }
    }

    internal static class UltraMiseryState
    {
        internal static bool IsActive { get; private set; }

        internal static void SetSelected(bool selected)
        {
            if (IsActive == selected)
            {
                return;
            }

            IsActive = selected;
            ShotgunBalance.Shared.Reset();
            ShotgunRuntimeTrace.Reset("mode " + selected);
            UltraMiseryEnrageAudioGate.Reset();
            StatueEncounterRules.Reset();
            HealthbarsCompatibility.Reset();
            if (selected)
            {
                StatueEncounterRules.CacheScene(SceneManager.GetActiveScene());
                if (MissionRestartRules.ShouldRejectLoad(true, MissionRestartRules.CheatsEnabled, SceneHelper.CurrentScene))
                    MissionRestartRules.Request("UltraMisery selected inside a secret mission");
            }
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo(selected
                    ? "ULTRAMISERY selected; UKMD-based rules enabled."
                    : "A different difficulty was selected; UltraMisery rules disabled.");
            }
        }
    }
}
