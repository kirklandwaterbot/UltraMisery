using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    [HarmonyPatch(typeof(ScreenDistortionController), "Update")]
    internal static class EnemyScreenStaticPatch
    {
        internal static bool Suppress(bool ultraMisery, bool enemyOwned) => ultraMisery && enemyOwned;

        private static bool Prefix(ScreenDistortionController __instance,
            PostProcessV2_Handler ___pp, ref int ___lastCount)
        {
            if (!UltraMiseryState.IsActive || !___pp) return true;
            int visibleCount = 0;
            float strength = 0f;
            // Keep registered fields intact so vanilla difficulty immediately
            // recovers its effects. Only prune destroyed references, as native does.
            for (int i = __instance.fields.Count - 1; i >= 0; i--)
            {
                ScreenDistortionField field = __instance.fields[i];
                if (!field) { __instance.fields.RemoveAt(i); continue; }
                if (Suppress(true, field.GetComponentInParent<EnemyIdentifier>() != null)) continue;
                visibleCount++;
                strength = Mathf.Max(strength, field.currentStrength);
            }
            if (___lastCount != visibleCount)
            {
                ___pp.WickedEffect(visibleCount > 0);
                ___lastCount = visibleCount;
            }
            Shader.SetGlobalFloat("_RandomNoiseStrength", strength);
            return false;
        }
    }
}
