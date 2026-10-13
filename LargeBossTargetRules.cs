using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ULTRAKILL.Enemy;
using UnityEngine;
using UnityEngine.AI;

namespace UltraMisery
{
    internal sealed class UltraMiseryBossGeometry : MonoBehaviour
    {
        internal Collider[] Hits;
        internal Bounds Bounds;
        internal float BoundsAfter;
        internal bool HasBounds;
        private void Awake() => Hits = GetComponentsInChildren<Collider>(true);
    }
    internal sealed class UltraMiseryBossTargetCache : MonoBehaviour
    {
        internal EnemyIdentifier Boss;
        internal Vector3 Aim, Approach;
        internal bool HasAim, HasApproach;
        internal float AimAfter, ApproachAfter;
        internal readonly NavMeshPath Path = new NavMeshPath();
    }
    internal static class LargeBossTargetRules
    {
        internal static bool Supported(EnemyType type) => type == EnemyType.FleshPanopticon ||
            type == EnemyType.FleshPrison || type == EnemyType.HideousMass || type == EnemyType.Minotaur ||
            type == EnemyType.Minos || type == EnemyType.Leviathan || type == EnemyType.Centaur;

        private static bool Context(Component attacker, out EnemyIdentifier boss, out UltraMiseryBossGeometry geometry)
        {
            boss = null; geometry = null;
            if (!LifestealRules.AiCombatAllowed || !attacker) return false;
            if (attacker is Projectile shot)
            {
                if (shot.playerBullet || shot.friendly || shot.parried) return false;
                if (shot.targetHandle != null)
                {
                    if (shot.targetHandle.portals.Count != 0 || !shot.targetHandle.target.isEnemy) return false;
                    boss = shot.targetHandle.target.EID;
                }
#pragma warning disable CS0612, CS0618
                else if (shot.target != null && shot.target.isEnemy) boss = shot.target.enemyIdentifier;
#pragma warning restore CS0612, CS0618
            }
            else
            {
                EnemyIdentifier owner = attacker.GetComponentInParent<EnemyIdentifier>();
                if (!owner || owner.dead || owner.target == null || !owner.target.isEnemy) return false;
                boss = owner.target.enemyIdentifier;
            }
            if (!boss || boss.dead || !Supported(boss.enemyType)) return false;
            geometry = boss.GetComponent<UltraMiseryBossGeometry>() ?? boss.gameObject.AddComponent<UltraMiseryBossGeometry>();
            return true;
        }
        private static bool Damageable(Collider hit, EnemyIdentifier boss) => hit && hit.enabled && hit.gameObject.activeInHierarchy &&
            (hit.CompareTag("Head") || hit.CompareTag("Body") || hit.CompareTag("Limb") || hit.CompareTag("EndLimb")) &&
            FriendlyFireRules.GetEnemy(hit) == boss;

