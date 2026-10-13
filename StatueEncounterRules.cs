using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class StatueEncounterRules
    {
        private const float EncounterRadius = 32f;

        private static readonly HashSet<int> ConvertedDecorations = new HashSet<int>();
        private static float nextActualMannequinActivation;
        internal const float MannequinActivationInterval = 1f / 1.5f;
        private sealed class PendingMannequin
        {
            internal MonoBehaviour Decoration;
            internal Transform Wave;
            internal int Id;
            internal int WaveId;
        }
        private static readonly List<PendingMannequin> PendingMannequins = new List<PendingMannequin>();
        private static bool mannequinQueueRunning;
        private static int mannequinQueueGeneration;
        private static readonly HashSet<int> AwakeningDecorations = new HashSet<int>();
        private static readonly Dictionary<int, int> AwakeningWaves = new Dictionary<int, int>();

        internal static bool HasPendingAwakening(ActivateNextWave wave)
        {
            return wave && AwakeningWaves.ContainsKey(wave.transform.GetInstanceID());
        }
        private static readonly HashSet<int> ClaimedEnemies = new HashSet<int>();
        private static readonly HashSet<int> ProcessingWaves = new HashSet<int>();
        private static readonly List<StatueFake> CachedCerberusDecorations = new List<StatueFake>();
        private static readonly List<UltraMiseryMeshStatue> CachedMeshDecorations = new List<UltraMiseryMeshStatue>();
        internal static void Register(UltraMiseryMeshStatue decoration)
        {
            if (IsCacheable(decoration) && !CachedMeshDecorations.Contains(decoration)) CachedMeshDecorations.Add(decoration);
        }
        private static readonly List<MannequinPoses> CachedMannequinDecorations = new List<MannequinPoses>();
        private static int cachedSceneHandle = -1;

        internal static void Reset()
        {
            nextActualMannequinActivation = 0f;
            PendingMannequins.Clear();
            mannequinQueueRunning = false;
            mannequinQueueGeneration++;
            ConvertedDecorations.Clear();
            AwakeningDecorations.Clear();
            AwakeningWaves.Clear();
            ClaimedEnemies.Clear();
            ProcessingWaves.Clear();
            CachedCerberusDecorations.Clear();
            CachedMeshDecorations.Clear();
            CachedMannequinDecorations.Clear();
            cachedSceneHandle = -1;
        }

        internal static void CacheScene(Scene scene)
        {
            if (!UltraMiseryState.IsActive || !scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            cachedSceneHandle = scene.handle;
            MeshStatueDiscovery.Capture(scene);

            foreach (StatueFake decoration in Resources.FindObjectsOfTypeAll<StatueFake>())
            {
                Register(decoration);
            }

            foreach (MannequinPoses decoration in Resources.FindObjectsOfTypeAll<MannequinPoses>())
            {
                Register(decoration);
            }

            Plugin.Log.LogDebug($"Cached {CachedCerberusDecorations.Count} decorative Cerberus and " +
                $"{CachedMannequinDecorations.Count} decorative Mannequins for {scene.name}.");
        }

        internal static void Register(StatueFake decoration)
        {
            if (IsCacheable(decoration) && !CachedCerberusDecorations.Contains(decoration))
            {
                CachedCerberusDecorations.Add(decoration);
            }
        }

        internal static void Register(MannequinPoses decoration)
        {
            if (decoration && decoration.GetComponentInParent<EnemyIdentifier>(true)) return;
            if (IsCacheable(decoration) && !CachedMannequinDecorations.Contains(decoration))
            {
                CachedMannequinDecorations.Add(decoration);
            }
        }

        internal static void ActivateForArena(ActivateArena arena)
        {
            if (!UltraMiseryState.IsActive || !arena || arena.forEnemy ||
                arena.enemies == null || arena.enemies.Length == 0)
            {
                return;
            }

            Transform wave = FindWave(arena.enemies);
            if (!wave)
            {
                return;
            }

            List<Vector3> anchors = BuildAnchors(wave, arena.enemies);
            anchors.Add(arena.transform.position);
            ActivateDecorations(wave, anchors);
        }

        internal static void ActivateForNextWave(ActivateNextWave controller, int currentEnemy)
        {
            if (!UltraMiseryState.IsActive || !controller || controller.nextEnemies == null ||
                currentEnemy < 0 || currentEnemy >= controller.nextEnemies.Length)
            {
                return;
            }

            GameObject target = controller.nextEnemies[currentEnemy];
            if (!target)
            {
                return;
            }

            ActivateNextWave targetWave = target.GetComponent<ActivateNextWave>();
            Transform wave = targetWave ? target.transform : target.transform.parent;
            if (!wave)
            {
                return;
            }

            ActivateDecorations(wave, BuildAnchors(wave, controller.nextEnemies));
        }

        internal static void ActivateForStartedEnemy(EnemyIdentifier enemy)
        {
            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || !enemy.gameObject.activeInHierarchy ||
                (CachedCerberusDecorations.Count == 0 && CachedMannequinDecorations.Count == 0))
            {
                return;
            }

            ActivateNextWave wave = enemy.GetComponentInParent<ActivateNextWave>();
            if (!wave)
            {
                return;
            }

            List<Vector3> anchors = BuildAnchors(wave.transform, null);
            anchors.Add(enemy.transform.position);
            ActivateDecorations(wave.transform, anchors);
        }

        private static Transform FindWave(GameObject[] enemies)
        {
            foreach (GameObject enemy in enemies)
            {
                if (!enemy)
                {
                    continue;
                }

                ActivateNextWave ownWave = enemy.GetComponent<ActivateNextWave>();
                if (ownWave)
                {
                    return ownWave.transform;
                }

                if (enemy.transform.parent)
                {
                    return enemy.transform.parent;
                }
            }

            return null;
        }

        private static List<Vector3> BuildAnchors(Transform wave, GameObject[] authoredEnemies)
        {
            List<Vector3> anchors = new List<Vector3>();
            if (authoredEnemies != null)
            {
                foreach (GameObject enemy in authoredEnemies)
                {
                    if (enemy)
                    {
                        anchors.Add(enemy.transform.position);
                    }
                }
            }

            if (wave)
            {
                foreach (EnemyIdentifier enemy in wave.GetComponentsInChildren<EnemyIdentifier>(true))
                {
                    if (enemy)
                    {
                        anchors.Add(enemy.transform.position);
                    }
                }

                if (anchors.Count == 0)
                {
                    anchors.Add(wave.position);
                }
            }

            return anchors;
        }

        private static void ActivateDecorations(Transform wave, List<Vector3> anchors, bool includeHierarchy = true)
        {
            if (!wave || anchors == null || anchors.Count == 0)
            {
                return;
            }

            int waveId = wave.GetInstanceID();
            if (!ProcessingWaves.Add(waveId))
            {
                return;
            }

            List<StatueFake> cerberusDecorations = new List<StatueFake>();
            for (int index = CachedCerberusDecorations.Count - 1; index >= 0; index--)
            {
                StatueFake decoration = CachedCerberusDecorations[index];
                if (!decoration)
                {
                    CachedCerberusDecorations.RemoveAt(index);
                    continue;
                }

                if (IsEncounterDecoration(decoration, wave, anchors, includeHierarchy))
                {
                    cerberusDecorations.Add(decoration);
                }
            }

            List<MannequinPoses> mannequinDecorations = new List<MannequinPoses>();
            for (int index = CachedMannequinDecorations.Count - 1; index >= 0; index--)
            {
                MannequinPoses decoration = CachedMannequinDecorations[index];
                if (!decoration)
                {
                    CachedMannequinDecorations.RemoveAt(index);
                    continue;
                }

                if (IsEncounterDecoration(decoration, wave, anchors, includeHierarchy))
                {
                    mannequinDecorations.Add(decoration);
                }
            }

            try
            {
                int activated = 0;
                foreach (StatueFake decoration in cerberusDecorations)
                {
                    if (ConvertDecoration(decoration, EnemyType.Cerberus, wave))
                    {
                        activated++;
                    }
                }

                foreach (MannequinPoses decoration in mannequinDecorations)
                {
                    if (ConvertDecoration(decoration, EnemyType.Mannequin, wave))
                    {
                        activated++;
                    }
                }

                if (activated == 0)
                {
                    return;
                }

                ActivateNextWave waveController = wave ? wave.GetComponent<ActivateNextWave>() : null;
                if (waveController)
                {
                    waveController.CountEnemies();
                }

                Plugin.Log.LogInfo($"Activated {activated} decorative Cerberus/Mannequin statues in encounter {(wave ? wave.name : "standalone statue")}.");
            }
            finally
            {
                ProcessingWaves.Remove(waveId);
            }
        }

        private static bool IsCacheable(MonoBehaviour decoration)
        {
            if (!UltraMiseryState.IsActive || !decoration || !decoration.gameObject.scene.IsValid() ||
                !decoration.gameObject.scene.isLoaded)
            {
                return false;
            }

            if (cachedSceneHandle < 0)
            {
                cachedSceneHandle = decoration.gameObject.scene.handle;
            }

            return true;
        }

        internal static void ActivateNearPlayer()
        {
            if (!UltraMiseryState.IsActive || !MonoSingleton<NewMovement>.Instance) return;
            ActivateAllPresentStatues();
            Vector3 playerPosition = MonoSingleton<NewMovement>.Instance.transform.position;
            // Only visit cached components. Inactive statues remain cached until
            // their room is enabled; empty pedestals still fail the mesh check.
            List<MonoBehaviour> candidates = new List<MonoBehaviour>();
            candidates.AddRange(CachedMannequinDecorations);
            foreach (MonoBehaviour decoration in candidates)
            {
                if (!decoration || !IsPresentDecoration(decoration) ||
                    ConvertedDecorations.Contains(decoration.GetInstanceID()) ||
                    Vector3.SqrMagnitude(decoration.transform.position - playerPosition) > EncounterRadius * EncounterRadius)
                    continue;
                Vector3 center = decoration.transform.position + Vector3.up;
                if (Physics.Linecast(playerPosition + Vector3.up, center, LayerMaskDefaults.Get(LMD.Environment),
                    QueryTriggerInteraction.Ignore)) continue;
                Transform container = decoration.transform.parent;
                if (!container) container = decoration.transform;
                ActivateDecorations(container, new List<Vector3> { decoration.transform.position }, false);
            }
        }

        internal static void ActivateAllPresentStatues()
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || MonoSingleton<SandboxNavmesh>.Instance) return;
            // No radius, sight ray, or encounter-membership condition. Only live
            // scene instances are eligible; checkpoint templates remain inactive.
            var candidates = new List<MonoBehaviour>();
            candidates.AddRange(CachedCerberusDecorations);
            candidates.AddRange(CachedMannequinDecorations);
            candidates.AddRange(CachedMeshDecorations);
            foreach (MonoBehaviour decoration in candidates)
            {
                if (!decoration || decoration.gameObject.scene != SceneManager.GetActiveScene() ||
                    !IsPresentDecoration(decoration)) continue;
                ActivateNextWave controller = decoration.GetComponentInParent<ActivateNextWave>();
                Transform parent = controller ? controller.transform : decoration.transform.parent;
                EnemyType type = decoration is UltraMiseryMeshStatue mesh ? mesh.Type :
                    decoration is MannequinPoses ? EnemyType.Mannequin : EnemyType.Cerberus;
                ConvertDecoration(decoration, type, parent);
            }
        }

        private static bool IsEncounterDecoration(MonoBehaviour decoration, Transform wave,
            List<Vector3> anchors, bool includeHierarchy)
        {
            if (!decoration || !decoration.gameObject.scene.IsValid() ||
                !decoration.gameObject.scene.isLoaded || !IsPresentDecoration(decoration) ||
                ConvertedDecorations.Contains(decoration.GetInstanceID()))
            {
                return false;
            }

            if (includeHierarchy && (decoration.transform == wave || decoration.transform.IsChildOf(wave)))
            {
                return true;
            }

            float maximumDistance = EncounterRadius * EncounterRadius;
            foreach (Vector3 anchor in anchors)
            {
                if (Vector3.SqrMagnitude(decoration.transform.position - anchor) <= maximumDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPresentDecoration(MonoBehaviour decoration)
        {
            if (decoration is MannequinPoses && decoration.GetComponentInParent<EnemyIdentifier>(true)) return false;
            if (!decoration || !decoration.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (decoration is UltraMiseryMeshStatue meshStatue) return meshStatue.HasBody;

            Animator animator = decoration.GetComponentInChildren<Animator>();
            if (!animator || !animator.gameObject.activeInHierarchy)
            {
                return false;
            }

            foreach (Renderer renderer in animator.GetComponentsInChildren<Renderer>())
            {
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                    !(renderer is ParticleSystemRenderer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ConvertDecoration(MonoBehaviour decoration, EnemyType type, Transform wave)
        {
            if (!decoration || ConvertedDecorations.Contains(decoration.GetInstanceID()) ||
                !AwakeningDecorations.Add(decoration.GetInstanceID())) return false;
            float activateAt = Time.time;
            if (type == EnemyType.Mannequin)
            {
                int waveId = wave ? wave.GetInstanceID() : 0;
                PendingMannequins.Add(new PendingMannequin { Decoration = decoration, Wave = wave,
                    Id = decoration.GetInstanceID(), WaveId = waveId });
                if (waveId != 0)
                {
                    AwakeningWaves.TryGetValue(waveId, out int count);
                    AwakeningWaves[waveId] = count + 1;
                }
                if (!mannequinQueueRunning)
                {
                    mannequinQueueRunning = true;
                    Plugin.Instance.StartCoroutine(DrainMannequins(mannequinQueueGeneration));
                }
                return true;
            }
            Plugin.Instance.StartCoroutine(AwakenThenConvert(decoration, type, wave, activateAt));
            return true;
        }

        private static IEnumerator DrainMannequins(int generation)
        {
            // Let every discovery path enqueue before choosing the nearest body.
            yield return null;
            while (generation == mannequinQueueGeneration && PendingMannequins.Count > 0)
            {
                Vector3 player = MonoSingleton<NewMovement>.Instance ? MonoSingleton<NewMovement>.Instance.transform.position : Vector3.zero;
                int selected = -1;
                float closest = float.PositiveInfinity;
                for (int i = PendingMannequins.Count - 1; i >= 0; i--)
                {
                    PendingMannequin candidate = PendingMannequins[i];
                    if (!UltraMiseryState.IsActive || !candidate.Decoration || !IsPresentDecoration(candidate.Decoration))
                    {
                        ReleaseQueuedWave(candidate);
                        AwakeningDecorations.Remove(candidate.Id);
                        PendingMannequins.RemoveAt(i);
                        continue;
                    }
                }
                for (int i = 0; i < PendingMannequins.Count; i++)
                {
                    float distance = (PendingMannequins[i].Decoration.transform.position - player).sqrMagnitude;
                    if (distance < closest) { closest = distance; selected = i; }
                }
                if (selected < 0) break;
                PendingMannequin next = PendingMannequins[selected];
                PendingMannequins.RemoveAt(selected);
                // Transfer the room reservation to the awakening coroutine in
                // the same frame; do not let wave completion skip queued bodies.
                ReleaseQueuedWave(next);
                Plugin.Instance.StartCoroutine(AwakenThenConvert(next.Decoration, EnemyType.Mannequin, next.Wave, Time.time));
                yield return new WaitForSeconds(MannequinActivationInterval);
            }
            if (generation == mannequinQueueGeneration) mannequinQueueRunning = false;
        }

        private static void ReleaseQueuedWave(PendingMannequin pending)
        {
            if (pending.WaveId == 0 || !AwakeningWaves.TryGetValue(pending.WaveId, out int count)) return;
            if (count <= 1) AwakeningWaves.Remove(pending.WaveId);
            else AwakeningWaves[pending.WaveId] = count - 1;
        }

        private static IEnumerator AwakenThenConvert(MonoBehaviour decoration, EnemyType type, Transform wave, float activateAt)
        {
            int id = decoration.GetInstanceID();
            int waveId = wave ? wave.GetInstanceID() : 0;
            if (waveId != 0)
            {
                AwakeningWaves.TryGetValue(waveId, out int count);
                AwakeningWaves[waveId] = count + 1;
            }
            Animator animator = decoration.GetComponentInChildren<Animator>();
            bool events = animator && animator.fireEvents;
            bool scriptEnabled = decoration.enabled;
            try
            {
                // Reserve wave bookkeeping before waiting, so the room cannot
                // finish while its queued statues have yet to awaken.
                float delay = activateAt - Time.time;
                if (delay > 0f) yield return new WaitForSeconds(delay);
                // Let the decoration's Start finish before taking over its pose.
                yield return null;
                if (!decoration || !UltraMiseryState.IsActive || !IsPresentDecoration(decoration)) yield break;
                if (type == EnemyType.Mannequin)
                {
                    // A long frame must not release all overdue reservations.
                    while (Time.time < nextActualMannequinActivation)
                    {
                        yield return new WaitForSeconds(nextActualMannequinActivation - Time.time);
                        if (!decoration || !UltraMiseryState.IsActive || !IsPresentDecoration(decoration)) yield break;
                    }
                    nextActualMannequinActivation = Time.time + MannequinActivationInterval;
                }
                StatueFake authoredStatue = decoration as StatueFake;
                if (authoredStatue && (IsZeroFive() || HasAuthoredEnemyActivation(authoredStatue.onComplete)))
                {
                    // Intro statues already own the real enemy via their events.
                    // Keep Done/onComplete intact instead of suppressing events
                    // and manufacturing a second enemy next to the authored one.
                    ConvertedDecorations.Add(id);
                    authoredStatue.enabled = true;
                    if (animator)
                    {
                        animator.enabled = true;
                        animator.fireEvents = true;
                    }
                    if (!authoredStatue.beingActivated)
                        authoredStatue.Activate();
                    float wait = 0f;
                    while (authoredStatue && authoredStatue.gameObject.activeInHierarchy && wait < 20f)
                    {
                        wait += Time.deltaTime;
                        yield return null;
                    }
                    // No clone fallback: a slow intro must never create a third
                    // Cerberus. Native completion activates the authored boss.
                    yield break;
                }
                if (type == EnemyType.Cerberus && animator && animator.HasState(0, Animator.StringToHash("Awaken")))
                {
                    // Completion is handled below. Suppress authored animation
                    // callbacks that could swap/destroy the statue mid-conversion.
                    decoration.CancelInvoke();
                    decoration.enabled = false;
                    animator.enabled = true;
                    animator.fireEvents = false;
                    animator.speed = 1f;
                    animator.Play("Awaken", 0, 0f);
                    yield return null;
                    yield return WaitForEntrance(animator, "Awaken");
                }
                if (!decoration || !UltraMiseryState.IsActive) yield break;
                if (CompleteConversion(decoration, type, wave) && wave)
                {
                    ActivateNextWave controller = wave.GetComponent<ActivateNextWave>();
                    if (controller) controller.CountEnemies();
                }
            }
            finally
            {
                AwakeningDecorations.Remove(id);
                if (waveId != 0 && AwakeningWaves.TryGetValue(waveId, out int count))
                {
                    if (count <= 1) AwakeningWaves.Remove(waveId);
                    else AwakeningWaves[waveId] = count - 1;
                }
                if (animator) animator.fireEvents = events;
                if (decoration) decoration.enabled = scriptEnabled;
            }
        }

        private static IEnumerator WaitForEntrance(Animator animator, string state)
        {
            float elapsed = 0f;
            while (animator && animator.gameObject.activeInHierarchy && elapsed < 12f)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
                if (!info.IsName(state) || info.normalizedTime >= 1f) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private static bool IsZeroFive()
        {
            return !SceneHelper.IsPlayingCustom &&
                (SceneHelper.CurrentScene == "Level 0-5" ||
                 (MonoSingleton<StatsManager>.Instance && MonoSingleton<StatsManager>.Instance.levelNumber == 5));
        }

        private static bool HasAuthoredEnemyActivation(UltrakillEvent activation)
        {
            if (activation == null) return false;
            if (activation.toActivateObjects != null)
            {
                foreach (GameObject target in activation.toActivateObjects)
                    if (target && target.GetComponentInChildren<EnemyIdentifier>(true)) return true;
            }
            if (activation.onActivate != null)
            {
                for (int i = 0; i < activation.onActivate.GetPersistentEventCount(); i++)
                {
                    Object target = activation.onActivate.GetPersistentTarget(i);
                    GameObject gameObject = target as GameObject;
                    if (target is Component component) gameObject = component.gameObject;
                    if (gameObject && gameObject.GetComponentInChildren<EnemyIdentifier>(true)) return true;
                }
            }
            return false;
        }

        private static IEnumerator PlayMannequinEntrance(EnemyIdentifier enemy)
        {
            // Start initializes difficulty and movement before the entrance pose.
            yield return null;
            if (!enemy || enemy.dead) yield break;
            Mannequin mannequin = enemy.GetComponent<Mannequin>();
            Enemy body = enemy.GetComponent<Enemy>();
            Animator animator = enemy.GetComponentInChildren<Animator>();
            if (!mannequin || !body || !animator || !animator.HasState(0, Animator.StringToHash("Landing"))) yield break;
            bool mannequinEnabled = mannequin.enabled;
            bool bodyEnabled = body.enabled;
            try
            {
                mannequin.enabled = false;
                body.enabled = false;
                mannequin.CancelInvoke();
                animator.Play("Landing", 0, 0f);
                yield return null;
                yield return WaitForEntrance(animator, "Landing");
            }
            finally
            {
                if (enemy && !enemy.dead)
                {
                    if (body) body.enabled = bodyEnabled;
                    if (mannequin)
                    {
                        mannequin.enabled = mannequinEnabled;
                        // Restore the periodic decision loop stopped for entry.
                        if (mannequinEnabled) mannequin.Invoke("SlowUpdate", 0f);
                    }
                }
            }
        }

        private static bool CompleteConversion(MonoBehaviour decoration, EnemyType type, Transform wave)
        {
            if (!decoration || ConvertedDecorations.Contains(decoration.GetInstanceID()))
            {
                return false;
            }

            Vector3 position = StatueSpawnRules.SpawnInFront(type) ? StatueSpawnRules.PowerPosition(decoration) : decoration.transform.position;
            Quaternion rotation = type == EnemyType.Power ? StatueSpawnRules.PowerRotation(decoration.transform) : decoration.transform.rotation;
            // A decorative Ferryman summons its own unit; do not claim a
            // nearby dormant Ferryman belonging to a scripted boss encounter.
            EnemyIdentifier enemy = type == EnemyType.Ferryman ? null : FindAuthoredEnemy(decoration.transform, type, wave);

            // Resolve a complete enemy BEFORE retiring the decoration. Never
            // leave an empty pedestal after a missing prefab or malformed model.
            if (!enemy)
            {
                if (type == EnemyType.Cerberus && IsZeroFive()) return false;
                GameObject prefab = StatueSpawnRules.Prefab(type);
                if (!prefab) return false;
                GameObject spawned = Object.Instantiate(prefab, position, rotation);
                enemy = spawned.GetComponentInChildren<EnemyIdentifier>(true);
                if (!IsCompleteEnemy(enemy, type))
                {
                    Object.Destroy(spawned);
                    Plugin.Log.LogWarning($"Rejected incomplete decorative {type} replacement; original statue retained.");
                    return false;
                }
                spawned.name = $"UltraMisery Awakened Decorative {type}";
            }
            ConvertedDecorations.Add(decoration.GetInstanceID());
            Plugin.Log.LogInfo($"Statue conversion spawned {type}: scene={decoration.gameObject.scene.name}, statue={decoration.name}, position={enemy.transform.position}.");
            // Never parent a replacement under the decoration being removed.
            if (wave && (wave == decoration.transform || wave.IsChildOf(decoration.transform))) wave = decoration.transform.parent;
            if (wave && (wave == enemy.transform || wave.IsChildOf(enemy.transform))) wave = enemy.transform.parent;

            if (enemy && enemy.transform.IsChildOf(decoration.transform))
            {
                enemy.transform.SetParent(wave, true);
            }

            bool decorationBelongsToEnemy = enemy &&
                (decoration.transform == enemy.transform || decoration.transform.IsChildOf(enemy.transform));
            if (decorationBelongsToEnemy)
            {
                // MannequinPoses can live on the real enemy's rig. Removing that
                // GameObject also removes its renderer/animator/hitboxes.
                decoration.enabled = false;
                Object.Destroy(decoration);
            }
            else if (!StatueSpawnRules.SpawnInFront(type))
            {
                MeshStatueDiscovery.RetireHeldOrb(decoration);
                decoration.transform.SetParent(null, true);
                decoration.gameObject.SetActive(false);
                Object.Destroy(decoration.gameObject);
            }

            if (!enemy)
            {
                return false;
            }

            ClaimedEnemies.Add(enemy.GetInstanceID());
            enemy.dontCountAsKills = false;
            enemy.enabled = true;
            Enemy body = enemy.GetComponent<Enemy>();
            body.enabled = true;
            body.dontDie = false;
            Animator animator = enemy.GetComponentInChildren<Animator>(true);
            animator.enabled = true;
            foreach (EnemyIdentifierIdentifier limb in enemy.GetComponentsInChildren<EnemyIdentifierIdentifier>(true))
                limb.eid = enemy;
            if (type == EnemyType.Cerberus) enemy.GetComponent<StatueBoss>().enabled = true;
            if (type == EnemyType.Mannequin) enemy.GetComponent<Mannequin>().enabled = true;
            if (type == EnemyType.Power) enemy.GetComponent<Power>().enabled = true;
            if (type == EnemyType.Ferryman) enemy.GetComponent<Ferryman>().enabled = true;
            if (enemy.transform.parent != wave)
            {
                enemy.transform.SetParent(wave, true);
            }

            bool alreadyStarted = MonoSingleton<EnemyTracker>.Instance &&
                MonoSingleton<EnemyTracker>.Instance.GetCurrentEnemies().Contains(enemy);
            enemy.spawnIn = true;
            if (enemy.spawnEffect)
            {
                // Reset the authored effect before revealing the enemy. This
                // gives converted decorations the same entrance cue as normal
                // arena spawns instead of popping into existence immediately.
                enemy.spawnEffect.SetActive(false);
            }

            ActivateHierarchy(enemy.transform, wave);
            if (enemy.spawnEffect && !enemy.spawnEffect.activeSelf)
            {
                enemy.spawnEffect.SetActive(true);
            }

            // EnemyIdentifier.Start consumes spawnIn for enemies that had not
            // initialized yet. For an authored enemy whose Start already ran,
            // the effect was started explicitly above, so clear the pending flag.
            if (alreadyStarted)
            {
                enemy.spawnIn = false;
            }

            EnemyRules.EnsureApplied(enemy);
            if (type == EnemyType.Mannequin) Plugin.Instance.StartCoroutine(PlayMannequinEntrance(enemy));
            return true;
        }

        private static EnemyIdentifier FindAuthoredEnemy(Transform decoration, EnemyType type,
            Transform wave)
        {
            EnemyIdentifier owner = decoration.GetComponentInParent<EnemyIdentifier>(true);
            if (IsCompleteEnemy(owner, type) && !ClaimedEnemies.Contains(owner.GetInstanceID()))
            {
                return owner;
            }

            Transform container = decoration.parent;
            if (container)
            {
                EnemyIdentifier best = FindNearestEnemy(container.GetComponentsInChildren<EnemyIdentifier>(true),
                    type, decoration.position, 0.75f);
                if (best)
                {
                    return best;
                }
            }

            return wave
                ? FindNearestEnemy(wave.GetComponentsInChildren<EnemyIdentifier>(true), type,
                    decoration.position, 0.75f)
                : null;
        }

        private static EnemyIdentifier FindNearestEnemy(EnemyIdentifier[] enemies, EnemyType type,
            Vector3 position, float maxDistance)
        {
            EnemyIdentifier result = null;
            float bestDistance = maxDistance * maxDistance;
            foreach (EnemyIdentifier enemy in enemies)
            {
                if (!IsCompleteEnemy(enemy, type) || enemy.gameObject.activeInHierarchy || ClaimedEnemies.Contains(enemy.GetInstanceID()))
                {
                    continue;
                }

                float distance = Vector3.SqrMagnitude(enemy.transform.position - position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    result = enemy;
                }
            }

            return result;
        }

        private static void ActivateHierarchy(Transform target, Transform stopAt)
        {
            List<GameObject> hierarchy = new List<GameObject>();
            Transform current = target;
            while (current && current != stopAt)
            {
                hierarchy.Add(current.gameObject);
                current = current.parent;
            }

            for (int index = hierarchy.Count - 1; index >= 0; index--)
            {
                hierarchy[index].SetActive(true);
            }
        }

        private static bool IsCompleteEnemy(EnemyIdentifier enemy, EnemyType type)
        {
            if (!enemy || enemy.enemyType != type || enemy.dead) return false;
            Enemy body = enemy.GetComponent<Enemy>();
            if (!body || body.health <= 0f) return false;
            if (type == EnemyType.Cerberus && !enemy.GetComponent<StatueBoss>()) return false;
            if (type == EnemyType.Mannequin && !enemy.GetComponent<Mannequin>()) return false;
            if (type == EnemyType.Power && !enemy.GetComponent<Power>()) return false;
            if (type == EnemyType.Ferryman && !enemy.GetComponent<Ferryman>()) return false;
            return enemy.GetComponentInChildren<Animator>(true) &&
                enemy.GetComponentsInChildren<EnemyIdentifierIdentifier>(true).Length > 0 &&
                enemy.GetComponentsInChildren<Collider>(true).Length > 0;
        }
    }

    [HarmonyPatch(typeof(ActivateNextWave), "FixedUpdate")]
    internal static class StatueAwakeningWaveWaitPatch
    {
        private static bool Prefix(ActivateNextWave __instance)
        {
            return !UltraMiseryState.IsActive || !StatueEncounterRules.HasPendingAwakening(__instance);
        }
    }

    [HarmonyPatch(typeof(ActivateArena), "Activate")]
    internal static class DecorativeStatuesArenaActivationPatch
    {
        private static void Prefix(ActivateArena __instance)
        {
            if (UltraMiseryState.IsActive && !__instance.activated)
            {
                StatueEncounterRules.ActivateForArena(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ActivateNextWave), "SpawnEnemy")]
    internal static class DecorativeStatuesNextWaveActivationPatch
    {
        private static void Prefix(ActivateNextWave __instance, int ___currentEnemy)
        {
            StatueEncounterRules.ActivateForNextWave(__instance, ___currentEnemy);
        }
    }

    [HarmonyPatch(typeof(StatueFake), "Start")]
    internal static class DecorativeCerberusCachePatch
    {
        private static void Prefix(StatueFake __instance)
        {
            StatueEncounterRules.Register(__instance);
        }
    }

    [HarmonyPatch(typeof(MannequinPoses), "Start")]
    internal static class DecorativeMannequinCachePatch
    {
        private static void Prefix(MannequinPoses __instance)
        {
            StatueEncounterRules.Register(__instance);
        }
    }
}
