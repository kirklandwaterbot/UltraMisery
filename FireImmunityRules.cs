using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class FireImmunityRules
    {
        internal static EnemyIdentifier Owner(Component component)
        {
            EnemyIdentifierIdentifier limb = component.GetComponentInParent<EnemyIdentifierIdentifier>();
            return limb && limb.eid ? limb.eid : component.GetComponentInParent<EnemyIdentifier>();
        }

        internal static void Clear(EnemyIdentifier enemy)
        {
            if (!UltraMiseryState.IsActive || !enemy) return;
            bool oiled = enemy.beenGasolined || enemy.isGasolined;
            if (!oiled && (enemy.burners == null || enemy.burners.Count == 0)) return;
            enemy.CancelInvoke("Burn");
            foreach (Flammable flammable in enemy.GetComponentsInChildren<Flammable>(true))
            {
                if (Owner(flammable) != enemy) continue;
                flammable.PutOut(false);
                flammable.burning = false;
                flammable.fuel = 0f;
            }
            enemy.burners?.Clear();
            enemy.beenGasolined = false;
            if (oiled)
                foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>(true))
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetFloat("_OiledAmount", 0f);
                    renderer.SetPropertyBlock(block);
                }
        }
    }

    [HarmonyPatch]
    internal static class EnemyNoOilOrBurnPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EnemyIdentifier), "AddFlammable");
            yield return AccessTools.Method(typeof(EnemyIdentifier), "StartBurning");
            yield return AccessTools.Method(typeof(EnemyIdentifier), "Burn");
        }
        private static bool Prefix(EnemyIdentifier __instance)
        {
            if (!UltraMiseryState.IsActive) return true;
            FireImmunityRules.Clear(__instance);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class EnemyNoIgnitionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Flammable), "Burn");
            yield return AccessTools.Method(typeof(Flammable), "Pulse");
        }
        private static bool Prefix(Flammable __instance)
        {
            if (!UltraMiseryState.IsActive || !FireImmunityRules.Owner(__instance)) return true;
            __instance.PutOut(false);
            __instance.burning = false;
            __instance.fuel = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(GasolineStain), "AttachTo")]
    internal static class EnemyNoOilStainPatch
    {
        private static bool Prefix(GasolineStain __instance, Collider other)
        {
            if (!UltraMiseryState.IsActive || !other || !FireImmunityRules.Owner(other)) return true;
            Object.Destroy(__instance.gameObject);
            return false;
        }
    }

    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class EnemyNoFireDamagePatch
    {
        private static bool Prefix(EnemyIdentifier __instance) => !UltraMiseryState.IsActive || __instance.hitter != "fire" ||
            (LifestealRules.AiCombatAllowed && FriendlyFireRules.IsAttributedEnemyHit(__instance));
    }

    [HarmonyPatch(typeof(Enemy), "GetHurt")]
    internal static class EnemyNoDirectFireDamagePatch
    {
        private static bool Prefix(Enemy __instance)
        {
            EnemyIdentifier enemy = UltraMiseryState.IsActive ? __instance.GetComponent<EnemyIdentifier>() : null;
            return !enemy || enemy.hitter != "fire" ||
                (LifestealRules.AiCombatAllowed && FriendlyFireRules.IsAttributedEnemyHit(enemy));
        }
    }
}
