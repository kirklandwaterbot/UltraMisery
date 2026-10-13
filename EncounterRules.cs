using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    [HarmonyPatch(typeof(Arena), "Awake")]
    internal static class ArenaSupportEnemyPatch
    {
        private static void Prefix(Arena __instance)
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || MonoSingleton<SandboxNavmesh>.Instance)
            {
                return;
            }

            try
            {
                ActivateNextWave[] waves = __instance.GetWaves();
                if (waves == null || waves.Length == 0)
                {
                    return;
                }

                // Arena.Awake configures enemy counts and next-wave arrays after
                // this prefix. Injecting now makes each support group a real member
                // of its own subwave instead of adding one group to the first wave.
                foreach (ActivateNextWave waveController in waves)
                {
                    if (!waveController || waveController.transform.childCount == 0 ||
                        IsInfinitePuppetSpawnerWave(waveController.transform))
                    {
                        continue;
                    }

                    Transform wave = waveController.transform;
                    List<Vector3> reserved = new List<Vector3>();
                    EnsureEnemyType(wave, EnemyType.Idol, reserved);
                    EnsureEnemyType(wave, EnemyType.Deathcatcher, reserved);
                    EnsureEnemyType(wave, EnemyType.Stalker, reserved);
                    UltraMiseryEnrageAudioGate.Bind(wave, __instance.GetInstanceID());
                }
            }
            catch (System.Exception exception)
            {
                // Arena.Awake must always be allowed to continue. A bad placement
                // or unavailable prefab should disable only the injection attempt.
                Plugin.Log.LogError($"Arena support-enemy injection failed: {exception}");
            }
        }

        internal static GameObject EnsureEnemyType(Transform wave, EnemyType type, List<Vector3> reserved)
        {
            foreach (EnemyIdentifier existing in wave.GetComponentsInChildren<EnemyIdentifier>(true))
            {
                if (IsSupportEnemy(existing.enemyType))
                {
                    Reserve(existing.transform.position, reserved);
                }

                if (existing.enemyType == type)
                {
                    return existing.gameObject;
                }
            }

            GameObject prefab = MonoSingleton<DefaultReferenceManager>.Instance.GetEnemyPrefab(type);
            if (!prefab)
            {
                Plugin.Log.LogWarning($"Could not find the {type} prefab for arena injection.");
                return null;
            }

            Vector3 position;
            Quaternion rotation;
            if (!TryFindPlacement(wave, reserved, out position, out rotation))
            {
                Plugin.Log.LogWarning($"No safe reachable placement was found for {type} in arena wave {wave.name}; skipped injection.");
                return null;
            }

            GameObject injected = Object.Instantiate(prefab, position, rotation, wave);
            injected.name = $"UltraMisery Injected {type}";
            injected.SetActive(false);
            Reserve(position, reserved);
            Plugin.Log.LogDebug($"Injected {type} into arena wave {wave.name} at {position}.");
            return injected;
        }

        private static bool IsSupportEnemy(EnemyType type)
        {
            return type == EnemyType.Idol || type == EnemyType.Deathcatcher ||
                type == EnemyType.Stalker;
        }

        internal static bool IsInfinitePuppetSpawnerWave(Transform wave)
        {
            if (!wave)
            {
                return false;
            }

            // Hungry Suicide Trees use EnemySpawnRadius to maintain an infinite
            // pool of Puppets until the tree is fed. They are progression
            // controllers, not finite arena subwaves, so adding counted support
            // enemies to them can leave their completion bookkeeping unsatisfied.
            return wave.GetComponentInChildren<EnemySpawnRadius>(true) ||
                   wave.GetComponentInParent<EnemySpawnRadius>(true) ||
                   wave.name.IndexOf("SuicideTree", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsBossEncounter(Transform wave)
        {
            if (wave.GetComponentInChildren<BossHealthBar>(true))
            {
                return true;
            }

            foreach (EnemyIdentifier enemy in wave.GetComponentsInChildren<EnemyIdentifier>(true))
            {
                switch (enemy.enemyType)
                {
                    case EnemyType.FleshPrison:
                    case EnemyType.FleshPanopticon:
                    case EnemyType.Gabriel:
                    case EnemyType.GabrielSecond:
                    case EnemyType.Geryon:
                    case EnemyType.Leviathan:
                    case EnemyType.Minos:
                    case EnemyType.MinosPrime:
                    case EnemyType.SisyphusPrime:
                    case EnemyType.V2:
                    case EnemyType.V2Second:
                        return true;
                }
            }

            return false;
        }

        private static bool TryFindPlacement(Transform wave, List<Vector3> reserved,
            out Vector3 position, out Quaternion rotation)
        {
            List<Transform> anchors = new List<Transform>();
            foreach (EnemyIdentifier enemy in wave.GetComponentsInChildren<EnemyIdentifier>(true))
            {
                if (enemy && !IsSupportEnemy(enemy.enemyType))
                {
                    anchors.Add(enemy.transform);
                }
            }

            if (anchors.Count == 0)
            {
                foreach (Transform child in wave)
                {
                    anchors.Add(child);
                }
            }

            if (anchors.Count == 0)
            {
                position = default;
                rotation = Quaternion.identity;
                return false;
            }

            int firstAnchor = 0;
            for (int anchorOffset = 0; anchorOffset < anchors.Count; anchorOffset++)
            {
                Transform anchor = anchors[(firstAnchor + anchorOffset) % anchors.Count];
                if (!anchor)
                {
                    continue;
                }

                // Stable hierarchy order and concentric offsets produce repeatable
                // locations without relying on Unity's global random state.
                for (int attempt = 0; attempt < 32; attempt++)
                {
                    float angle = (attempt % 8) * Mathf.PI / 4f;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float distance = 0.9f + (attempt / 8) * 0.9f;
                    Vector3 probe = anchor.position + new Vector3(direction.x, 0f, direction.y) * distance;
                    if (TryProjectToReachableGround(probe, anchor.position, reserved, true, out position))
                    {
                        rotation = anchor.rotation;
                        return true;
                    }
                }
            }

            // Some scripted arenas have no baked NavMesh. In that case, remain
            // immediately beside a known spawn anchor and still require ground,
            // clearance, boundary distance, and separation checks.
            for (int anchorOffset = 0; anchorOffset < anchors.Count; anchorOffset++)
            {
                Transform anchor = anchors[(firstAnchor + anchorOffset) % anchors.Count];
                if (anchor && TryProjectToReachableGround(anchor.position, anchor.position,
                        reserved, false, out position))
                {
                    rotation = anchor.rotation;
                    return true;
                }
            }

            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        private static bool TryProjectToReachableGround(Vector3 probe, Vector3 anchor,
            List<Vector3> reserved, bool requireNavMesh, out Vector3 position)
        {
            int environmentMask = LayerMaskDefaults.Get(LMD.Environment);
            RaycastHit groundHit;
            if (!Physics.Raycast(probe + Vector3.up * 12f, Vector3.down, out groundHit, 32f,
                    environmentMask, QueryTriggerInteraction.Ignore))
            {
                position = default;
                return false;
            }

            Vector3 ground = groundHit.point;
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(ground + Vector3.up * 0.25f, out navHit, 2f, NavMesh.AllAreas))
            {
                Vector3 lineStart = ground + Vector3.up * 0.75f;
                Vector3 lineEnd = navHit.position + Vector3.up * 0.75f;
                if (Vector3.Distance(lineStart, lineEnd) > 0.1f &&
                    Physics.Linecast(lineStart, lineEnd, environmentMask, QueryTriggerInteraction.Ignore))
                {
                    position = default;
                    return false;
                }

                ground = navHit.position;
            }
            else if (requireNavMesh)
            {
                position = default;
                return false;
            }

            // Never wander far enough from a real wave spawn to cross arena
            // walls, pits, or inaccessible decorative space.
            Vector2 horizontalDelta = new Vector2(ground.x - anchor.x, ground.z - anchor.z);
            if (horizontalDelta.sqrMagnitude > 20.25f)
            {
                position = default;
                return false;
            }

            position = ground + Vector3.up * 1.35f;
            if (Physics.CheckSphere(position, 0.5f, environmentMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (reserved != null)
            {
                foreach (Vector3 occupied in reserved)
                {
                    Vector2 separation = new Vector2(position.x - occupied.x, position.z - occupied.z);
                    if (separation.sqrMagnitude < 6.25f)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void Reserve(Vector3 position, List<Vector3> reserved)
        {
            if (reserved != null)
            {
                reserved.Add(position);
            }
        }
    }

    [HarmonyPatch(typeof(ActivateArena), "Activate")]
    internal static class ManualArenaSupportEnemyPatch
    {
        private static void Prefix(ActivateArena __instance)
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || MonoSingleton<SandboxNavmesh>.Instance ||
                __instance.activated || __instance.forEnemy ||
                __instance.enemies == null || __instance.enemies.Length == 0)
            {
                return;
            }

            try
            {
                GameObject anchorObject = null;
                foreach (GameObject enemy in __instance.enemies)
                {
                    if (enemy)
                    {
                        anchorObject = enemy;
                        break;
                    }
                }

                if (!anchorObject || !anchorObject.transform.parent)
                {
                    return;
                }

                Transform wave = anchorObject.transform.parent;
                if (ArenaSupportEnemyPatch.IsInfinitePuppetSpawnerWave(wave))
                {
                    return;
                }

                List<Vector3> reserved = new List<Vector3>();
                GameObject idol = ArenaSupportEnemyPatch.EnsureEnemyType(wave, EnemyType.Idol, reserved);
                GameObject deathcatcher = ArenaSupportEnemyPatch.EnsureEnemyType(wave, EnemyType.Deathcatcher, reserved);
                GameObject stalker = ArenaSupportEnemyPatch.EnsureEnemyType(wave, EnemyType.Stalker, reserved);
                AppendIfMissing(__instance, idol);
                AppendIfMissing(__instance, deathcatcher);
                AppendIfMissing(__instance, stalker);
                Arena owner = wave.GetComponentInParent<Arena>();
                foreach (GameObject member in __instance.enemies)
                    if (member) UltraMiseryEnrageAudioGate.Bind(member.transform, owner ? owner.GetInstanceID() : __instance.GetInstanceID());

                ActivateNextWave nextWave = wave.GetComponent<ActivateNextWave>();
                if (nextWave)
                {
                    nextWave.CountEnemies();
                }

            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogError($"Manual encounter support-enemy injection failed: {exception}");
            }
        }

        private static void AppendIfMissing(ActivateArena arena, GameObject enemy)
        {
            if (!enemy)
            {
                return;
            }

            foreach (GameObject existing in arena.enemies)
            {
                if (existing == enemy)
                {
                    return;
                }
            }

            int oldLength = arena.enemies.Length;
            System.Array.Resize(ref arena.enemies, oldLength + 1);
            arena.enemies[oldLength] = enemy;
        }
    }

    // Hand-authored nextEnemies arrays do not necessarily belong to Arena.
    // Extend those arrays before their first activation, never during enemy Start.
    [HarmonyPatch(typeof(ActivateNextWave), "SpawnEnemy")]
    internal static class ManualSubwaveSupportEnemyPatch
    {
        private static void Prefix(ActivateNextWave __instance, int ___currentEnemy)
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom ||
                MonoSingleton<SandboxNavmesh>.Instance || ___currentEnemy != 0 ||
                __instance.nextEnemies == null) return;
            try
            {
                var members = new List<GameObject>(__instance.nextEnemies);
                var groups = new HashSet<Transform>();
                foreach (GameObject member in __instance.nextEnemies)
                    if (member && member.transform.parent) groups.Add(member.transform.parent);
                foreach (Transform group in groups)
                {
                    ActivateNextWave counter = group.GetComponent<ActivateNextWave>();
                    if (!counter || counter.activated || ArenaSupportEnemyPatch.IsInfinitePuppetSpawnerWave(group)) continue;
                    var reserved = new List<Vector3>();
                    foreach (EnemyType type in new[] { EnemyType.Idol, EnemyType.Deathcatcher, EnemyType.Stalker })
                    {
                        int before = group.childCount;
                        GameObject support = ArenaSupportEnemyPatch.EnsureEnemyType(group, type, reserved);
                        // Preserve scripted counts; only add newly created members.
                        int added = group.childCount - before;
                        foreach (ActivateNextWave linked in group.GetComponents<ActivateNextWave>())
                            linked.enemyCount += added;
                        if (support && !members.Contains(support)) members.Add(support);
                    }
                    Arena arena = group.GetComponentInParent<Arena>();
                    UltraMiseryEnrageAudioGate.Bind(group, arena ? arena.GetInstanceID() : __instance.GetInstanceID());
                }
                __instance.nextEnemies = members.ToArray();
            }
            catch (System.Exception exception)
            {
                Plugin.Log.LogError($"Manual subwave support injection failed: {exception}");
            }
        }
    }
}
