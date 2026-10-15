using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ULTRAKILL.Enemy;
using ULTRAKILL.Portal;
using UnityEngine;

namespace UltraMisery
{
    internal sealed class BalancedWeaponExplosion : MonoBehaviour { }
    internal sealed class BalancedOverheatSaw : MonoBehaviour { }
    internal sealed class BalancedRailShot : MonoBehaviour { internal bool Malicious; }
    internal sealed class BalancedWeaponBeam : MonoBehaviour { }
    internal sealed class BalancedMaliciousExplosion : MonoBehaviour { }
    [HarmonyPatch]
    internal static class WeaponSpawnBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Grenade), "Explode");
            yield return WeaponPatchReflection.Method(typeof(Punch), "BlastCheck");
            yield return WeaponPatchReflection.Method(typeof(Nailgun), "SuperSaw");
            yield return WeaponPatchReflection.Method(typeof(Railcannon), "Shoot");
            yield return WeaponPatchReflection.Method(typeof(RevolverBeam), "HitSomething");
            yield return WeaponPatchReflection.Method(typeof(Cannonball), "Explode");
        }
        internal static GameObject Spawned(GameObject obj, Component owner)
        {
            if (!WeaponBalanceRules.Active || !obj) return obj;
            if (owner is Grenade grenade && !grenade.enemy && !grenade.originEnemy)
                foreach (var explosion in obj.GetComponentsInChildren<Explosion>(true)) explosion.gameObject.AddComponent<BalancedWeaponExplosion>();
            if (owner is Punch)
                foreach (var explosion in obj.GetComponentsInChildren<Explosion>(true)) { explosion.maxSize *= .5f; explosion.speed *= .5f; }
            if (owner is Nailgun && obj.GetComponent<Nail>()) obj.AddComponent<BalancedOverheatSaw>();
            if (owner is Railcannon rail && (obj.GetComponent<RevolverBeam>() || obj.GetComponent<Harpoon>())) obj.AddComponent<BalancedRailShot>().Malicious = rail.variation == 2;
            var railMarker = owner.GetComponent<BalancedRailShot>();
            if (railMarker || owner.GetComponent<BalancedWeaponBeam>() || (owner is Cannonball ball && ball.sourceWeapon && ball.sourceWeapon.GetComponent<RocketLauncher>()))
                foreach (var explosion in obj.GetComponentsInChildren<Explosion>(true))
                {
                    explosion.gameObject.AddComponent<BalancedWeaponExplosion>();
                    if (railMarker && railMarker.Malicious) explosion.gameObject.AddComponent<BalancedMaliciousExplosion>();
                }
            return obj;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.opcode == OpCodes.Call && c.operand is MethodInfo m && m.DeclaringType == typeof(UnityEngine.Object) && m.Name == "Instantiate" && m.ReturnType == typeof(GameObject))
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(WeaponSpawnBalancePatch), "Spawned")); count++; }
            }
            if (count == 0) throw new InvalidOperationException("Weapon spawn layout changed");
        }
    }
    [HarmonyPatch(typeof(Grenade), "GrenadeBeam")]
    internal static class BoostedCoreBeamBalancePatch
    {
        internal static RevolverBeam Spawned(RevolverBeam beam, Grenade grenade)
        {
            if (WeaponBalanceRules.Active && !grenade.enemy && !grenade.originEnemy) beam.gameObject.AddComponent<BalancedWeaponBeam>();
            return beam;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(UnityEngine.Object) && m.Name == "Instantiate" && m.ReturnType == typeof(RevolverBeam))
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(BoostedCoreBeamBalancePatch), "Spawned")); count++; }
            }
            if (count != 1) throw new InvalidOperationException("Boosted core beam spawn changed: " + count);
        }
    }
    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class WeaponExplosionDamageBalancePatch
    {
        private static void Prefix(Explosion __instance, out bool __state)
        { __state = WeaponBalanceScope.CoreOrRocket; WeaponBalanceScope.CoreOrRocket = __instance.GetComponent<BalancedWeaponExplosion>(); }
        private static void Finalizer(bool __state) => WeaponBalanceScope.CoreOrRocket = __state;
    }
    internal sealed class NailDamageState
    {
        internal bool Scope;
        internal float Damage;
    }
    [HarmonyPatch(typeof(Nail), "DamageEnemy")]
    internal static class NailgunProjectileDamageBalancePatch
    {
        internal static bool PlayerNail(bool enemy, bool chainsaw) => !enemy && !chainsaw;
        private static void Prefix(Nail __instance, out NailDamageState __state)
        {
            __state = new NailDamageState { Scope = WeaponBalanceScope.Nailgun, Damage = __instance.damage };
            // Covers source-less nailbombs and player saws. Enemy projectiles
            // and shotgun chainsaws keep their separate damage rules.
            WeaponBalanceScope.Nailgun = __state.Scope || PlayerNail(__instance.enemy, __instance.chainsaw);
        }
        private static void Finalizer(Nail __instance, NailDamageState __state)
        {
            if (__state == null) return;
            WeaponBalanceScope.Nailgun = __state.Scope;
            // Preserve the previous patch's per-hit restoration of temporary
            // fodder bonuses, without applying the old Overheat saw damage half.
            if (__instance) __instance.damage = __state.Damage;
        }
    }
    [HarmonyPatch]
    internal static class OverheatSawLifetimeBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Nail), "Start");
            yield return WeaponPatchReflection.Method(typeof(Nail), "MagnetRelease");
        }
        internal static void Schedule(MonoBehaviour target, string method, float time)
        {
            if (WeaponBalanceRules.Active && target.GetComponent<BalancedOverheatSaw>() && (method == "RemoveTime" || method == "MasterRemoveTime")) time *= .5f;
            target.Invoke(method, time);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m.Name == "Invoke" && m.DeclaringType == typeof(MonoBehaviour))
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(OverheatSawLifetimeBalancePatch), "Schedule"); count++; }
                yield return c;
            }
            if (count == 0) throw new InvalidOperationException("Saw lifetime schedule missing");
        }
    }
    [HarmonyPatch]
    internal static class ChainsawArmInteractionBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return WeaponPatchReflection.Method(typeof(Punch), "ActiveFrame"); yield return WeaponPatchReflection.Method(typeof(Punch), "TryParryProjectile"); }
        internal static bool FindChainsaw(Component target, out Chainsaw saw)
        { if (WeaponBalanceRules.Active) { saw = null; return false; } return target.TryGetComponent(out saw); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m.Name == "TryGetComponent" && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Chainsaw))
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(ChainsawArmInteractionBalancePatch), "FindChainsaw"); count++; }
                yield return c;
            }
            if (count != 1) throw new InvalidOperationException("Chainsaw arm lookup changed: " + count);
        }
    }
    [HarmonyPatch(typeof(RevolverBeam), "ExecuteHits")]
    internal static class MaliciousCoreInteractionBalancePatch
    {
        internal static Grenade GetGrenade(Component target, RevolverBeam beam)
        {
            if (BlocksGrenade(beam)) return null;
            return target.GetComponentInParent<Grenade>();
        }
        internal static bool BlocksGrenadeType(BeamType type, bool boostedCore, bool malicious) =>
            WeaponBalanceRules.Active && (malicious || (type == BeamType.Revolver && !boostedCore));
        internal static bool BlocksGrenade(RevolverBeam beam) => BlocksGrenadeType(beam.beamType,
            beam.GetComponent<BalancedWeaponBeam>() || beam.isRocketBeam ||
                (beam.sourceWeapon && beam.sourceWeapon.GetComponent<ShotgunHammer>()), IsMalicious(beam));
        internal static bool IsMalicious(RevolverBeam beam) => WeaponBalanceRules.Active &&
            ((beam.GetComponent<BalancedRailShot>() is BalancedRailShot marker && marker.Malicious) ||
             (beam.sourceWeapon && beam.sourceWeapon.GetComponent<Railcannon>() is Railcannon rail && rail.variation == 2));
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                if (c.operand is MethodInfo m && m.Name == "GetComponentInParent" && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Grenade))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(c);
                    yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(MaliciousCoreInteractionBalancePatch), "GetGrenade")); count++;
                }
                else yield return c;
            }
            if (count != 1) throw new InvalidOperationException("Malicious grenade lookup changed: " + count);
        }
    }
    [HarmonyPatch(typeof(RevolverBeam), "HitSomething")]
    internal static class MaliciousCoreImpactBalancePatch
    {
        private static bool Prefix(RevolverBeam __instance, PhysicsCastResult hit) => !MaliciousCoreInteractionBalancePatch.BlocksGrenade(__instance) || !hit.transform.GetComponentInParent<Grenade>();
    }
    [HarmonyPatch(typeof(Coin), "ExplosiveReflectCheck")]
    internal static class CoinCoreTargetBalancePatch
    {
        private static bool Prefix(TargetDataRef data, GameObject ___altBeam, ref bool __result)
        {
            if (!WeaponBalanceRules.Active) return true;
            // Non-revolver reflected beams retain their native targeting unless
            // already prohibited (Malicious). Cannonballs remain valid targets.
            if (___altBeam)
            {
                var beam = ___altBeam.GetComponent<RevolverBeam>();
                if (!beam || !MaliciousCoreInteractionBalancePatch.BlocksGrenade(beam)) return true;
            }
            var target = data.target.GameObject;
            if (!target || !target.GetComponentInParent<Grenade>()) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(Explosion), "Collide")]
    internal static class MaliciousSplashCoreBalancePatch
    {
        private static bool Prefix(Explosion __instance, Collider other)
        {
            if (!WeaponBalanceRules.Active || !other.GetComponentInParent<Grenade>()) return true;
            if (__instance.GetComponent<BalancedMaliciousExplosion>()) return false;
            return !(__instance.sourceWeapon && __instance.sourceWeapon.GetComponent<Railcannon>() is Railcannon rail && rail.variation == 2);
        }
    }
    [HarmonyPatch(typeof(Cannonball), "Collide")]
    internal static class CannonballDamageBalancePatch
    {
        private static void Prefix(Cannonball __instance, out bool __state)
        { __state = WeaponBalanceScope.CoreOrRocket; WeaponBalanceScope.CoreOrRocket = __instance.sourceWeapon && __instance.sourceWeapon.GetComponent<RocketLauncher>(); }
        private static void Finalizer(bool __state) => WeaponBalanceScope.CoreOrRocket = __state;
    }
    [HarmonyPatch(typeof(Harpoon), "Punched")]
    internal static class ScrewdriverPunchBalancePatch
    {
        private static void Prefix(Harpoon __instance, out bool __state)
        { __state = WeaponBalanceScope.CoreOrRocket; WeaponBalanceScope.CoreOrRocket = __instance.GetComponent<BalancedRailShot>(); }
        private static void Finalizer(bool __state) => WeaponBalanceScope.CoreOrRocket = __state;
        private static void Postfix(Harpoon __instance)
        { var clock = __instance.GetComponent<SlowDrillClock>(); if (clock) clock.NextTick = 0f; }
    }
    [HarmonyPatch(typeof(RocketLauncher), "Shoot")]
    internal static class RocketFireRateBalancePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => WeaponBalanceRules.ScaleLoads(instructions, "rateOfFire", "Twice", 1);
    }
    [HarmonyPatch(typeof(Grenade), "FixedUpdate")]
    internal static class RocketTravelBalancePatch
    {
        private static void Prefix(Grenade __instance, out float[] __state, ref float ___downpull)
        {
            __state = null;
            if (!WeaponBalanceRules.Active || !__instance.rocket || __instance.enemy || __instance.originEnemy) return;
            __state = new[] { __instance.rocketSpeed, ___downpull };
            __instance.rocketSpeed *= .5f;
        }
        private static void Finalizer(Grenade __instance, float[] __state, ref float ___downpull)
        {
            if (__state == null) return;
            __instance.rocketSpeed = __state[0];
            if (___downpull > __state[1]) ___downpull = __state[1] + (___downpull - __state[1]) * .5f;
        }
    }
    [HarmonyPatch(typeof(HookArm), "FixedUpdate")]
    internal static class RocketWhiplashBalancePatch
    {
        internal static Grenade Filter(Grenade grenade) => WeaponBalanceRules.Active && grenade && grenade.rocket ? null : grenade;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(ObjectTracker) && m.Name == "GetGrenade")
                { yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(RocketWhiplashBalancePatch), "Filter")); count++; }
            }
            if (count < 1) throw new InvalidOperationException("Hook rocket lookup changed");
        }
    }
    internal sealed class UltraMiseryMovementBudget : MonoBehaviour
    {
        internal bool AirJumpUsed;
        internal bool RideUsed;
        private void Update()
        {
            var player = GetComponent<NewMovement>();
            if (!WeaponBalanceRules.Active || player.dead || (player.gc.onGround && !player.ridingRocket)) { AirJumpUsed = false; RideUsed = false; }
        }
    }
    [HarmonyPatch(typeof(Grenade), "PlayerRideStart")]
    internal static class RocketRideBudgetBalancePatch
    {
        private static bool Prefix(Grenade __instance)
        {
            if (!WeaponBalanceRules.Active) return true;
            var player = MonoSingleton<NewMovement>.Instance;
            if (__instance.playerRiding) return false;
            var budget = player.GetComponent<UltraMiseryMovementBudget>() ?? player.gameObject.AddComponent<UltraMiseryMovementBudget>();
            if (budget.RideUsed) return false;
            budget.RideUsed = true; return true;
        }
    }
    [HarmonyPatch(typeof(NewMovement), "WallJump")]
    internal static class FinalWallJumpSoundBalancePatch
    {
        // Share the input limit so the native final-jump sound plays on jump five.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ExtraJumpsBalancePatch.Transpiler(instructions);
    }
    [HarmonyPatch(typeof(NewMovement), "HandleInputs")]
    internal static class ExtraJumpsBalancePatch
    {
        internal static int WallLimit(int original) => WeaponBalanceRules.Active ? 5 : original;
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i];
                if (code[i].LoadsConstant(3) && i > 0 && code[i - 1].operand is FieldInfo f && f.Name == "currentWallJumps")
                { yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(ExtraJumpsBalancePatch), "WallLimit")); count++; }
            }
            if (count != 1) throw new InvalidOperationException("Wall jump limit changed: " + count);
        }
        private static void Postfix(NewMovement __instance, bool ___jumpCooldown)
        {
            if (!WeaponBalanceRules.Active || __instance.dead || __instance.gc.onGround || ___jumpCooldown || __instance.fakeFallRequests > 0 || GameStateManager.Instance.PlayerInputLocked) return;
            if (!MonoSingleton<InputManager>.Instance.InputSource.Jump.WasPerformedThisFrame) return;
            var budget = __instance.GetComponent<UltraMiseryMovementBudget>() ?? __instance.gameObject.AddComponent<UltraMiseryMovementBudget>();
            if (budget.AirJumpUsed) return;
            __instance.Jump();
            budget.AirJumpUsed = true;
        }
    }
}
