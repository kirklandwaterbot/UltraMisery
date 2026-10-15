using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class WeaponPatchReflection
    {
        internal static MethodInfo Method(Type type, string name, Type[] arguments = null)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var method = arguments == null ? type.GetMethods(flags).Single(m => m.Name == name) : type.GetMethod(name, flags, null, arguments, null);
            return method ?? throw new MissingMethodException(type.FullName, name);
        }
    }
    // Changes are evaluated at use time; shared prefabs are never modified.
    internal static class WeaponBalanceRules
    {
        internal static bool Active => UltraMiseryState.IsActive;
        internal static float Half(float value) => Active ? value * .5f : value;
        internal static float Twice(float value) => Active ? value * 2f : value;
        internal static void KillPlayer()
        {
            var player = MonoSingleton<NewMovement>.Instance;
            if (player && !player.dead) player.GetHurt(1000000, false, 1f, false, false, 0f, true);
        }
        internal static bool Underwater => MonoSingleton<UnderwaterController>.Instance && MonoSingleton<UnderwaterController>.Instance.inWater;
        internal static float ChargeStep(float current, float target, float delta, string field)
        {
            if (Active)
            {
                switch (field)
                {
                    case "rev0charge": case "rev1charge": case "rev2charge":
                    case "shoAltNadeCharge": case "shoSawCharge": case "raicharge":
                    case "rocketCannonballCharge": delta *= .5f; break;
                    case "rocketFreezeTime": delta *= target == 0f ? 2f : .5f; break;
                    case "naiAmmo": target = 25f; delta *= 1.5f; break;
                    case "naiSaws": target = 5f; delta *= 1.5f; break;
                    case "naiHeatsinks": target = 1f; break;
                    case "naiZapperRecharge": delta /= 1.5f; break;
                    case "naiMagnetCharge": target = Mathf.Max(0f, target - 2f); break;
                }
            }
            return Mathf.MoveTowards(current, target, delta);
        }
        internal static void Cap(WeaponCharges wc)
        {
            if (!Active) return;
            wc.naiAmmo = Mathf.Min(wc.naiAmmo, 25f);
            wc.naiSaws = Mathf.Min(wc.naiSaws, 5f);
            wc.naiHeatsinks = Mathf.Min(wc.naiHeatsinks, 1f);
            wc.naiMagnetCharge = Mathf.Min(wc.naiMagnetCharge, Mathf.Max(0, 1 - wc.magnets.Count));
        }
        internal static IEnumerable<CodeInstruction> ScaleLoads(IEnumerable<CodeInstruction> instructions, string field, string helper, int expected = -1)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo f && f.Name == field)
                {
                    yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(WeaponBalanceRules), helper));
                    count++;
                }
            }
            if (count == 0 || (expected >= 0 && count != expected)) throw new InvalidOperationException("Weapon field layout changed: " + field + " / " + count);
        }
    }

    internal sealed class OilMovementState
    {
        public OilMovementState() { }
        internal bool Applied;
        internal float Begin(bool onOil)
        {
            float inputScale = onOil && Applied ? 2f : 1f;
            Applied = onOil;
            return inputScale;
        }
    }

    [HarmonyPatch(typeof(NewMovement), "FixedUpdate")]
    internal static class OilMovementBalancePatch
    {
        private static readonly ConditionalWeakTable<NewMovement, OilMovementState> States = new ConditionalWeakTable<NewMovement, OilMovementState>();
        internal static Vector3 ScalePlanar(Vector3 velocity, Vector3 gravity, float scale) =>
            velocity + Vector3.ProjectOnPlane(velocity, gravity) * (scale - 1f);

        private static void Prefix(NewMovement __instance, out bool __state)
        {
            __state = false;
            var state = States.GetOrCreateValue(__instance);
            if (WeaponBalanceRules.Active && __instance.activated && !__instance.dead &&
                !__instance.jumping && __instance.rb && __instance.gc != null && __instance.gc.onGround &&
                MonoSingleton<StainVoxelManager>.TryGetInstance(out var stains))
            {
                var voxel = StainVoxelManager.WorldToVoxelPosition(__instance.transform.position + __instance.rb.GetGravityDirection() * 1.8333334f);
                // Use the native floor-oil query, refreshed each physics tick so
                // burned/removed oil is not retained by the game's voxel cache.
                __state = stains.HasProxiesAt(voxel, 3, VoxelCheckingShape.VerticalBox, ProxySearchMode.AnyFloor, true);
            }
            float scale = state.Begin(__state);
            if (scale != 1f)
                __instance.rb.velocity = ScalePlanar(__instance.rb.velocity, __instance.rb.GetGravityDirection(), scale);
        }

        private static void Finalizer(NewMovement __instance, bool __state)
        {
            if (!__state || !__instance.rb) return;
            // Native movement runs in its normal speed space, then the actual
            // physics velocity is halved exactly once. No exponential slowdown,
            // forced speed floor, vertical penalty, or speed boost on leaving oil.
            __instance.rb.velocity = ScalePlanar(__instance.rb.velocity, __instance.rb.GetGravityDirection(), .5f);
        }
    }

    [HarmonyPatch(typeof(Magnet), "get_maxWeightFinal")]
    internal static class MagnetLoadBalancePatch
    {
        internal static float Capacity(float value) => WeaponBalanceRules.Active ? value * 1.5f : value;
        // Scale the effective load, not the serialized field, so repeated reads
        // cannot compound and disabling UltraMisery restores the native limit.
        private static void Postfix(ref float __result) => __result = Capacity(__result);
    }

    [HarmonyPatch(typeof(TimeBomb), "StartCountdown")]
    internal static class MagnetLifetimeBalancePatch
    {
        internal static float Lifetime(float timer, bool activated, bool magnet) =>
            WeaponBalanceRules.Active && !activated && magnet ? timer * .5f : timer;

        private static void Prefix(TimeBomb __instance, bool ___activated)
        {
            // Both attractor variants spawn a magnetic Harpoon. Countdown begins
            // on impact; native pause, beeping, destruction and cleanup remain intact.
            if (!WeaponBalanceRules.Active || ___activated) return;
            var harpoon = __instance.GetComponent<Harpoon>();
            __instance.timer = Lifetime(__instance.timer, ___activated, harpoon && harpoon.GetComponentInChildren<Magnet>(true));
        }
    }

    [HarmonyPatch(typeof(WeaponCharges), "Charge")]
    internal static class WeaponResourceBalancePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var c = code[i];
                if (c.Calls(WeaponPatchReflection.Method(typeof(Mathf), "MoveTowards", new[] { typeof(float), typeof(float), typeof(float) })) &&
                    i + 1 < code.Count && code[i + 1].opcode == OpCodes.Stfld && code[i + 1].operand is FieldInfo field)
                {
                    yield return new CodeInstruction(OpCodes.Ldstr, field.Name).MoveLabelsFrom(c);
                    yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(WeaponBalanceRules), "ChargeStep")); count++;
                }
                else yield return c;
            }
            if (count < 15) throw new InvalidOperationException("WeaponCharges recharge layout changed: " + count);
        }
        private static void Postfix(WeaponCharges __instance) => WeaponBalanceRules.Cap(__instance);
    }
    [HarmonyPatch]
    internal static class WeaponResourceResetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(WeaponCharges), "MaxCharges");
            yield return WeaponPatchReflection.Method(typeof(WeaponCharges), "ResetNailgunAmmo");
        }
        private static void Postfix(WeaponCharges __instance) => WeaponBalanceRules.Cap(__instance);
    }

    // Final damage scale covers immediate, delayed and reflected damage without
    // rounding integer Explosion.damage or scaling player self-damage.
    [HarmonyPatch(typeof(EnemyIdentifier), "DeliverDamage")]
    internal static class PlayerWeaponDamageBalancePatch
    {
        private static void Prefix(EnemyIdentifier __instance, GameObject sourceWeapon, ref float multiplier)
        {
            if (!WeaponBalanceRules.Active) return;
            bool half = WeaponBalanceScope.Piercer || WeaponBalanceScope.CoreOrRocket || WeaponBalanceScope.PunchedCoin;
            bool nailgun = WeaponBalanceScope.Nailgun;
            if (sourceWeapon)
            {
                if (sourceWeapon.GetComponent<Railcannon>()) half = true;
                if (sourceWeapon.GetComponent<Nailgun>()) nailgun = true;
                if (sourceWeapon.GetComponent<Shotgun>() || sourceWeapon.GetComponent<ShotgunHammer>())
                    half |= !(__instance.hitter ?? "").StartsWith("chainsaw", StringComparison.Ordinal);
                if (sourceWeapon.GetComponent<RocketLauncher>())
                    half |= !(__instance.hitter ?? "").Contains("fire") && !(__instance.hitter ?? "").Contains("napalm") && !(__instance.hitter ?? "").Contains("gasoline");
            }
            // One family-wide reduction replaces the earlier Jumpstart and
            // Overheat saw halves, including overlapping damage scopes.
            multiplier *= nailgun ? .25f / 3f : half ? .5f : 1f;
        }
    }
    internal static class WeaponBalanceScope
    {
        [ThreadStatic] internal static bool Piercer;
        [ThreadStatic] internal static bool CoreOrRocket;
        [ThreadStatic] internal static bool PunchedCoin;
        [ThreadStatic] internal static bool Nailgun;
    }
    [HarmonyPatch(typeof(RevolverBeam), "ExecuteHits")]
    internal static class PiercerDamageBalancePatch
    {
        private static void Prefix(RevolverBeam __instance, out bool __state)
        { __state = WeaponBalanceScope.Piercer; WeaponBalanceScope.Piercer = __state || __instance.GetComponent<UltraMiseryPiercerCharge>() || __instance.GetComponent<BalancedRailShot>() || __instance.GetComponent<BalancedWeaponBeam>(); }
        private static void Finalizer(bool __state) => WeaponBalanceScope.Piercer = __state;
    }

    internal sealed class WeaponReloadState : MonoBehaviour
    {
        internal float Until;
        internal float SecondaryUntil;
        internal float OriginalAnimatorSpeed = 1f;
        internal bool Slowed;
        internal void RestoreAnimation()
        {
            if (!Slowed) return;
            var anim = GetComponentInChildren<Animator>(true); if (anim) anim.speed = OriginalAnimatorSpeed;
            Slowed = false;
        }
        private void Update()
        {
            if (!WeaponBalanceRules.Active) { Until = SecondaryUntil = 0f; RestoreAnimation(); }
            else if (Time.time >= Until) RestoreAnimation();
        }
    }
    internal static class ShotgunBalance
    {
        internal static readonly SharedShotgunRecovery Shared = new SharedShotgunRecovery();
        internal const float PrimaryCooldown = 1f;
        private static readonly ConditionalWeakTable<Shotgun, ShotgunPrimaryRecovery> Primaries = new ConditionalWeakTable<Shotgun, ShotgunPrimaryRecovery>();
        internal static ShotgunPrimaryRecovery Primary(Shotgun gun) => Primaries.GetOrCreateValue(gun);
        internal static bool SharedReady => !WeaponBalanceRules.Active || Shared.Ready(Time.time);
        internal static WeaponReloadState State(Component c) => c.GetComponent<WeaponReloadState>() ?? c.gameObject.AddComponent<WeaponReloadState>();
        internal static bool Ready(Component c) => !WeaponBalanceRules.Active || Time.time >= State(c).Until;
    }
    internal sealed class ShotgunPrimaryRecovery
    {
        public ShotgunPrimaryRecovery() { }
        private bool pending;
        private float until;
        internal void Fired(float now) { pending = true; until = now + ShotgunBalance.PrimaryCooldown; }
        internal void Cancel() { pending = false; }
        internal bool Complete(float now)
        {
            if (!pending || now < until) return false;
            pending = false;
            return true;
        }
    }
    internal sealed class SharedShotgunRecovery
    {
        private float until;
        internal void Reset() { until = 0f; }
        internal bool Ready(float now) => now >= until;
        internal bool TryFire(float now)
        {
            if (!Ready(now)) return false;
            until = now + ShotgunBalance.PrimaryCooldown;
            return true;
        }
    }
    internal sealed class ShotgunShotState
    {
        internal float Spread;
        internal bool Overpumped;
    }
    [HarmonyPatch(typeof(Shotgun), "Shoot")]
    internal static class ShotgunPrimaryBalancePatch
    {
        internal static float Spread(float value) => WeaponBalanceRules.Active ? value * .25f : value;
        internal static bool LethalOverpump(int variation, int charge) => WeaponBalanceRules.Active && variation == 1 && charge >= 3;
        private static void Prefix(Shotgun __instance, out ShotgunShotState __state)
        {
            __state = new ShotgunShotState { Spread = __instance.spread, Overpumped = LethalOverpump(__instance.variation, __instance.primaryCharge) };
            __instance.spread = Spread(__instance.spread);
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Shotgun __instance, ShotgunShotState __state, bool __runOriginal)
        {
            // Native Shoot creates the overpump blast and resets the pump count.
            // An early return from the fire gate must not kill an unfired pump shot.
            if (__state != null && CompletedOverpump(__state.Overpumped, __runOriginal, __instance.primaryCharge)) WeaponBalanceRules.KillPlayer();
        }
        internal static bool CompletedOverpump(bool overpumped, bool ranOriginal, int remainingCharge) => overpumped && ranOriginal && remainingCharge == 0;
        private static void Finalizer(Shotgun __instance, ShotgunShotState __state)
        { if (__state != null) __instance.spread = __state.Spread; }
    }
    [HarmonyPatch(typeof(Shotgun), "Shoot")]
    internal static class ShotgunFireRecoveryPatch
    {
        internal static bool TryBeginPrimary(Shotgun gun)
        {
            if (!WeaponBalanceRules.Active) { ShotgunRuntimeTrace.Decision(gun, true); return true; }
            // Commit at the actual firing body, before recoil, damage or spawning.
            // Readiness callbacks, equip events and delayed invocations cannot renew it.
            float now = Time.time;
            if (!ShotgunBalance.Shared.TryFire(now)) { ShotgunRuntimeTrace.Decision(gun, false); return false; }
            ShotgunBalance.Primary(gun).Fired(now);
            ShotgunRuntimeTrace.Decision(gun, true);
            return true;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var admitted = generator.DefineLabel();
            // BepInEx's Harmony exposes Label through its older mscorlib contract.
            // Use the non-generic list boundary when building against netstandard.
            ((System.Collections.IList)typeof(CodeInstruction).GetField("labels").GetValue(code[0])).Add(admitted);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(ShotgunFireRecoveryPatch), "TryBeginPrimary"));
            yield return new CodeInstruction(OpCodes.Brtrue, admitted);
            yield return new CodeInstruction(OpCodes.Ret);
            foreach (var instruction in code) yield return instruction;
        }
    }
    [HarmonyPatch]
    internal static class ShotgunSecondaryRecoveryBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Shotgun), "ShootSinks");
            yield return WeaponPatchReflection.Method(typeof(Shotgun), "ShootSaw", new[] { typeof(bool) });
            yield return WeaponPatchReflection.Method(typeof(Shotgun), "Pump");
        }
        private static void Prefix(Shotgun __instance)
        {
            // A primary timer must not finish a later secondary animation early.
            ShotgunBalance.Primary(__instance).Cancel();
        }
    }
    [HarmonyPatch]
    internal static class ShotgunEquipBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Shotgun), "Start");
            yield return WeaponPatchReflection.Method(typeof(Shotgun), "OnEnable");
        }
        private static void Postfix(Shotgun __instance, ref float ___sinceEquipped)
        {
            if (!WeaponBalanceRules.Active) return;
            ShotgunBalance.Primary(__instance).Cancel();
            ___sinceEquipped = .25f; // Skip native held-alt-fire equip suppression too.
            __instance.ReadyGun();
        }
    }
    [HarmonyPatch(typeof(Shotgun), "Update")]
    internal static class ShotgunSecondaryGateBalancePatch
    {
        private static void Prefix(Shotgun __instance)
        {
            var recovery = ShotgunBalance.Primary(__instance);
            if (!WeaponBalanceRules.Active) { recovery.Cancel(); return; }
            // Do not wait on a long animation event to permit the next primary.
            if (recovery.Complete(Time.time)) __instance.ReadyGun();
        }
        internal static float CoreInterval(float value) => WeaponBalanceRules.Active ? value * 2f : value;
        internal static bool SharedGunReady(bool native)
        {
            bool ready = native && ShotgunBalance.SharedReady;
            ShotgunRuntimeTrace.Input(native, ready);
            return ready;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0, readyReads = 0;
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i];
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo ready && ready.Name == "gunReady")
                {
                    // The first read gates primary; the others gate pump/saw alt-fire.
                    if (++readyReads == 1)
                        yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(ShotgunSecondaryGateBalancePatch), "SharedGunReady"));
                }
                if (code[i].LoadsConstant(.5f) && i >= 2 && code[i - 2].operand is FieldInfo f && f.Name == "sinceLastCore")
                {
                    yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(ShotgunSecondaryGateBalancePatch), "CoreInterval")); count++;
                }
            }
            if (count != 1 || readyReads != 3) throw new InvalidOperationException("Shotgun input layout changed: " + count + "/" + readyReads);
        }
    }
    [HarmonyPatch(typeof(ShotgunHammer), "ImpactEffects")]
    internal static class HammerOverpumpBalancePatch
    {
        private static void Prefix(ShotgunHammer __instance, out bool __state)
        {
            __state = ShotgunPrimaryBalancePatch.LethalOverpump(__instance.variation, __instance.primaryCharge);
        }
        private static void Postfix(bool __state) { if (__state) WeaponBalanceRules.KillPlayer(); }
    }
    [HarmonyPatch(typeof(ShotgunHammer), "DeliverDamage")]
    internal static class HammerEnemyHitOverheatBalancePatch
    {
        internal static void ApplyCooldown(float[] cooldowns, int variant, bool hitLivingEnemy)
        {
            if (WeaponBalanceRules.Active && hitLivingEnemy) cooldowns[variant] = 7.5f;
        }
        private static void Prefix(EnemyIdentifier ___hitEnemy, out bool __state)
        {
            // Capture before damage so a killing blow still counts, but corpses
            // do not. This callback also covers delayed hits completed on unequip.
            __state = WeaponBalanceRules.Active && ___hitEnemy && !___hitEnemy.dead;
        }
        private static void Postfix(ShotgunHammer __instance, bool __state)
        {
            if (__state) ApplyCooldown(MonoSingleton<WeaponCharges>.Instance.shoaltcooldowns, __instance.variation, true);
        }
    }
    [HarmonyPatch(typeof(ShotgunHammer), "OnDisable")]
    internal static class HammerUnequipBalancePatch
    {
        private static void Postfix(ShotgunHammer __instance)
        { var state = __instance.GetComponent<WeaponReloadState>(); if (state) state.RestoreAnimation(); }
    }
    [HarmonyPatch(typeof(ShotgunHammer), "Update")]
    internal static class HammerRecoveryBalancePatch
    {
        internal static float RecoveryStep(float current, float target, float delta) => Mathf.MoveTowards(current, target, WeaponBalanceRules.Active ? delta * .5f : delta);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var c = code[i];
                if (c.operand is MethodInfo m && m.Name == "MoveTowards" && i + 1 < code.Count && code[i + 1].operand is FieldInfo f && f.Name == "hammerCooldown")
                { c.opcode = OpCodes.Call; c.operand = WeaponPatchReflection.Method(typeof(HammerRecoveryBalancePatch), "RecoveryStep"); count++; }
                yield return c;
                if (c.opcode == OpCodes.Ldfld && c.operand is FieldInfo ready && ready.Name == "gunReady")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(HammerRecoveryBalancePatch), "Ready"));
                }
            }
            if (count != 1) throw new InvalidOperationException("Hammer recovery timer changed: " + count);
        }
        internal static bool Ready(bool native, ShotgunHammer gun) => native && (!WeaponBalanceRules.Active || Time.time >= ShotgunBalance.State(gun).SecondaryUntil);
    }
    // Core eject keeps native animation/primary readiness so the ejected core
    // can immediately be struck. Its separate resource recharge stays doubled.
    [HarmonyPatch(typeof(ShotgunHammer), "ShootSaw", new[] { typeof(bool) })]
    internal static class HammerSecondaryRecoveryBalancePatch
    {
        private static void Postfix(ShotgunHammer __instance, MethodBase __originalMethod, object[] __args)
        {
            if (!WeaponBalanceRules.Active) return;
            if (__originalMethod.Name == "ShootSaw" && (bool)__args[0]) return;
            var s = ShotgunBalance.State(__instance);
            var anim = __instance.GetComponentInChildren<Animator>(true);
            if (!anim) return;
            var clip = anim.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == "SawingShot");
            if (!s.Slowed) s.OriginalAnimatorSpeed = anim.speed;
            s.Slowed = true; anim.speed = s.OriginalAnimatorSpeed * .5f;
            s.SecondaryUntil = Time.time + (clip ? clip.length : .2f) / Mathf.Max(.01f, anim.speed);
            s.Until = s.SecondaryUntil;
        }
    }
    [HarmonyPatch]
    internal static class ChainsawDurabilityBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return WeaponPatchReflection.Method(typeof(Shotgun), "Update"); yield return WeaponPatchReflection.Method(typeof(ShotgunHammer), "Update"); }
        internal static int Limit(int value) => WeaponBalanceRules.Active ? (value == 60 ? 9 : 6) : value;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i];
                if ((code[i].LoadsConstant(60) || code[i].LoadsConstant(40)) && i > 0 && code[i - 1].operand is FieldInfo f && f.Name == "chainsawNonlethalHits")
                { yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(ChainsawDurabilityBalancePatch), "Limit")); count++; }
            }
            if (count != 2) throw new InvalidOperationException("Chainsaw durability layout changed: " + count);
        }
    }

    [HarmonyPatch(typeof(Nailgun), "ShootMagnet")]
    internal static class SingleMagnetBalancePatch
    {
        private static bool Prefix()
        { return !WeaponBalanceRules.Active || !MonoSingleton<WeaponCharges>.Instance.magnets.Any(m => m); }
    }
    [HarmonyPatch(typeof(Nailgun), "Update")]
    internal static class NailgunHeatBalancePatch
    {
        private static void Prefix(Nailgun __instance, ref float ___heatSinks)
        { if (WeaponBalanceRules.Active && __instance.variation == 0 && !__instance.altVersion) ___heatSinks = Mathf.Min(1f, ___heatSinks); }
        private static void Postfix(Nailgun __instance, ref float ___heatSinks)
        { Prefix(__instance, ref ___heatSinks); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.LoadsConstant(.55f))
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(NailgunHeatBalancePatch), "HeatRate")); count++; }
            }
            if (count != 1) throw new InvalidOperationException("Nailgun heat rate layout changed: " + count);
        }
        internal static float HeatGain(float value, int variation, bool saw) => WeaponBalanceRules.Active && variation == 0 ? value / 20f : value;
        internal static float HeatRate(float value, Nailgun gun) => !gun.altVersion ? HeatGain(value, gun.variation, false) : value;
    }
    [HarmonyPatch(typeof(Nailgun), "Shoot")]
    internal static class OverheatSawHeatBalancePatch
    {
        internal static float HeatStep(float current, float target, float delta) =>
            Mathf.MoveTowards(current, target, NailgunHeatBalancePatch.HeatGain(delta, 0, true));
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var c = code[i];
                // Native Shoot adds heat per saw only on the Overheat variant.
                if (c.Calls(WeaponPatchReflection.Method(typeof(Mathf), "MoveTowards", new[] { typeof(float), typeof(float), typeof(float) })) &&
                    i + 1 < code.Count && code[i + 1].opcode == OpCodes.Stfld && code[i + 1].operand is FieldInfo field && field.Name == "heatUp")
                {
                    c.operand = WeaponPatchReflection.Method(typeof(OverheatSawHeatBalancePatch), "HeatStep"); count++;
                }
                yield return c;
            }
            if (count != 1) throw new InvalidOperationException("Saw heat gain layout changed: " + count);
        }
    }
    [HarmonyPatch]
    internal static class NailgunFireRateBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Nailgun), "Shoot");
            yield return WeaponPatchReflection.Method(typeof(Nailgun), "FixedUpdate");
            yield return WeaponPatchReflection.Method(typeof(Nailgun), "SuperSaw");
        }
        internal static float CalculateInterval(float value, bool enabled, int variation, bool saw, bool connected)
        {
            if (!enabled) return value;
            float interval = variation == 1 ? value / 2f : variation == 0 && !saw ? value / 3f : value;
            return saw && connected ? interval * 2f : interval;
        }
        internal static float Interval(float value, Nailgun gun) => CalculateInterval(value, WeaponBalanceRules.Active,
            gun.variation, gun.altVersion, WeaponBalanceRules.Active && gun.altVersion && SawConductionRules.Connected);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.opcode == OpCodes.Ldfld && c.operand is FieldInfo f && f.Name == "currentFireRate")
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(NailgunFireRateBalancePatch), "Interval")); count++; }
            }
            if (count == 0) throw new InvalidOperationException("Nailgun shot interval missing");
        }
    }
    internal static class SawConductionRules
    {
        private static readonly HashSet<Zapper> Cables = new HashSet<Zapper>();
        private static readonly FieldInfo Broken = typeof(Zapper).GetField("broken", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static void Track(Zapper cable) => Cables.Add(cable);
        internal static void Remove(Zapper cable) => Cables.Remove(cable);
        internal static bool ConnectionActive(bool attached, bool broken, bool livingTarget) => attached && !broken && livingTarget;
        internal static bool Connected
        {
            get
            {
                Cables.RemoveWhere(c => !c);
                foreach (var cable in Cables)
                {
                    if (!cable.isActiveAndEnabled || !cable.sourceWeapon || !cable.sourceWeapon.GetComponent<Nailgun>()) continue;
                    var target = cable.attachedEnemy;
                    if (ConnectionActive(cable.attached, (bool)Broken.GetValue(cable), target && target.isActiveAndEnabled && !target.dead)) return true;
                }
                return false;
            }
        }
    }
    [HarmonyPatch(typeof(Zapper), "OnEnable")]
    internal static class SawConductionTrackBalancePatch
    {
        private static void Postfix(Zapper __instance) => SawConductionRules.Track(__instance);
    }
    [HarmonyPatch]
    internal static class SawConductionRemoveBalancePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return WeaponPatchReflection.Method(typeof(Zapper), "OnDisable");
            yield return WeaponPatchReflection.Method(typeof(Zapper), "Break");
        }
        private static void Postfix(Zapper __instance) => SawConductionRules.Remove(__instance);
    }
    [HarmonyPatch(typeof(Zapper), "Update")]
    internal static class JumpstartChargeBalancePatch
    {
        private static void Prefix(Zapper __instance)
        { if (WeaponBalanceRules.Active && __instance.attached && WeaponBalanceRules.Underwater) WeaponBalanceRules.KillPlayer(); }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                if (c.Calls(WeaponPatchReflection.Method(typeof(Mathf), "MoveTowards", new[] { typeof(float), typeof(float), typeof(float) })))
                { yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(WeaponBalanceRules), "Half")).MoveLabelsFrom(c); count++; }
                yield return c;
            }
            if (count != 1) throw new InvalidOperationException("Jumpstart charge layout changed: " + count);
        }
    }
    [HarmonyPatch(typeof(Zapper), "Zap")]
    internal static class JumpstartDamageBalancePatch
    {
        private static void Prefix(out bool __state)
        { __state = WeaponBalanceScope.Nailgun; WeaponBalanceScope.Nailgun = true; }
        private static void Finalizer(bool __state) => WeaponBalanceScope.Nailgun = __state;
    }
    [HarmonyPatch(typeof(EnemyIdentifier), "Zap")]
    internal static class ConductedDamageBalancePatch
    {
        private static readonly FieldInfo source = typeof(EnemyIdentifier).GetField("afterShockSourceWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
        private static void Prefix(EnemyIdentifier sourceEid, GameObject sourceWeapon, out bool[] __state)
        {
            __state = new[] { WeaponBalanceScope.CoreOrRocket, WeaponBalanceScope.Nailgun };
            var weapon = sourceWeapon ? sourceWeapon : sourceEid ? (GameObject)source.GetValue(sourceEid) : null;
            if (weapon && weapon.GetComponent<Railcannon>()) WeaponBalanceScope.CoreOrRocket = true;
            if (weapon && weapon.GetComponent<Nailgun>()) WeaponBalanceScope.Nailgun = true;
        }
        private static void Finalizer(bool[] __state)
        { if (__state != null) { WeaponBalanceScope.CoreOrRocket = __state[0]; WeaponBalanceScope.Nailgun = __state[1]; } }
    }
    [HarmonyPatch(typeof(Railcannon), "Shoot")]
    internal static class ElectricRailWaterBalancePatch
    {
        private static void Prefix(Railcannon __instance)
        { if (WeaponBalanceRules.Active && __instance.variation == 0 && WeaponBalanceRules.Underwater) WeaponBalanceRules.KillPlayer(); }
    }
    internal sealed class SlowDrillClock : MonoBehaviour { internal float NextTick; }
    [HarmonyPatch(typeof(Harpoon), "FixedUpdate")]
    internal static class ScrewdriverDurationBalancePatch
    {
        internal static float Interval(int hits) => 15f / Math.Max(1, hits);
        private static bool Prefix(Harpoon __instance, bool ___stopped, bool ___drilling, EnemyIdentifierIdentifier ___target, ref float ___drillCooldown)
        {
            if (!WeaponBalanceRules.Active || !__instance.drill || !__instance.sourceWeapon || !__instance.sourceWeapon.GetComponent<Railcannon>()) return true;
            var clock = __instance.GetComponent<SlowDrillClock>() ?? __instance.gameObject.AddComponent<SlowDrillClock>();
            if (!___stopped || !___drilling || !___target) { clock.NextTick = 0f; return true; }
            float interval = Interval(__instance.drillHits);
            if (clock.NextTick == 0f) clock.NextTick = Time.time + interval;
            if (Time.time < clock.NextTick) return false;
            clock.NextTick += interval;
            // Schedule the authored number of damage ticks across 15 seconds.
            // Halving deltaTime alone would also retain an extra native idle tick.
            ___drillCooldown = 0f;
            return true;
        }
    }
    [HarmonyPatch(typeof(Nailgun), "BurstFire")]
    internal static class OverheatBurstRateBalancePatch
    {
        internal static float Interval(float value) => WeaponBalanceRules.Active ? value / 3f : value;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var c in instructions)
            {
                yield return c;
                if (c.LoadsConstant(.03f))
                { yield return new CodeInstruction(OpCodes.Call, WeaponPatchReflection.Method(typeof(OverheatBurstRateBalancePatch), "Interval")); count++; }
            }
            if (count != 1) throw new InvalidOperationException("Overheat burst interval changed: " + count);
        }
    }
}

