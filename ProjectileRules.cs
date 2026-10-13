using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UltraMisery
{
    internal sealed class UltraMiseryHomingOrbReplacement : MonoBehaviour
    {
        internal Vector3 ProgressPosition;
        internal float StalledFor;
        internal bool Launched;
    }

    internal static class HomingOrbRules
    {
        private const string HomingAddress = "Assets/Prefabs/Attacks and Projectiles/Projectile Homing.prefab";
        private static GameObject homingPrefab;
        private static float retryAfter;
        private static readonly HashSet<string> reportedPrefabs = new HashSet<string>();

        private static GameObject GetHomingPrefab()
        {
            if (homingPrefab) return homingPrefab;
            if (Time.unscaledTime < retryAfter) return null;
            retryAfter = Time.unscaledTime + 10f;
            try
            {
                GameObject loaded = AssetHelper.LoadPrefab(HomingAddress);
                if (!loaded || !loaded.GetComponent<Projectile>())
                    throw new InvalidOperationException("Homing prefab is missing its root Projectile component.");
                homingPrefab = loaded;
                Plugin.Log.LogInfo($"Homing orb asset loaded: {HomingAddress}");
                return homingPrefab;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"Homing orb asset load failed; retaining original shots: {exception}");
                return null;
            }
        }

        // Swap at Instantiate so native target/damage assignments and stored
        // projectile references all apply to the actual replacement object.
        internal static GameObject InstantiateDroneShot(GameObject prefab, Vector3 position, Quaternion rotation, Drone shooter)
        {
            EnemyIdentifier enemy = shooter.GetComponent<EnemyIdentifier>();
            if (enemy && enemy.enemyType == EnemyType.Providence)
                return Object.Instantiate(prefab, position, rotation);
            return InstantiateOwnedEnemyShot(prefab, position, rotation, shooter);
        }

        internal static GameObject InstantiateOwnedEnemyShot(GameObject prefab, Vector3 position, Quaternion rotation, Component shooter)
        {
            GameObject result = InstantiateEnemyShot(prefab, position, rotation);
            EnemyIdentifier owner = shooter ? shooter.GetComponent<EnemyIdentifier>() : null;
            if (owner && UltraMiseryState.IsActive)
                foreach (UltraMiseryHomingOrbReplacement marker in result.GetComponentsInChildren<UltraMiseryHomingOrbReplacement>(true))
                {
#pragma warning disable CS0612, CS0618
                    marker.GetComponent<Projectile>().target = owner.target;
#pragma warning restore CS0612, CS0618
                }
            return result;
        }

        internal static GameObject InstantiateEnemyShot(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!UltraMiseryState.IsActive || !prefab)
                return Object.Instantiate(prefab, position, rotation);
            Projectile source = prefab.GetComponent<Projectile>();
            ProjectileSpread spread = prefab.GetComponentInChildren<ProjectileSpread>(true);
            if (!source && !spread)
                return Object.Instantiate(prefab, position, rotation);
            if (source && (source.friendly || source.playerBullet || source.decorative ||
                prefab.GetComponent<UltraMiseryHomingOrbReplacement>()))
                return Object.Instantiate(prefab, position, rotation);
            GameObject stock = GetHomingPrefab();
            if (!stock) return Object.Instantiate(prefab, position, rotation);
            GameObject result;
            if (source)
                result = CreateOrb(stock, source, position, rotation);
            else
            {
                result = Object.Instantiate(prefab, position, rotation);
                // Preserve the spread wrapper. Its Start clones our seed orb
                // and propagates the target assigned by the firing enemy.
                foreach (Projectile child in result.GetComponentsInChildren<Projectile>(true))
                {
                    if (child.friendly || child.playerBullet || child.decorative) continue;
                    GameObject orb = CreateOrb(stock, child, child.transform.position, child.transform.rotation);
                    orb.transform.SetParent(child.transform.parent, true);
                    child.gameObject.SetActive(false);
                    child.transform.SetParent(null, true);
                    Object.Destroy(child.gameObject);
                }
            }
            if (reportedPrefabs.Add(prefab.name))
                Plugin.Log.LogInfo($"Homing orb replacement confirmed for firing prefab '{prefab.name}'.");
            return result;
        }

        internal static bool UsesHomingShotgun(EnemyType type) => type == EnemyType.Swordsmachine ||
            type == EnemyType.V2 || type == EnemyType.V2Second;

        internal static GameObject InstantiateSwordsmachinePellet(GameObject prefab, Vector3 position,
            Quaternion rotation, Transform parent, EnemyShotgun shotgun)
        {
            // Both V2 fights and Swordsmachine share the native EnemyShotgun.
            EnemyIdentifier owner = shotgun ? shotgun.GetComponentInParent<EnemyIdentifier>() : null;
            if (!UltraMiseryState.IsActive || !shotgun ||
                !(owner && UsesHomingShotgun(owner.enemyType)) && !shotgun.GetComponentInParent<SwordsMachine>())
                return Object.Instantiate(prefab, position, rotation, parent);

            Projectile source = prefab ? prefab.GetComponent<Projectile>() : null;
            GameObject stock = source ? GetHomingPrefab() : null;
            if (!stock) return Object.Instantiate(prefab, position, rotation, parent);

            GameObject result = CreateOrb(stock, source, position, rotation);
            result.transform.SetParent(parent, true);
            string label = owner ? owner.enemyType + " shotgun" : "Swordsmachine shotgun";
            if (reportedPrefabs.Add(label))
                Plugin.Log.LogInfo($"Homing orb replacement confirmed for {label} pellets.");
            return result;
        }

        internal static void ConfigureDruidFullAuto(Projectile shot, float nativeSpeed, Mandalore knight)
        {
            shot.speed = nativeSpeed;
            if (!UltraMiseryState.IsActive || !shot.GetComponent<UltraMiseryHomingOrbReplacement>()) return;
            shot.speed = 10f;
            EnemyIdentifier enemy = knight.GetComponent<EnemyIdentifier>();
#pragma warning disable CS0612, CS0618
            if (enemy) shot.target = enemy.target;
#pragma warning restore CS0612, CS0618
        }

        private static GameObject CreateOrb(GameObject stock, Projectile source, Vector3 position, Quaternion rotation)
        {
            GameObject result = Object.Instantiate(stock, position, rotation);
            result.name = "UltraMisery Homing Orb";
            result.AddComponent<UltraMiseryHomingOrbReplacement>();
            Projectile shot = result.GetComponent<Projectile>();
            shot.damage = source.damage;
            shot.enemyDamageMultiplier = source.enemyDamageMultiplier;
            shot.sourceWeapon = source.sourceWeapon;
            shot.safeEnemyType = source.safeEnemyType;
            shot.targetHandle = source.targetHandle;
#pragma warning disable CS0612, CS0618
            shot.target = source.target;
#pragma warning restore CS0612, CS0618
            shot.spreaded = source.spreaded;
            shot.isTargetPlayer = source.isTargetPlayer;
            shot.bulletType = source.bulletType;
            shot.weaponType = source.weaponType;
            shot.homingType = HomingType.Gradual;
            shot.speed = 10f;
            shot.turnSpeed = 10f;
            shot.turningSpeedMultiplier = 0.7f;
            shot.stopTrackingAfterSeconds = 0f;
            shot.explosive = false;
            shot.bigExplosion = false;
            shot.unparryable = false;
            shot.undeflectable = false;
            result.SetActive(true);
            return result;
        }
    }

    [HarmonyPatch(typeof(EnemyShotgun), "Fire")]
    internal static class SwordsmachineHomingShotgunPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            MethodInfo replacement = AccessTools.Method(typeof(HomingOrbRules), nameof(HomingOrbRules.InstantiateSwordsmachinePellet));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Object) &&
                    method.Name == "Instantiate" && method.IsGenericMethod &&
                    method.GetGenericArguments()[0] == typeof(GameObject))
                {
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 4 && args[1].ParameterType == typeof(Vector3) &&
                        args[2].ParameterType == typeof(Quaternion) && args[3].ParameterType == typeof(Transform))
                    {
                        // Pass the shotgun instance after the four existing
                        // Instantiate arguments, retaining branch/exception labels.
                        instruction.opcode = OpCodes.Ldarg_0;
                        instruction.operand = null;
                        yield return instruction;
                        yield return new CodeInstruction(OpCodes.Call, replacement);
                        replaced++;
                        continue;
                    }
                }
                yield return instruction;
            }
            if (replaced != 2)
                throw new InvalidOperationException($"Expected two EnemyShotgun pellet creation sites; found {replaced}.");
            Plugin.Log.LogInfo("Homing spawn hook: Swordsmachine and both V2 shotguns, center and spread pellets.");
        }
    }

    [HarmonyPatch]
    internal static class EnemyHomingOrbSpawnPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MaliciousFace), "ShootProj");
            yield return AccessTools.Method(typeof(ZombieProjectiles), "ThrowProjectile");
            yield return AccessTools.Method(typeof(ZombieProjectiles), "ShootProjectile");
            yield return AccessTools.Method(typeof(Drone), "Shoot");
            yield return AccessTools.Method(typeof(Mandalore), "FullBurst");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            int speedWrites = 0;
            bool druidFullAuto = __originalMethod.DeclaringType == typeof(Mandalore) && __originalMethod.Name == "FullBurst";
            MethodInfo replacement = AccessTools.Method(typeof(HomingOrbRules), nameof(HomingOrbRules.InstantiateOwnedEnemyShot));
            foreach (CodeInstruction instruction in instructions)
            {
                if (druidFullAuto && instruction.opcode == OpCodes.Stfld &&
                    Equals(instruction.operand, AccessTools.Field(typeof(Projectile), "speed")))
                {
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HomingOrbRules), nameof(HomingOrbRules.ConfigureDruidFullAuto)));
                    speedWrites++;
                    continue;
                }
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Object) &&
                    method.Name == "Instantiate" && method.IsGenericMethod &&
                    method.GetGenericArguments()[0] == typeof(GameObject))
                {
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length == 3 && args[1].ParameterType == typeof(Vector3) && args[2].ParameterType == typeof(Quaternion))
                    {
                        if (__originalMethod.DeclaringType == typeof(Drone))
                        {
                            instruction.opcode = OpCodes.Ldarg_0;
                            instruction.operand = null;
                            yield return instruction;
                            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HomingOrbRules), nameof(HomingOrbRules.InstantiateDroneShot)));
                            replaced++;
                            continue;
                        }
                        instruction.opcode = OpCodes.Ldarg_0;
                        instruction.operand = null;
                        yield return instruction;
                        yield return new CodeInstruction(OpCodes.Call, replacement);
                        replaced++;
                        continue;
                    }
                }
                yield return instruction;
            }
            if (replaced == 0)
                throw new InvalidOperationException($"No projectile instantiation found in {__originalMethod.DeclaringType.Name}.{__originalMethod.Name}.");
            if (druidFullAuto && (replaced != 1 || speedWrites != 1))
                throw new InvalidOperationException($"Druid full-auto firing layout changed: spawns={replaced}, speed assignments={speedWrites}.");
            Plugin.Log.LogInfo($"Homing spawn hook: {__originalMethod.DeclaringType.Name}.{__originalMethod.Name}, {replaced} instantiation sites.");
        }
    }
}
