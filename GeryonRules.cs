using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class GeryonPermanentEnrage : MonoBehaviour
    {
        private Geryon geryon;
        private EnemyIdentifier enemy;
        private float phaseTwoThreshold;
        private bool phaseChangeInvoked;

        internal void Initialize(Geryon target, EnemyIdentifier identifier)
        {
            geryon = target;
            enemy = identifier;
            phaseTwoThreshold = identifier.Health / 2f;
        }

        private void Update()
        {
            if (!UltraMiseryState.IsActive || !geryon || !enemy || enemy.dead ||
                phaseChangeInvoked || enemy.health >= phaseTwoThreshold)
            {
                return;
            }

            // Geryon's native Update cannot fire this event after we enable its
            // phase-two/enraged state at spawn. Preserve the authored 50% phase
            // transition without reapplying the 1.25x enrage speed a second time.
            phaseChangeInvoked = true;
            geryon.onPhaseChange?.Invoke("");
        }
    }

    [HarmonyPatch(typeof(Geryon), "IsInMaxHeat")]
    internal static class GeryonNoOverheatPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!UltraMiseryState.IsActive)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Geryon), "Start")]
    internal static class GeryonHeatBarPatch
    {
        private static void Postfix(Geryon __instance)
        {
            if (!UltraMiseryState.IsActive)
            {
                return;
            }

            BossHealthBar healthBar = __instance.GetComponent<BossHealthBar>();
            if (healthBar)
            {
                healthBar.secondaryBar = false;
            }

            EnemyIdentifier enemy = __instance.GetComponent<EnemyIdentifier>();
            Animator animator = __instance.GetComponent<Animator>();
            if (!enemy || !animator)
            {
                return;
            }

            // Native Geryon represents its phase-two rage with this private flag.
            // Enabling it after Start applies all enraged behavior from phase one,
            // while the helper above still fires the normal transition event at 50%.
            Traverse traversal = Traverse.Create(__instance);
            traversal.Field("secondPhase").SetValue(true);
            animator.speed *= 1.25f;

            GameObject effect = traversal.Field("currentEnrageEffect").GetValue<GameObject>();
            if (!effect && MonoSingleton<DefaultReferenceManager>.Instance &&
                MonoSingleton<DefaultReferenceManager>.Instance.enrageEffect)
            {
                Transform anchor = enemy.weakPoint ? enemy.weakPoint.transform : enemy.transform;
                effect = Object.Instantiate(
                    MonoSingleton<DefaultReferenceManager>.Instance.enrageEffect,
                    anchor.position,
                    Quaternion.identity);
                effect.name = "UltraMisery Geryon Permanent Enrage";
                effect.transform.SetParent(anchor, true);
                effect.transform.localScale *= 10f;

                EnrageEffect presentation = effect.GetComponentInChildren<EnrageEffect>(true);
                if (presentation)
                {
                    presentation.activated = true;
                }

                UltraMiseryForcedEnrage.MakePresentationNonInteractive(effect);
                traversal.Field("currentEnrageEffect").SetValue(effect);
            }

            GeryonPermanentEnrage permanent =
                __instance.GetComponent<GeryonPermanentEnrage>();
            if (!permanent)
            {
                permanent = __instance.gameObject.AddComponent<GeryonPermanentEnrage>();
            }

            permanent.Initialize(__instance, enemy);
        }
    }
}
