using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UltraMisery
{
    internal sealed class UltraMiseryAttackOwner : MonoBehaviour
    {
        internal EnemyIdentifier Owner;
        internal bool ReflectedByEnemy;
    }

    internal sealed class UltraMiseryLifeState : MonoBehaviour
    {
        internal float MaximumHealth;
        internal float NextParry;
    }

    internal static class LifestealRules
    {
        private static readonly FieldInfo DifficultyField = AccessTools.Field(typeof(EnemyIdentifier), "difficulty");
        private static readonly FieldInfo FireMultiplierField = AccessTools.Field(typeof(EnemyIdentifier), "getFireDamageMultiplier");
        [ThreadStatic] internal static EnemyIdentifier Attacker;
        internal static bool AiCombatAllowed => AiCombatRules.AllowsAiCombat(UltraMiseryState.IsActive,
            MonoSingleton<CheatsController>.Instance && MonoSingleton<CheatsController>.Instance.cheatsEnabled,
            ULTRAKILL.Cheats.EnemiesHateEnemies.Active);

        internal static EnemyIdentifier Resolve(Component source)
        {
            if (!source) return null;
            UltraMiseryAttackOwner tag = source.GetComponentInParent<UltraMiseryAttackOwner>();
            if (tag && tag.ReflectedByEnemy && tag.Owner) return tag.Owner;
            // Player parries must not keep healing the enemy who fired the shot.
            if (source is Projectile projectile && (projectile.playerBullet || projectile.friendly || projectile.parried)) return null;
            if (source is ThrownSword sword && sword.deflected) return null;
            if (tag && tag.Owner) return tag.Owner;
            if (source is Explosion explosion && explosion.originEnemy) return explosion.originEnemy;
            if (source is Grenade grenade && grenade.originEnemy) return grenade.originEnemy;
            if (source is SwingCheck2 swing && swing.eid) return swing.eid;
            return source.GetComponentInParent<EnemyIdentifier>();
        }

        internal static void TagSpawn(Object spawned, Component source)
        {
            if (!UltraMiseryState.IsActive || !spawned) return;
            EnemyIdentifier owner = Resolve(source);
            if (!owner) return;
            GameObject go = spawned as GameObject;
            if (spawned is Component component) go = component.gameObject;
            if (!go || go.GetComponent<EnemyIdentifier>()) return;
            UltraMiseryAttackOwner tag = go.GetComponent<UltraMiseryAttackOwner>() ?? go.AddComponent<UltraMiseryAttackOwner>();
            tag.Owner = owner;
            UltraMiseryAttackOwner sourceTag = source.GetComponentInParent<UltraMiseryAttackOwner>();
            tag.ReflectedByEnemy = sourceTag && sourceTag.ReflectedByEnemy;
            if ((source is Mass && go.TryGetComponent<Projectile>(out var massShot) && massShot.explosive) ||
                source.GetComponentInParent<UltraMiseryMassCannon>())
            {
                if (!go.GetComponent<UltraMiseryMassCannon>()) go.AddComponent<UltraMiseryMassCannon>();
            }
            if (source is MortarLauncher && go.GetComponent<Projectile>())
            {
                var clearance = go.GetComponent<UltraMiseryMortarClearance>() ?? go.AddComponent<UltraMiseryMortarClearance>();
                clearance.Bind(owner);
            }
        }

        internal static UltraMiseryLifeState State(EnemyIdentifier enemy)
        {
            UltraMiseryLifeState state = enemy.GetComponent<UltraMiseryLifeState>();
            if (!state)
            {
                state = enemy.gameObject.AddComponent<UltraMiseryLifeState>();
                Enemy body = enemy.GetComponent<Enemy>();
                state.MaximumHealth = body ? (body.originalHealth > 0f ? body.originalHealth : body.health) : 0f;
            }
            return state;
        }

        internal static void Heal(EnemyIdentifier enemy, float amount)
        {
            if (!UltraMiseryState.IsActive || !enemy || enemy.dead || amount <= 0f || float.IsNaN(amount)) return;
            amount *= AiStalemateRules.Scale(enemy);
            Enemy body = enemy.GetComponent<Enemy>();
            if (!body || body.health <= 0f) return;
            float maximum = State(enemy).MaximumHealth;
            if (maximum <= body.health) return;
            body.health = Mathf.Min(maximum, body.health + amount);
            enemy.health = body.health;
        }

        internal static bool CanParry(EnemyIdentifier defender, EnemyIdentifier attacker)
        {
            if (!AiCombatAllowed || !defender || defender.dead || !attacker || attacker == defender || attacker.dead ||
                defender.enemyType == EnemyType.Idol || defender.enemyType == EnemyType.Deathcatcher ||
                !defender.GetComponent<Enemy>()) return false;
            UltraMiseryLifeState state = State(defender);
            if (Time.time < state.NextParry) return false;
            state.NextParry = Time.time + 1f;
            return true;
        }

        // Convert incoming enemy-attack damage into the defender's health units.
        // Do not call GetHurt as a probe: it can trigger death/phase side effects.
        internal static float ParryHealth(EnemyIdentifier defender, GameObject hit, float damage)
        {
            if (defender.blessed || ULTRAKILL.Cheats.InvincibleEnemies.Enabled) return 0f;
            damage *= DamageBonusRules.InvertBonus(defender.totalDamageTakenMultiplier) / Mathf.Max(0.001f, defender.totalHealthModifier);
            int difficulty = (int)DifficultyField.GetValue(defender);
            if (defender.isBoss && difficulty >= 4) damage /= difficulty == 5 ? 2f : 1.5f;
            for (int i = 0; defender.weaknesses != null && defender.weaknessMultipliers != null && i < defender.weaknesses.Length && i < defender.weaknessMultipliers.Length; i++)
                if (defender.weaknesses[i] == "enemy") damage *= DamageBonusRules.InvertBonus(defender.weaknessMultipliers[i]);
            if ((bool)FireMultiplierField.GetValue(defender) && defender.burners != null && defender.burners.Count > 0) damage *= DamageBonusRules.InvertBonus(1.5f);
            Enemy body = defender.GetComponent<Enemy>();
            if (body && body.IsZombie() && body.gc && !body.gc.onGround) damage *= DamageBonusRules.InvertBonus(1.5f);
            if (body && body.IsStatue() && body.extraDamageZones.Contains(hit)) damage *= DamageBonusRules.InvertBonus(body.extraDamageMultiplier);
            return Mathf.Max(0f, damage);
        }
    }

    // Track ownership at creation, not by finding the nearest enemy to a hit.
    // Cover native enemy scripts and their detached damage objects, including
    // delayed projectile impacts and explosions spawned by other attacks.
    [HarmonyPatch]
    internal static class EnemyAttackAttributionPatch
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());
        private static readonly HashSet<Type> DamageTypes = new HashSet<Type>
        {
            typeof(Projectile), typeof(ProjectileSpread), typeof(Explosion), typeof(Grenade),
            typeof(SwingCheck2), typeof(RevolverBeam), typeof(PhysicalShockwave), typeof(MassSpear),
            typeof(VirtueInsignia), typeof(HurtZone), typeof(FireZone), typeof(EnemyShotgun), typeof(DroneFlesh), typeof(Enemy),
              typeof(BlackHoleProjectile), typeof(ContinuousBeam), typeof(Nail), typeof(ThrownSword), typeof(LeviathanController), typeof(Mandalore), typeof(MortarLauncher), typeof(EnemyRevolver), typeof(EnemyNailgun)
        };

        private static bool RelevantCall(MethodInfo method)
        {
            return (method.DeclaringType == typeof(Object) && method.Name == "Instantiate") ||
                (method.DeclaringType == typeof(NewMovement) && method.Name == "GetHurt") ||
                (method.DeclaringType == typeof(EnemyIdentifier) && method.Name == "DeliverDamage");
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in typeof(EnemyIdentifier).Assembly.GetTypes())
            {
                if (!typeof(Component).IsAssignableFrom(type) ||
                    (!typeof(EnemyScript).IsAssignableFrom(type) && !DamageTypes.Contains(type))) continue;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // GetHurt belongs to the VICTIM; preserve the attacker's
                    // surrounding scope through nested victim damage handlers.
                    if (method.IsStatic || method.IsAbstract || method.Name == "GetHurt" ||
                        method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    if (ContainsRelevantCall(method))
                        yield return method;
                }
            }
        }

        private static bool ContainsRelevantCall(MethodInfo method)
        {
            byte[] code = method.GetMethodBody().GetILAsByteArray();
            for (int offset = 0; offset < code.Length;)
            {
                short value = code[offset++];
                if (value == 0xfe) value = (short)(0xfe00 | code[offset++]);
                OpCode op = OpCodesByValue[value];
                if (op.OperandType == OperandType.InlineMethod)
                {
                    MethodBase called = method.Module.ResolveMethod(BitConverter.ToInt32(code, offset));
                    if (called is MethodInfo call && RelevantCall(call)) return true;
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset++; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch:
                        offset += 4 + BitConverter.ToInt32(code, offset) * 4; break;
                    default: offset += 4; break;
                }
            }
            return false;
        }

        private static void Prefix(Component __instance, out EnemyIdentifier __state)
        {
            __state = LifestealRules.Attacker;
            LifestealRules.Attacker = UltraMiseryState.IsActive ? LifestealRules.Resolve(__instance) : null;
        }

        private static void Finalizer(EnemyIdentifier __state) => LifestealRules.Attacker = __state;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo tag = AccessTools.Method(typeof(LifestealRules), nameof(LifestealRules.TagSpawn));
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.operand is MethodInfo method &&
                    ((method.DeclaringType == typeof(Object) && method.Name == "Instantiate") ||
                     (method.DeclaringType == typeof(HomingOrbRules) && method.Name.StartsWith("Instantiate"))) &&
                    typeof(Object).IsAssignableFrom(method.ReturnType))
                {
                    yield return new CodeInstruction(OpCodes.Dup);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, tag);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Enemy), "Start")]
    internal static class LifestealMaximumHealthPatch
    {
        private static void Postfix(Enemy __instance)
        {
            if (!UltraMiseryState.IsActive) return;
            EnemyIdentifier enemy = __instance.GetComponent<EnemyIdentifier>();
            if (enemy) LifestealRules.State(enemy);
        }
    }

    [HarmonyPatch(typeof(NewMovement), "GetHurt")]
    internal static class PlayerDamageLifestealPatch
    {
        internal struct Hit { internal EnemyIdentifier Attacker; internal int Health; }
        private static void Prefix(NewMovement __instance, out Hit __state)
        {
            __state = new Hit { Attacker = LifestealRules.Attacker, Health = __instance.hp };
        }
        private static void Postfix(NewMovement __instance, Hit __state)
        {
            LifestealRules.Heal(__state.Attacker, Mathf.Max(0, __state.Health - Mathf.Max(0, __instance.hp)));
        }
    }

    [HarmonyPatch(typeof(Enemy), "GetHurt")]
    internal static class EnemyDamageLifestealPatch
    {
        internal struct Hit { internal EnemyIdentifier Attacker; internal float Health; }
        private static void Prefix(Enemy __instance, out Hit __state)
        {
            __state = new Hit { Attacker = LifestealRules.AiCombatAllowed ? LifestealRules.Attacker : null, Health = __instance.health };
        }
        private static void Postfix(Enemy __instance, Hit __state)
        {
            if (!__state.Attacker || __state.Attacker.gameObject == __instance.gameObject) return;
            EnemyIdentifier victim = __instance.GetComponent<EnemyIdentifier>();
            AiTargetRecoveryRules.Damage(__state.Attacker, victim, Mathf.Max(0f, __state.Health - Mathf.Max(0f, __instance.health)));
            if (victim && AiStalemateRules.AllowsHitLifesteal(__state.Attacker.enemyType, victim.enemyType))
                LifestealRules.Heal(__state.Attacker, Mathf.Max(0f, __state.Health - Mathf.Max(0f, __instance.health)));
        }
    }

    [HarmonyPatch(typeof(Projectile), "Collided")]
    internal static class EnemyProjectileParryPatch
    {
        private static bool Prefix(Projectile __instance, Collider other, bool ___active)
        {
            if (!LifestealRules.AiCombatAllowed || !___active || __instance.unparryable ||
                __instance.undeflectable || __instance.decorative || __instance.playerBullet) return true;
            EnemyIdentifier attacker = LifestealRules.Resolve(__instance);
            EnemyIdentifier defender = FriendlyFireRules.GetEnemy(other);
            if (!defender || !other || other.CompareTag("Armor") || __instance.alreadyHitEnemies.Contains(defender)) return true;
            if (!(other.CompareTag("Head") || other.CompareTag("Body") || other.CompareTag("Limb") || other.CompareTag("EndLimb"))) return true;
            if (defender.enemyType == __instance.safeEnemyType ||
                EnemyIdentifier.CheckHurtException(__instance.safeEnemyType, defender.enemyType, __instance.targetHandle)) return true;
            ProjectileSpread spread = __instance.spreaded ? __instance.GetComponentInParent<ProjectileSpread>() : null;
            if (spread && spread.hitEnemies.Contains(defender)) return true;
            if (!LifestealRules.CanParry(defender, attacker)) return true;
            float prevented = __instance.damage / 10f * __instance.enemyDamageMultiplier;
            LifestealRules.Heal(defender, LifestealRules.ParryHealth(defender, other.gameObject, prevented));
            // Detach from the original spread so a reflected shot has its own hit history.
            __instance.spreaded = false;
            __instance.transform.SetParent(null, true);
            UltraMiseryAttackOwner tag = __instance.GetComponent<UltraMiseryAttackOwner>() ??
                __instance.gameObject.AddComponent<UltraMiseryAttackOwner>();
            tag.Owner = defender;
            tag.ReflectedByEnemy = true;
            __instance.targetHandle = null;
#pragma warning disable CS0612, CS0618
            __instance.target = new EnemyTarget(attacker.transform);
#pragma warning restore CS0612, CS0618
            __instance.safeEnemyType = defender.enemyType;
            __instance.parried = false;
            __instance.friendly = false;
            Vector3 target = attacker.overrideCenter ? attacker.overrideCenter.position : attacker.transform.position;
            Vector3 direction = target - __instance.transform.position;
            if (direction.sqrMagnitude > 0.001f) __instance.transform.forward = direction.normalized;
            Rigidbody body = __instance.GetComponent<Rigidbody>();
            if (body) body.velocity = __instance.transform.forward * Mathf.Max(10f, body.velocity.magnitude);
            __instance.transform.position += __instance.transform.forward * 0.5f;
            __instance.alreadyHitEnemies.Clear();
            __instance.alreadyHitEnemies.Add(defender);
            return false;
        }
    }

    [HarmonyPatch(typeof(SwingCheck2), "CheckEidCollision")]
    internal static class EnemyMeleeParryPatch
    {
        private static readonly MethodInfo RelevantMask = AccessTools.PropertyGetter(typeof(SwingCheck2), "relevantLMask");
        private static bool Prefix(SwingCheck2 __instance, EnemyIdentifier enid, Collider other, List<EnemyIdentifier> ___hitEnemies)
        {
            EnemyIdentifier attacker = __instance.eid;
            Enemy body = attacker ? attacker.GetComponent<Enemy>() : null;
            if (!LifestealRules.AiCombatAllowed || !__instance.damaging || !body || !body.parryable ||
                !enid || !other || ___hitEnemies.Contains(enid)) return true;
#pragma warning disable CS0618
            if (enid.enemyType == __instance.type || attacker.immuneToFriendlyFire ||
                EnemyIdentifier.CheckHurtException(__instance.type, enid.enemyType, attacker.target)) return true;
#pragma warning restore CS0618
            if (__instance.useRaycastCheck)
            {
                Vector3 start = new Vector3(attacker.transform.position.x, __instance.transform.position.y, attacker.transform.position.z);
                LayerMask mask = (LayerMask)RelevantMask.Invoke(__instance, null);
                if (Physics.Raycast(start, other.transform.position - start, Vector3.Distance(start, other.transform.position), mask)) return true;
            }
            if (!LifestealRules.CanParry(enid, attacker)) return true;
            float prevented = (__instance.enemyDamage == 0 ? __instance.damage / 10 : __instance.enemyDamage) *
                attacker.totalDamageModifier;
            if (__instance.type == EnemyType.Guttertank && enid.enemyType == EnemyType.Gutterman) prevented *= 4f;
            LifestealRules.Heal(enid, LifestealRules.ParryHealth(enid, other.gameObject, prevented));
            ___hitEnemies.Add(enid);
            if (other) __instance.hitColliders.Add(other);
            return false;
        }
    }
}
