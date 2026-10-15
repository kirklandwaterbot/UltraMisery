using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ULTRAKILL.Enemy;
using UnityEngine;

namespace UltraMisery
{
    internal static class MarksmanBalanceRules
    {
        [ThreadStatic] internal static Dictionary<EnemyIdentifier, GameObject> Points;
        internal static bool Better(float hp, float distance, float bestHp, float bestDistance) => hp > bestHp || (hp == bestHp && distance < bestDistance);
        internal static bool See(Vision vision, VisionQuery query, out TargetDataRef data)
        {
            if (!WeaponBalanceRules.Active || query.Name != "CoinEnemy") return vision.TrySee(query, out data);
            float hp = float.NegativeInfinity, distance = float.PositiveInfinity;
            EnemyIdentifier best = null;
            // Use the native portal-aware candidate set and original predicate.
            // The scan predicate intentionally returns false to examine all entries.
            var scan = new VisionQuery("UltraMiseryCoinScan", t =>
            {
                if (query.predicate(t))
                {
                    float d = (t.position - vision.sourcePos).sqrMagnitude;
                    if (Better(t.target.EID.health, d, hp, distance)) { best = t.target.EID; hp = best.health; distance = d; }
                }
                return false;
            });
            vision.TrySee(scan, out data);
            if (!best) return false;
            return vision.TrySee(new VisionQuery("UltraMiseryCoinChosen", t => t.target.EID == best && query.predicate(t) && (t.position - vision.sourcePos).sqrMagnitude <= distance + .001f), out data);
        }
        internal static GameObject StrongPoint(EnemyIdentifier enemy)
        {
            if (!WeaponBalanceRules.Active || !enemy) return enemy ? enemy.weakPoint : null;
            if (Points != null && Points.TryGetValue(enemy, out var cached)) return cached;
            GameObject best = null; float bestFactor = float.PositiveInfinity;
            foreach (var limb in enemy.GetComponentsInChildren<EnemyIdentifierIdentifier>())
            {
                if (limb.eid != enemy || !limb.gameObject.activeInHierarchy || limb.gameObject == enemy.weakPoint) continue;
                var collider = limb.GetComponent<Collider>(); if (!collider || !collider.enabled) continue;
                var obj = limb.gameObject;
                // Existing UltraMisery critical inversion makes head/limb tags
                // resist coin criticals. Never select the authored weakPoint.
                float factor = obj.CompareTag("Armor") ? 0f : obj.CompareTag("Head") ? .5f :
                    obj.CompareTag("Limb") || obj.CompareTag("EndLimb") ? 2f / 3f : 1f;
                if (factor < bestFactor) { best = obj; bestFactor = factor; }
            }
            if (Points != null) Points[enemy] = best;
            return best;
        }
        internal static TargetData Data(TargetDataRef source)
        {
            var result = source.ToData();
            if (WeaponBalanceRules.Active && source.target.Type == TargetType.ENEMY)
            {
                var point = StrongPoint(source.target.EID);
                if (point) result.headPosition = source.portalMatrix.MultiplyPoint3x4(point.transform.position);
            }
            return result;
        }
    }
    [HarmonyPatch]
    internal static class MarksmanTargetBalancePatch
    {
        private static void Prefix(out Dictionary<EnemyIdentifier, GameObject> __state)
        { __state = MarksmanBalanceRules.Points; if (WeaponBalanceRules.Active) MarksmanBalanceRules.Points = new Dictionary<EnemyIdentifier, GameObject>(); }
        private static void Finalizer(Dictionary<EnemyIdentifier, GameObject> __state) => MarksmanBalanceRules.Points = __state;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Coin), "ReflectRevolver");
            yield return WeaponPatchReflection.Method(typeof(Coin), "Punchflection");
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int sees = 0, points = 0;
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(Vision) && m.Name == "TrySee")
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(MarksmanBalanceRules), "See"); sees++; }
                else if (c.opcode == OpCodes.Ldfld && c.operand is FieldInfo f && f.DeclaringType == typeof(EnemyIdentifier) && f.Name == "weakPoint")
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(MarksmanBalanceRules), "StrongPoint"); points++; }
                else if (c.operand is MethodInfo toData && toData.Name == "ToData" && toData.ReturnType == typeof(TargetData))
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(MarksmanBalanceRules), "Data"); }
                yield return c;
            }
            if (sees == 0 || points == 0) throw new InvalidOperationException("Coin targeting layout changed");
        }
    }
    [HarmonyPatch(typeof(Coin), "Punchflection")]
    internal static class CoinPunchDamageBalancePatch
    {
        private static void Prefix(out bool __state) { __state = WeaponBalanceScope.PunchedCoin; WeaponBalanceScope.PunchedCoin = true; }
        private static void Finalizer(bool __state) => WeaponBalanceScope.PunchedCoin = __state;
    }
}
