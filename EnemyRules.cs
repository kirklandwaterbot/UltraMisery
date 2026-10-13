using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class UltraMiseryEnrageAudioGate
    {
        private static readonly HashSet<int> PlayedEncounterKeys = new HashSet<int>();
        private static readonly Dictionary<int, int> ExplicitEncounterKeys = new Dictionary<int, int>();

        internal static void BindScene(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Arena arena in root.GetComponentsInChildren<Arena>(true))
                    Bind(arena.transform, arena.GetInstanceID());
                foreach (ActivateArena trigger in root.GetComponentsInChildren<ActivateArena>(true))
                {
                    if (trigger.forEnemy || trigger.enemies == null) continue;
                    foreach (GameObject member in trigger.enemies)
                    {
                        if (!member) continue;
                        Arena arena = member.GetComponentInParent<Arena>();
                        Bind(member.transform, arena ? arena.GetInstanceID() : trigger.GetInstanceID());
                    }
                }
            }
        }

        internal static void Bind(Transform group, int key)
        {
            if (!group) return;
            foreach (EnemyIdentifier enemy in group.GetComponentsInChildren<EnemyIdentifier>(true))
                ExplicitEncounterKeys[enemy.GetInstanceID()] = key;
        }

        internal static void Reset()
        {
            PlayedEncounterKeys.Clear();
            ExplicitEncounterKeys.Clear();
        }

        internal static bool TryClaim(EnemyIdentifier enemy)
        {
            if (!enemy)
            {
                return false;
            }

            if (ExplicitEncounterKeys.TryGetValue(enemy.GetInstanceID(), out int explicitKey))
                return PlayedEncounterKeys.Add(explicitKey);
            ActivateNextWave wave = enemy.GetComponentInParent<ActivateNextWave>();
            int key;
            if (wave)
            {
                Arena arena = wave.GetComponentInParent<Arena>();
                key = arena ? arena.GetInstanceID() : wave.GetInstanceID();
            }
            else if (enemy.transform.parent)
            {
                // Manually activated encounters generally keep their enemy
                // objects under one shared level-authored parent.
                key = enemy.transform.parent.GetInstanceID();
            }
            else
            {
                key = enemy.gameObject.scene.handle;
            }

            return PlayedEncounterKeys.Add(key);
        }
    }

    internal sealed class UltraMiseryForcedEnrage : MonoBehaviour
    {
        internal bool DirectEnrageApplied { get; set; }
        internal bool NativeEnrageApplied { get; set; }
        internal bool GenericEnrageApplied { get; private set; }

        private EnemyIdentifier enemy;
        private GameObject rageEffect;
        private bool modifiersInitialized;
        private bool presentationStarted;
        private bool presentationCleaned;
        private bool audioPolicyAssigned;
        private bool allowRageAudio;
        private bool rageAudioPlayed;

        private void Awake()
        {
            enemy = GetComponent<EnemyIdentifier>();
        }

        internal void EnsureAudioPolicy(EnemyIdentifier target)
        {
            enemy = target;
            if (audioPolicyAssigned)
            {
                return;
            }

            audioPolicyAssigned = true;
            allowRageAudio = UltraMiseryEnrageAudioGate.TryClaim(enemy);
        }

        internal void HandleStandaloneRageAudio(AudioSource audio)
        {
            if (!audio)
            {
                return;
            }

            if (allowRageAudio && !rageAudioPlayed)
            {
                if (audio.enabled && audio.clip && !audio.isPlaying)
                {
                    audio.Play();
                }

                rageAudioPlayed = true;
                return;
            }

            if (audio.isPlaying)
            {
                audio.Stop();
            }
        }

        internal void ApplyGenericEnrage(EnemyIdentifier target)
        {
            enemy = target;
            GenericEnrageApplied = true;
            MaintainGenericEnrage();
        }

        internal void MaintainPresentation(EnemyIdentifier target)
        {
            enemy = target;
            if (!UltraMiseryState.IsActive || !enemy || enemy.dead)
            {
                return;
            }

            presentationCleaned = false;
            EnsureAudioPolicy(enemy);

            foreach (EnemySimplifier simplifier in enemy.GetComponentsInChildren<EnemySimplifier>(true))
            {
                simplifier.enraged = true;
            }

            // EnemySimplifier owns its material refresh in Start/Update. Calling
            // SetOutline here also touched inactive Sandbox prefab children before
            // their Awake method initialized its renderer/player fields, producing
            // a NullReferenceException every half second. Setting the state is
            // sufficient and lets each simplifier refresh when it is actually live.

            // Prefer an authentic effect already created by the enemy's native
            // Enrage method. Otherwise attach ULTRAKILL's stock RageEffect prefab.
            if (!rageEffect)
            {
                presentationStarted = false;
                EnrageEffect nativeEffect = enemy.GetComponentInChildren<EnrageEffect>(true);
                if (nativeEffect)
                {
                    rageEffect = nativeEffect.gameObject;
                }
            }

            if (!rageEffect && MonoSingleton<DefaultReferenceManager>.Instance &&
                MonoSingleton<DefaultReferenceManager>.Instance.enrageEffect)
            {
                Transform anchor = enemy.weakPoint ? enemy.weakPoint.transform : enemy.transform;
                float scale = CalculateEffectScale(enemy);
                rageEffect = Object.Instantiate(
                    MonoSingleton<DefaultReferenceManager>.Instance.enrageEffect,
                    anchor.position,
                    Quaternion.identity);
                rageEffect.name = "UltraMisery Authentic Rage Effect";
                rageEffect.transform.SetParent(anchor, true);
                rageEffect.transform.localScale *= scale;

                // Presentation-only objects must never become a surface that the
                // Sandbox/Spawner Arm raycast can hit. Some stock effect variants
                // contain dormant physics components, so make the clone completely
                // non-interactive and put the whole hierarchy on Ignore Raycast.
                MakePresentationNonInteractive(rageEffect);

                // Forced rage is a difficulty rule, not a player-triggered style
                // event. Preserve the real particles/audio without awarding free
                // ENRAGED style or shaking the camera for every subwave spawn.
                EnrageEffect presentation = rageEffect.GetComponentInChildren<EnrageEffect>(true);
                if (presentation)
                {
                    presentation.activated = true;
                }
            }

            if (rageEffect)
            {
                if (EarthmoverDefenceRules.IsDefence(enemy) && !rageEffect.GetComponent<UltraMiseryDefenceRageSize>())
                {
                    var fixedSize = rageEffect.AddComponent<UltraMiseryDefenceRageSize>();
                    fixedSize.Bind(enemy.weakPoint ? enemy.weakPoint.transform : enemy.transform);
                }
                if (!rageEffect.activeSelf)
                {
                    rageEffect.SetActive(true);
                }

                foreach (ParticleSystem particles in rageEffect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (!particles.gameObject.activeSelf)
                    {
                        particles.gameObject.SetActive(true);
                    }

                    if (!particles.isPlaying && (!presentationStarted || particles.main.loop))
                    {
                        particles.Play(true);
                    }
                }

                if (!presentationStarted)
                {
                    foreach (AudioSource audio in rageEffect.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (allowRageAudio && !rageAudioPlayed)
                        {
                            if (audio.enabled && audio.clip && !audio.isPlaying)
                            {
                                audio.Play();
                            }
                        }
                        else if (audio.isPlaying)
                        {
                            audio.Stop();
                        }
                    }

                    if (allowRageAudio && !rageAudioPlayed)
                    {
                        rageAudioPlayed = true;
                    }

                    presentationStarted = true;
                }
                else if (!allowRageAudio)
                {
                    // Native enrage implementations may replay their source on
                    // the same effect object when the rage timer refreshes.
                    foreach (AudioSource audio in rageEffect.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (audio.isPlaying)
                        {
                            audio.Stop();
                        }
                    }
                }
            }
        }

        internal void CleanupPresentation()
        {
            if (presentationCleaned)
            {
                return;
            }

            presentationCleaned = true;
            presentationStarted = false;

            if (rageEffect)
            {
                foreach (EnrageEffect effect in rageEffect.GetComponentsInChildren<EnrageEffect>(true))
                {
                    // EnrageEffect.OnDestroy normally spawns an additional end
                    // sound. Suppress it so dead enemies cannot create another
                    // stack of encounter-wide rage audio.
                    effect.noParticle = true;
                }

                foreach (ParticleSystem particles in rageEffect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                foreach (AudioSource audio in rageEffect.GetComponentsInChildren<AudioSource>(true))
                {
                    audio.Stop();
                }

                rageEffect.SetActive(false);
                Object.Destroy(rageEffect);
                rageEffect = null;
            }

            if (enemy)
            {
                foreach (EnemySimplifier simplifier in enemy.GetComponentsInChildren<EnemySimplifier>(true))
                {
                    simplifier.enraged = false;
                }
            }
        }

        private void OnDisable()
        {
            if (!enemy || enemy.dead)
            {
                CleanupPresentation();
            }
        }

        private void OnDestroy()
        {
            CleanupPresentation();
        }

        internal void MaintainGenericEnrage()
        {
            if (!GenericEnrageApplied || !UltraMiseryState.IsActive || !enemy || enemy.dead)
            {
                return;
            }

            MaintainPresentation(enemy);

            EnsureModifierRefresh(enemy);
        }

        internal void EnsureModifierRefresh(EnemyIdentifier target)
        {
            enemy = target;
            if (modifiersInitialized || !enemy || enemy.dead)
            {
                return;
            }

            modifiersInitialized = true;
            // Recalculate once after the forced-rage marker exists. Any later
            // game recalculation is also covered by the postfix below.
            enemy.UpdateBuffs(false, false);
        }

        private static float CalculateEffectScale(EnemyIdentifier target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return 1f;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return Mathf.Clamp(bounds.extents.magnitude * 0.35f, 0.5f, 4f);
        }

        internal static void MakePresentationNonInteractive(GameObject presentation)
        {
            if (!presentation)
            {
                return;
            }

            foreach (Transform child in presentation.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = 2;
            }

            foreach (Collider collider in presentation.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (Rigidbody body in presentation.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.isKinematic = true;
            }
        }
    }

    internal sealed class UltraMiseryApplied : MonoBehaviour
    {
        private EnemyIdentifier enemy;
        private float nextBuffRefresh;
        private float nextEnrageRefresh;

        private void Awake()
        {
            enemy = GetComponent<EnemyIdentifier>();
        }

        private void Update()
        {
            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || enemy.puppet)
            {
                return;
            }

            // Several native enrage implementations expire after a timer. Refresh
            // the state twice per second so UltraMisery's forced enrage persists.
            enemy.immuneToFriendlyFire = FriendlyFireRules.SuppressEnemyDamage;
            if (Time.time >= nextBuffRefresh)
            {
                nextBuffRefresh = Time.time + 0.25f;
                EnemyRules.MaintainForcedBuffs(enemy);
            }

            if (Time.time >= nextEnrageRefresh)
            {
                nextEnrageRefresh = Time.time + 0.5f;
                EnemyRules.RefreshEnrage(enemy);
            }
        }
    }

    internal static class EnemyRules
    {
        internal static IEnumerator ApplyAfterEnemyStart(EnemyIdentifier enemy)
        {
            // EnemyIdentifier has an early execution order. Waiting one frame lets each
            // enemy's own Start method initialize before its native Enrage method runs.
            yield return null;

            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || enemy.puppet)
            {
                yield break;
            }

            MaintainForcedBuffs(enemy);

            if (enemy.enemyType == EnemyType.CancerousRodent ||
                enemy.enemyType == EnemyType.VeryCancerousRodent)
            {
                enemy.dontCountAsKills = false;
            }

            RefreshEnrage(enemy);

            // Providence's hookpoint and Virtue's sphere are represented by the
            // shared Drone component's spawn-on-death array.
            Drone drone = enemy.GetComponent<Drone>();
            if (drone)
            {
                DroneDeathDropRules.RemoveDrop(drone);
            }
        }

        internal static void MaintainForcedBuffs(EnemyIdentifier enemy)
        {
            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || enemy.puppet)
            {
                return;
            }

            enemy.immuneToFriendlyFire = FriendlyFireRules.SuppressEnemyDamage;
            if (!enemy.sandified)
            {
                enemy.Sandify(false);
            }

            // Maintain explicit requests in addition to the global force flag.
            // Some delayed subwaves reset their local buff request counters after
            // EnemyIdentifier.Start, which previously removed their Radiance.
            if (!enemy.speedBuff)
            {
                enemy.SpeedBuff();
            }

            if (!enemy.healthBuff)
            {
                enemy.HealthBuff();
            }

            if (!enemy.damageBuff)
            {
                enemy.DamageBuff();
            }
        }

        internal static void RefreshEnrage(EnemyIdentifier enemy)
        {
            if (!enemy || enemy.dead || enemy.puppet)
            {
                return;
            }

            // Drone has native Enrage but does not implement IEnrage in this
            // game build. Avoid giving Virtues generic rage before Drone.Start.
            if (enemy.enemyType == EnemyType.Virtue)
            {
                VirtueEnrageRules.EnsureNativeRage(enemy.GetComponent<Drone>());
                return;
            }

            UltraMiseryForcedEnrage forced = enemy.GetComponent<UltraMiseryForcedEnrage>();
            if (!forced)
            {
                forced = enemy.gameObject.AddComponent<UltraMiseryForcedEnrage>();
            }

            forced.EnsureAudioPolicy(enemy);

            // The current MaliciousFace script replaced SpiderBody and does not
            // implement IEnrage. Its real state must be activated explicitly.
            MaliciousFace face = enemy.GetComponent<MaliciousFace>();
            if (face && (!forced.NativeEnrageApplied || !face.isEnraged))
            {
                MaliciousFaceRules.EnsureNativeRage(face);
            }

            bool nativeEnrageApplied = forced.NativeEnrageApplied;
            if (!forced.NativeEnrageApplied)
            {
                MonoBehaviour[] behaviours = enemy.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour behaviour in behaviours)
                {
                    IEnrage enragedEnemy = behaviour as IEnrage;
                    if (enragedEnemy == null)
                    {
                        continue;
                    }

                    try
                    {
                        Gutterman nativeGutterman = behaviour as Gutterman;
                        if (nativeGutterman)
                        {
                            nativeGutterman.eternalRage = true;
                        }

                        SwordsMachine nativeSwordsmachine = behaviour as SwordsMachine;
                        if (nativeSwordsmachine)
                        {
                            nativeSwordsmachine.eternalRage = true;
                        }

                        if (!enragedEnemy.isEnraged)
                        {
                            enragedEnemy.Enrage();
                        }

                        // Some native implementations (notably V2) do not flip
                        // isEnraged until their transition animation finishes.
                        // Latch the successful invocation immediately so the
                        // half-second maintenance pass cannot restart it.
                        nativeEnrageApplied = true;
                    }
                    catch (System.Exception exception)
                    {
                        Plugin.Log.LogWarning($"Native enrage failed for {enemy.enemyType}: {exception.Message}");
                    }
                }

                forced.NativeEnrageApplied = nativeEnrageApplied;
            }

            // Gutterman's native rage cue is an AudioSource rather than an
            // EnrageEffect object, so include it in the same one-cue policy.
            Gutterman gutterman = enemy.GetComponent<Gutterman>();
            if (gutterman)
            {
                AudioSource nativeAudio = Traverse.Create(gutterman)
                    .Field("currentEnrageEffect").GetValue<AudioSource>();
                forced.HandleStandaloneRageAudio(nativeAudio);
            }

            // These newer enemies expose their enrage entry points directly instead
            // of implementing IEnrage.
            Power power = enemy.GetComponent<Power>();
            if (power)
            {
                if (!power.enraged)
                {
                    power.EnrageNow();
                }

                forced.DirectEnrageApplied = true;
                forced.NativeEnrageApplied = true;
                nativeEnrageApplied = true;
            }

            GabrielBase gabriel = enemy.GetComponent<GabrielBase>();
            if (gabriel)
            {
                if (!gabriel.enraged)
                {
                    gabriel.enraged = true;
                    gabriel.EnrageNow();
                }

                forced.DirectEnrageApplied = true;
                forced.NativeEnrageApplied = true;
                nativeEnrageApplied = true;
            }

            if (!forced.DirectEnrageApplied)
            {
                Mass mass = enemy.GetComponent<Mass>();
                if (mass)
                {
                    if (!mass.currentEnrageEffect)
                    {
                        mass.Enrage();
                    }

                    forced.DirectEnrageApplied = true;
                    forced.NativeEnrageApplied = true;
                    nativeEnrageApplied = true;
                }

                MinosPrime minosPrime = enemy.GetComponent<MinosPrime>();
                if (minosPrime)
                {
                    minosPrime.Enrage();
                    forced.DirectEnrageApplied = true;
                    forced.NativeEnrageApplied = true;
                    nativeEnrageApplied = true;
                }

                SisyphusPrime sisyphusPrime = enemy.GetComponent<SisyphusPrime>();
                if (sisyphusPrime)
                {
                    sisyphusPrime.Enrage();
                    forced.DirectEnrageApplied = true;
                    forced.NativeEnrageApplied = true;
                    nativeEnrageApplied = true;
                }
            }
            else
            {
                nativeEnrageApplied = true;
            }

            if (!nativeEnrageApplied)
            {
                forced.ApplyGenericEnrage(enemy);
            }
            else if (forced.GenericEnrageApplied)
            {
                forced.MaintainGenericEnrage();
            }

            // Reuse the effect produced by native rage where available. Generic
            // enemies receive the stock presentation created by this component.
            forced.MaintainPresentation(enemy);
            forced.EnsureModifierRefresh(enemy);
        }

        internal static bool ShouldPreventNativeUnEnrage(Component source)
        {
            if (!UltraMiseryState.IsActive || !source)
            {
                return false;
            }

            EnemyIdentifier enemy = source.GetComponentInParent<EnemyIdentifier>();
            if (!enemy || enemy.dead)
            {
                return false;
            }

            UltraMiseryForcedEnrage forced = enemy.GetComponent<UltraMiseryForcedEnrage>();
            return forced && forced.NativeEnrageApplied;
        }

        internal static bool ShouldPreventDuplicateNativeEnrage(Component source)
        {
            if (!UltraMiseryState.IsActive || !source)
            {
                return false;
            }

            EnemyIdentifier enemy = source.GetComponentInParent<EnemyIdentifier>();
            if (!enemy || enemy.dead)
            {
                return false;
            }

            UltraMiseryForcedEnrage forced = enemy.GetComponent<UltraMiseryForcedEnrage>();
            return forced && forced.NativeEnrageApplied;
        }

        internal static void EnsureApplied(EnemyIdentifier enemy)
        {
            FireImmunityRules.Clear(enemy);
            SupportEnemyCombatRules.EnsureVisionTarget(enemy);
            // Marked For Death must also cover enemies created by delayed 7-3
            // subwaves, including Puppets that are intentionally excluded from
            // UltraMisery's Sanded/enrage component.
            MarkedForDeathRules.ApplyToEnemy(enemy);

            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || enemy.puppet ||
                enemy.GetComponent<UltraMiseryApplied>())
            {
                return;
            }

            enemy.gameObject.AddComponent<UltraMiseryApplied>();
            Plugin.Instance.QueueEnemyRules(enemy);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "UpdateModifiers")]
    internal static class UniversalEnrageModifierPatch
    {
        private static void Postfix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance)
            {
                return;
            }

            UltraMiseryForcedEnrage forced = __instance.GetComponent<UltraMiseryForcedEnrage>();
            if (!forced || !forced.GenericEnrageApplied || forced.NativeEnrageApplied)
            {
                return;
            }

            // Radiance is already the main stat increase. Forced rage adds only
            // a small, flat modifier so UltraMisery stays difficult but fair.
            __instance.totalSpeedModifier *= 1.01f;
            __instance.totalDamageModifier *= 1.01f;
        }
    }

    [HarmonyPatch]
    internal static class NativeEnragePersistencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(V2), "UnEnrage");
            yield return AccessTools.Method(typeof(Mindflayer), "UnEnrage");
            yield return AccessTools.Method(typeof(SpiderBody), "UnEnrage");
            yield return AccessTools.Method(typeof(StatueBoss), "UnEnrage");
            yield return AccessTools.Method(typeof(Gutterman), "UnEnrage");
            yield return AccessTools.Method(typeof(SwordsMachine), "UnEnrage");
        }

        private static bool Prefix(object __instance)
        {
            Component source = __instance as Component;
            return !EnemyRules.ShouldPreventNativeUnEnrage(source);
        }
    }

    [HarmonyPatch]
    internal static class NativeEnrageDuplicateActivationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Patch only the public/native activation entry points. Helper and
            // animation-event methods must remain available to finish the first
            // transition that UltraMisery starts at spawn.
            yield return AccessTools.Method(typeof(V2), "Enrage", System.Type.EmptyTypes);
            yield return AccessTools.Method(typeof(Mindflayer), "Enrage");
            yield return AccessTools.Method(typeof(SpiderBody), "Enrage");
            yield return AccessTools.Method(typeof(StatueBoss), "Enrage");
            yield return AccessTools.Method(typeof(Gutterman), "Enrage");
            yield return AccessTools.Method(typeof(SwordsMachine), "Enrage");
            yield return AccessTools.Method(typeof(Mass), "Enrage");
            yield return AccessTools.Method(typeof(Power), "EnrageNow");
            yield return AccessTools.Method(typeof(GabrielBase), "EnrageNow");
            yield return AccessTools.Method(typeof(MinosPrime), "Enrage");
            yield return AccessTools.Method(typeof(SisyphusPrime), "Enrage");
        }

        private static bool Prefix(object __instance)
        {
            Component source = __instance as Component;
            return !EnemyRules.ShouldPreventDuplicateNativeEnrage(source);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "Start")]
    internal static class EnemyIdentifierStartPatch
    {
        private static void Postfix(EnemyIdentifier __instance)
        {
            EnemyRules.EnsureApplied(__instance);
            StatueEncounterRules.ActivateForStartedEnemy(__instance);
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "ProcessDeath")]
    internal static class ForcedEnrageDeathCleanupPatch
    {
        private static void Prefix(EnemyIdentifier __instance)
        {
            if (!__instance)
            {
                return;
            }

            UltraMiseryForcedEnrage forced = __instance.GetComponent<UltraMiseryForcedEnrage>();
            if (forced)
            {
                forced.CleanupPresentation();
            }
        }
    }
}
