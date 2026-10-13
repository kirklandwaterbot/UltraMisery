using System;
using System.Collections;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine.UI;

namespace UltraMisery
{
    // Reflection keeps Angry optional and avoids changing its DLL/config files.
    internal static class AngryCompatibility
    {
        private const string Name = "ULTRAMISERY";
        private static Type manager, difficulty, config;
        private static object entry;
        private static FieldInfo ukmdIndex;
        private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal static void Install(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.eternalUnion.angryLevelLoader", out var plugin)) return;
            try
            {
                Assembly assembly = plugin.Instance.GetType().Assembly;
                manager = assembly.GetType("AngryLevelLoader.Managers.AngryDifficultyManager", true);
                difficulty = assembly.GetType("AngryLevelLoader.Managers.AngryDifficulty", true);
                config = assembly.GetType("AngryLevelLoader.Managers.ConfigManager", true);
                harmony.Patch(manager.GetMethod("Init", All), postfix: Hook(nameof(Register)));
                harmony.Patch(difficulty.GetMethod("SetDifficulty", All), postfix: Hook(nameof(Apply)));
                harmony.Patch(difficulty.GetMethod("UnsetDifficulty", All), postfix: Hook(nameof(Unset)));
                harmony.Patch(manager.GetMethod("SetDifficultyFromPrefs", All), prefix: Hook(nameof(FromPrefs)));
                // Do not initialize Angry from our Awake. Its field constructor
                // owns initialization; UKMD Unlocker appends on every Init call.
                GuardUkmdRegistration(harmony);
                if ((bool)manager.GetField("_inited", All).GetValue(null)) Register();
                Plugin.Instance.StartCoroutine(RefreshField());
            }
            catch (Exception error) { Plugin.Log.LogError("Angry integration failed: " + error); }
        }
        private static HarmonyMethod Hook(string method) => new HarmonyMethod(typeof(AngryCompatibility).GetMethod(method, All));
        private static void GuardUkmdRegistration(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.whyis2plus2.UKMDUnlocker", out var plugin)) return;
            Type fix = plugin.Instance.GetType().Assembly.GetType("UKMDUnlocker.Compat.AngryFix");
            MethodInfo append = fix?.GetNestedType("Patches", BindingFlags.NonPublic)?.GetMethod("AddUKMDToDifficultyList", All);
            ukmdIndex = fix?.GetField("ukmdIdx", All);
            if (append == null || ukmdIndex == null)
            {
                Plugin.Log.LogWarning("UKMD Unlocker registration guard unavailable for this version.");
                return;
            }
            harmony.Patch(append, prefix: Hook(nameof(KeepSingleUkmd)));
        }
        internal static int FindNamedEntry(IList list, FieldInfo nameField, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if ((string)nameField.GetValue(list[i]) == name) return i;
            return -1;
        }
        private static bool KeepSingleUkmd()
        {
            IList list = (IList)manager.GetField("difficulties", All).GetValue(null);
            int index = FindNamedEntry(list, difficulty.GetField("name", All), "UKMD");
            if (index < 0) return true;
            // Preserve Unlocker's saved-index contract when suppressing append.
            ukmdIndex.SetValue(null, index);
            return false;
        }
        private static void Register()
        {
            IList list = (IList)manager.GetField("difficulties", All).GetValue(null);
            int existing = FindNamedEntry(list, difficulty.GetField("name", All), Name);
            if (existing >= 0) { entry = list[existing]; return; }
            entry = Activator.CreateInstance(difficulty, Name, (int)GameDifficulty.UKMD);
            list.Add(entry);
            Plugin.Log.LogInfo("Registered ULTRAMISERY in Angry Level Loader difficulty list (UKMD base).");
        }
        private static IEnumerator RefreshField()
        {
            yield return null;
            // Angry's field may already exist before our soft-dependent Awake.
            object field = config.GetField("difficultyField", All).GetValue(null);
            if (field == null) yield break; // Its later constructor uses the new registry.
            object inner = field.GetType().GetField("internalDifficultyField", All).GetValue(field);
            IList values = (IList)inner.GetType().GetField("values", All).GetValue(inner);
            if (!values.Contains(Name)) inner.GetType().GetMethod("AddValue", All).Invoke(inner, new object[] { Name });
            object ui = field.GetType().GetField("currentUi", All).GetValue(field);
            if (ui is UnityEngine.Object unityUi && unityUi)
            {
                var dropdown = ui.GetType().GetField("difficultyList", All).GetValue(ui) as Dropdown;
                if (dropdown && !dropdown.options.Exists(o => o.text == Name))
                    dropdown.AddOptions(new System.Collections.Generic.List<string> { Name });
            }
        }
        private static void Apply(object __instance) => UltraMiseryState.SetSelected(ReferenceEquals(__instance, entry));
        private static void Unset(object __instance)
        {
            if (ReferenceEquals(__instance, entry)) UltraMiseryState.SetSelected(false);
        }
        private static bool FromPrefs()
        {
            if (!UltraMiseryState.IsActive || entry == null) return true;
            manager.GetMethod("ForceSetDifficulty", All).Invoke(null, new[] { entry });
            return false;
        }
    }
}