        private static bool BoundsFor(UltraMiseryBossGeometry geometry, EnemyIdentifier boss, out Bounds bounds)
        {
            if (Time.time < geometry.BoundsAfter) { bounds = geometry.Bounds; return geometry.HasBounds; }
            bounds = default; bool any = false;
            foreach (Collider hit in geometry.Hits)
            {
                if (!Damageable(hit, boss)) continue;
                if (!any) bounds = hit.bounds; else bounds.Encapsulate(hit.bounds);
                any = true;
            }
            geometry.Bounds = bounds; geometry.HasBounds = any; geometry.BoundsAfter = Time.time + 0.15f;
            return any;
        }
        private static UltraMiseryBossTargetCache Cache(Component attacker, EnemyIdentifier boss)
        {
            var cache = attacker.GetComponent<UltraMiseryBossTargetCache>() ?? attacker.gameObject.AddComponent<UltraMiseryBossTargetCache>();
            if (cache.Boss != boss)
            {
                cache.Boss = boss; cache.HasAim = cache.HasApproach = false;
                cache.AimAfter = cache.ApproachAfter = 0f;
            }
            return cache;
        }
        internal static Vector3 Aim(Vector3 original, Component attacker)
        {
            if (!Context(attacker, out var boss, out var geometry) || !BoundsFor(geometry, boss, out var bounds)) return original;
            // Portal-projected targets and unrelated cached vision points stay native.
            Bounds vicinity = bounds; vicinity.Expand(4f);
            if (!vicinity.Contains(original) && (original - boss.transform.position).sqrMagnitude > 4f) return original;
            var cache = Cache(attacker, boss);
            if (Time.time < cache.AimAfter) return cache.HasAim ? cache.Aim : original;
            cache.AimAfter = Time.time + 0.15f; cache.HasAim = false;
            Vector3 source = attacker.transform.position + (attacker is Projectile ? Vector3.zero : Vector3.up);
            float best = float.PositiveInfinity;
            foreach (Collider hit in geometry.Hits)
            {
                if (!Damageable(hit, boss)) continue;
                Vector3 direction = hit.bounds.center - source;
                if (direction.sqrMagnitude < 0.001f || !hit.Raycast(new Ray(source, direction.normalized), out RaycastHit surface, direction.magnitude + hit.bounds.size.magnitude)) continue;
                if (surface.distance >= best || Physics.Linecast(source, surface.point, LayerMaskDefaults.Get(LMD.Environment), QueryTriggerInteraction.Ignore)) continue;
                bool armored = false;
                foreach (Collider obstruction in geometry.Hits)
                    if (obstruction && obstruction.enabled && obstruction.gameObject.activeInHierarchy && obstruction.CompareTag("Armor") &&
                        obstruction.Raycast(new Ray(source, direction.normalized), out RaycastHit armorHit, surface.distance) && armorHit.distance < surface.distance - 0.01f)
                    { armored = true; break; }
                if (armored) continue;
                best = surface.distance;
                cache.Aim = surface.point + direction.normalized * 0.05f;
                cache.HasAim = true;
            }
            return cache.HasAim ? cache.Aim : original;
        }
        internal static Vector3 Approach(Vector3 original, Component attacker)
        {
            if (!Context(attacker, out var boss, out var geometry) || !BoundsFor(geometry, boss, out var bounds)) return original;
            NavMeshAgent agent = attacker.GetComponent<NavMeshAgent>();
            if (!agent || !agent.enabled || !agent.isOnNavMesh || agent.isOnOffMeshLink) return original;
            // Only replace destinations at the boss, never retreat/patrol locations.
            Bounds vicinity = bounds; vicinity.Expand(4f);
            Vector3 flat = original; flat.y = bounds.center.y;
            if (!vicinity.Contains(flat)) return original;
            var cache = Cache(attacker, boss);
            if (Time.time < cache.ApproachAfter) return cache.HasApproach ? cache.Approach : attacker.transform.position;
            cache.ApproachAfter = Time.time + 0.5f; cache.HasApproach = false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Axis-aligned rectangle boundary along this ray.
                float radius = Mathf.Min(bounds.extents.x / Mathf.Max(0.001f, Mathf.Abs(direction.x)),
                    bounds.extents.z / Mathf.Max(0.001f, Mathf.Abs(direction.z)));
                Vector3 probe = bounds.center + direction * (radius + agent.radius + 0.6f);
                probe.y = attacker.transform.position.y;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit nav, 3f, agent.areaMask)) continue;
                Vector3 horizontal = nav.position; horizontal.y = bounds.center.y;
                Bounds padded = bounds; padded.Expand(agent.radius * 2f);
                if (padded.Contains(horizontal) || !agent.CalculatePath(nav.position, cache.Path) || cache.Path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] corners = cache.Path.corners;
                float distance = 0f;
                bool crossesBody = false;
                for (int j = 1; j < corners.Length; j++)
                {
                    distance += Vector3.Distance(corners[j - 1], corners[j]);
                    Vector3 start = corners[j - 1], end = corners[j];
                    start.y = end.y = padded.center.y;
                    Vector3 segment = end - start;
                    // Allow an already-overlapping spawn to exit; otherwise do
                    // not accept a NavMesh route cutting through the boss body.
                    if (!padded.Contains(start) && segment.sqrMagnitude > 0.001f &&
                        padded.IntersectRay(new Ray(start, segment.normalized), out float entry) && entry < segment.magnitude)
                    { crossesBody = true; break; }
                }
                if (crossesBody) continue;
                if (distance >= best) continue;
                best = distance; cache.Approach = nav.position; cache.HasApproach = true;
            }
            return cache.HasApproach ? cache.Approach : attacker.transform.position;
        }
        internal static bool SetDestination(NavMeshAgent agent, Vector3 position, Component attacker) => agent.SetDestination(Approach(position, attacker));
    }

    [HarmonyPatch]
    internal static class LargeBossAttackPointsPatch
    {
        private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());
        private static bool HasRelevantRead(MethodInfo method)
        {
            byte[] code = method.GetMethodBody().GetILAsByteArray();
            for (int offset = 0; offset < code.Length;)
            {
                short value = code[offset++];
                if (value == 0xfe) value = (short)(0xfe00 | code[offset++]);
                OpCode op = Codes[value];
                if ((op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Ldfld) &&
                    Relevant(method.Module.ResolveMember(BitConverter.ToInt32(code, offset)))) return true;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset++; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch: offset += 4 + BitConverter.ToInt32(code, offset) * 4; break;
                    default: offset += 4; break;
                }
            }
            return false;
        }
        private static bool Relevant(MemberInfo member) =>
            member is FieldInfo field && field.DeclaringType == typeof(TargetData) && (field.Name == "position" || field.Name == "headPosition") ||
            member is MethodInfo method && ((method.DeclaringType == typeof(EnemyTarget) && (method.Name == "get_position" || method.Name == "get_headPosition" || (method.Name == "GetNavPoint" && !method.IsStatic))) ||
            method.DeclaringType == typeof(NavMeshAgent) && method.Name == "SetDestination");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(EnemyScript).Assembly.GetTypes())
                if (typeof(EnemyScript).IsAssignableFrom(type) || type == typeof(Projectile) || typeof(IEnemyWeapon).IsAssignableFrom(type))
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (!method.IsAbstract && !method.ContainsGenericParameters && method.Name.IndexOf("Portal", StringComparison.OrdinalIgnoreCase) < 0 && method.GetMethodBody() != null &&
                            HasRelevantRead(method))
                            yield return method;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo call && call.DeclaringType == typeof(NavMeshAgent) && call.Name == "SetDestination")
                {
                    instruction.opcode = OpCodes.Ldarg_0; instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, typeof(LargeBossTargetRules).GetMethod("SetDestination", BindingFlags.Static | BindingFlags.NonPublic));
                    continue;
                }
                yield return instruction;
                if (!(instruction.operand is MemberInfo member) || !Relevant(member)) continue;
                if (instruction.opcode != OpCodes.Ldfld && instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) continue;
                string helper = member.Name == "GetNavPoint" ? "Approach" : "Aim";
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, typeof(LargeBossTargetRules).GetMethod(helper, BindingFlags.Static | BindingFlags.NonPublic));
            }
        }
    }
}
