using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal static class CutsceneAttackLockRules
    {
        private static readonly HashSet<CutsceneSkip> Active = new HashSet<CutsceneSkip>();
        private static readonly HashSet<V2> V2Intros = new HashSet<V2>();
        internal static void Track(V2 boss) { if (boss && boss.inIntro) V2Intros.Add(boss); }
        internal static void Begin(CutsceneSkip cutscene) => Active.Add(cutscene);
        internal static void End(CutsceneSkip cutscene) => Active.Remove(cutscene);
        internal static bool Locked
        {
            get
            {
                if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || MonoSingleton<SandboxNavmesh>.Instance) return false;
                if (!MonoSingleton<NewMovement>.Instance || MonoSingleton<NewMovement>.Instance.dead) return false;
                int scene = SceneManager.GetActiveScene().handle;
                Active.RemoveWhere(item => !item || !item.isActiveAndEnabled || item.gameObject.scene.handle != scene);
                V2Intros.RemoveWhere(item => !item || !item.isActiveAndEnabled || !item.inIntro || item.gameObject.scene.handle != scene);
                return Active.Count != 0 || V2Intros.Count != 0;
            }
        }
        internal static bool IsAttack(string name) => name == "PrimaryFire" || name == "SecondaryFire" || name == "Punch" || name == "Hook";
    }

    [HarmonyPatch(typeof(V2), "Start")]
    internal static class V2IntroAttackLockPatch
    {
        private static void Postfix(V2 __instance) => CutsceneAttackLockRules.Track(__instance);
    }

    [HarmonyPatch(typeof(CutsceneSkip), "OnEnable")]
    internal static class CutsceneAttackLockBeginPatch
    {
        private static void Prefix(CutsceneSkip __instance) => CutsceneAttackLockRules.Begin(__instance);
    }
    [HarmonyPatch(typeof(CutsceneSkip), "OnDisable")]
    internal static class CutsceneAttackLockEndPatch
    {
        private static void Prefix(CutsceneSkip __instance) => CutsceneAttackLockRules.End(__instance);
    }
    [HarmonyPatch(typeof(CutsceneSkip), "LateUpdate")]
    internal static class CutsceneAttackLockSkipPatch
    {
        private static void Prefix(bool ___waitingForInput, out bool __state) => __state = ___waitingForInput;
        private static void Postfix(CutsceneSkip __instance, bool ___waitingForInput, bool __state)
        {
            if (__state && !___waitingForInput) CutsceneAttackLockRules.End(__instance);
        }
    }
    [HarmonyPatch]
    internal static class CutsceneAttackInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "IsPressed", "WasPerformedThisFrame", "WasCanceledThisFrame" })
                yield return AccessTools.PropertyGetter(typeof(InputActionState), name);
        }
        private static void Postfix(InputActionState __instance, ref bool __result)
        {
            if (__result && CutsceneAttackLockRules.IsAttack(__instance.Action.name) && CutsceneAttackLockRules.Locked) __result = false;
        }
    }

    // Also intercept delayed animation callbacks/charged shots which can fire
    // without polling an input that frame. Never skip a whole weapon Update.
    [HarmonyPatch]
    internal static class CutsceneWeaponEmissionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var entry in new[] {
                (typeof(Revolver), new[] { "Shoot", "ThrowCoin", "Punch" }),
                (typeof(Shotgun), new[] { "Shoot", "ShootSinks", "ShootSaw" }),
                (typeof(ShotgunHammer), new[] { "Impact", "ThrowNade", "ShootSaw" }),
                (typeof(Nailgun), new[] { "Shoot", "ShootMagnet", "ShootZapper" }),
                (typeof(Railcannon), new[] { "Shoot" }),
                (typeof(RocketLauncher), new[] { "Shoot", "ShootCannonball", "ShootNapalm" }),
                (typeof(Punch), new[] { "PunchStart" }) })
                foreach (MethodInfo method in entry.Item1.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (System.Array.IndexOf(entry.Item2, method.Name) >= 0) yield return method;
        }
        private static bool Prefix() => !CutsceneAttackLockRules.Locked;
    }
}
