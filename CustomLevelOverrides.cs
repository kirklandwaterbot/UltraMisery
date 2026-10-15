using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace UltraMisery.LevelOverrides
{
    // Runs independently of the selected difficulty. Angry remains optional.
    internal static class Integration
    {
        internal static void Install()
        {
            Rules.InCustomLevel = null;
            if (!Chainloader.PluginInfos.TryGetValue("com.eternalUnion.angryLevelLoader", out var loader)) return;
            try
            {
                var manager = loader.Instance.GetType().Assembly.GetType("AngryLevelLoader.Managers.AngrySceneManager", true);
                var getter = manager.GetProperty("isInCustomLevel", BindingFlags.Public | BindingFlags.Static).GetMethod;
                Rules.InCustomLevel = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
            }
            catch (Exception error) { Plugin.Log.LogError("Custom level override initialization failed: " + error); }
        }
        internal static void BeforeSceneStart()
        {
            if (!Rules.Custom) return;
            foreach (var setter in UnityEngine.Object.FindObjectsOfType<GunSetter>(true)) setter.forcedLoadout = null;
            foreach (var fists in UnityEngine.Object.FindObjectsOfType<FistControl>(true)) fists.forcedLoadout = null;
        }
        internal static void Shutdown() => Rules.InCustomLevel = null;
    }
    internal static class Rules
    {
        internal static Func<bool> InCustomLevel;
        internal static bool Custom => InCustomLevel != null && InCustomLevel();
        internal const string ChristmasLevel = "DavieJay2011.ChristmasGifttotheChristmasUltra-killCommunity.....SohappeytofinallybebackhomeForchristmasGiftswinter ";
        internal static bool BlocksCheatEvent(bool custom, string level, string objectName) =>
            custom && string.Equals(level, ChristmasLevel, StringComparison.Ordinal) &&
            string.Equals(objectName, "Port to RUDE", StringComparison.Ordinal);
    }
    [HarmonyPatch(typeof(PlayerLoadout), "SetLoadout")]
    internal static class MapLoadoutPatch
    {
        private static bool Prefix() => !Rules.Custom;
    }
    [HarmonyPatch(typeof(PlayerLoadoutTarget), "CommitLoadout")]
    internal static class SpawnLoadoutPatch
    {
        private static void Prefix(ref ForcedLoadout loadout)
        {
            // Keep native spawn setup and saved gear selection, discard map rules.
            if (Rules.Custom) loadout = null;
        }
    }
    [HarmonyPatch]
    internal static class SavedGunsPatch
    {
        private static MethodBase[] TargetMethods() => new[] {
            typeof(GunSetter).GetMethod("ResetWeapons", BindingFlags.Instance | BindingFlags.Public),
            typeof(GunSetter).GetMethod("CheckWeapon", BindingFlags.Instance | BindingFlags.NonPublic)
        };
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(GunSetter __instance)
        {
            if (Rules.Custom) __instance.forcedLoadout = null;
        }
    }
    [HarmonyPatch(typeof(FistControl), "ResetFists")]
    internal static class SavedArmsPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(FistControl __instance)
        {
            if (Rules.Custom) __instance.forcedLoadout = null;
        }
    }
    [HarmonyPatch(typeof(GunSetter), "ForceWeapon")]
    internal static class MapWeaponSelectionPatch
    {
        // GunControl.ForceWeapon also handles normal player switching; leave it alone.
        private static bool Prefix() => !Rules.Custom;
    }
    [HarmonyPatch(typeof(OnCheatsEnable), "Update")]
    internal static class ChristmasCheatScreenPatch
    {
        private static bool Prefix(OnCheatsEnable __instance) =>
            !Rules.BlocksCheatEvent(Rules.Custom, SceneHelper.CurrentScene, __instance.gameObject.name);
    }
}
