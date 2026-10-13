using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UltraMisery
{
    internal sealed class UltraMiseryDifficultyMarker : MonoBehaviour
    {
    }

    internal static class DifficultySelectionUi
    {
        private const float VisibleHierarchySettleSeconds = 0.75f;

        private static int candidateParentId;
        private static float candidateFirstSeenAt;

        internal static void ResetReconciliation()
        {
            candidateParentId = 0;
            candidateFirstSeenAt = 0f;
        }

        internal static void ReconcileVisibleButton()
        {
            // Menu Cleaner can asynchronously replace the difficulty hierarchy
            // after a scene has already loaded. Reconcile against whichever
            // loaded hierarchy is currently visible instead of binding the
            // button to one sceneLoaded callback.
            UltraMiseryDifficultyMarker visible = FindVisibleButton();
            if (visible)
            {
                DifficultyWheelInput.Ensure(visible.transform);
                ResetReconciliation();
                return;
            }

            // Only active buttons are considered. This makes the periodic check
            // cheap during levels and avoids cloning into Menu Cleaner's hidden
            // source/template hierarchy while the difficulty screen is closed.
            DifficultySelectButton source = FindVisibleSourceButton();
            if (!source || !source.transform.parent)
            {
                ResetReconciliation();
                return;
            }

            int parentId = source.transform.parent.GetInstanceID();
            if (candidateParentId != parentId)
            {
                candidateParentId = parentId;
                candidateFirstSeenAt = Time.unscaledTime;
                return;
            }

            // Give Menu Cleaner's RedoDifficulties coroutine time to finish
            // before inserting. If it replaces the hierarchy later anyway, the
            // next reconciliation pass safely recreates the missing button.
            if (Time.unscaledTime - candidateFirstSeenAt < VisibleHierarchySettleSeconds)
            {
                return;
            }

            RemoveObsoleteHiddenButtons(source.transform.parent);
            GameObject buttonObject = CreateButton(source);
            if (!buttonObject)
            {
                return;
            }

            RebuildContainingLayouts(buttonObject.transform as RectTransform);
            Plugin.Log.LogInfo(
                $"Reconciled visible ULTRAMISERY difficulty button beside {GetHierarchyPath(source.transform)}.");
            ResetReconciliation();
        }

        private static GameObject CreateButton(DifficultySelectButton source)
        {
            GameObject buttonObject = Object.Instantiate(source.gameObject, source.transform.parent);
            buttonObject.SetActive(false);
            buttonObject.name = "UltraMisery";

            DifficultySelectButton button = buttonObject.GetComponent<DifficultySelectButton>();
            if (!button)
            {
                Object.Destroy(buttonObject);
                return null;
            }

            // Preserve unfinished UKMD as the actual game ruleset. The marker,
            // not this shared numeric value, distinguishes UltraMisery from UKMD.
            button.difficulty = (int)GameDifficulty.UKMD;
            buttonObject.AddComponent<UltraMiseryDifficultyMarker>();

            TMP_Text[] labels = buttonObject.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text chosenLabel = null;
            foreach (TMP_Text label in labels)
            {
                if (label.transform.name == "Name")
                {
                    chosenLabel = label;
                    break;
                }
            }

            if (!chosenLabel && labels.Length > 0)
            {
                chosenLabel = labels[0];
            }

            if (chosenLabel)
            {
                chosenLabel.text = "ULTRAMISERY";
            }

            buttonObject.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            RectTransform sourceRect = source.transform as RectTransform;
            RectTransform buttonRect = buttonObject.transform as RectTransform;
            if (sourceRect && buttonRect && !buttonObject.transform.parent.GetComponent<LayoutGroup>())
            {
                float spacing = Mathf.Max(60f, sourceRect.rect.height + 8f);
                buttonRect.anchoredPosition = sourceRect.anchoredPosition + Vector2.down * spacing;
            }

            buttonObject.SetActive(true);
            DifficultyWheelInput.Ensure(buttonObject.transform);
            RebuildContainingLayouts(buttonRect);
            return buttonObject;
        }

        private static UltraMiseryDifficultyMarker FindVisibleButton()
        {
            foreach (UltraMiseryDifficultyMarker marker in Object.FindObjectsOfType<UltraMiseryDifficultyMarker>())
            {
                if (marker && marker.gameObject.activeInHierarchy)
                {
                    return marker;
                }
            }

            return null;
        }

        private static void RemoveObsoleteHiddenButtons(Transform visibleParent)
        {
            foreach (UltraMiseryDifficultyMarker marker in Resources.FindObjectsOfTypeAll<UltraMiseryDifficultyMarker>())
            {
                if (marker && !marker.gameObject.activeInHierarchy && marker.transform.parent != visibleParent)
                {
                    Object.Destroy(marker.gameObject);
                }
            }
        }

        private static DifficultySelectButton FindVisibleSourceButton()
        {
            DifficultySelectButton brutal = null;
            foreach (DifficultySelectButton button in Object.FindObjectsOfType<DifficultySelectButton>())
            {
                if (!button || !button.gameObject.activeInHierarchy ||
                    button.GetComponent<UltraMiseryDifficultyMarker>())
                {
                    continue;
                }

                if (button.difficulty == (int)GameDifficulty.UKMD)
                {
                    return button;
                }

                if (button.difficulty == (int)GameDifficulty.Brutal)
                {
                    brutal = button;
                }
            }

            return brutal;
        }

        private static void RebuildContainingLayouts(RectTransform rect)
        {
            Canvas.ForceUpdateCanvases();
            Transform current = rect;
            for (int depth = 0; current && depth < 8; depth++, current = current.parent)
            {
                RectTransform currentRect = current as RectTransform;
                if (currentRect)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(currentRect);
                }
            }

            Canvas.ForceUpdateCanvases();
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform ? transform.name : "<missing>";
            for (Transform current = transform ? transform.parent : null; current; current = current.parent)
            {
                path = current.name + "/" + path;
            }

            return path;
        }
    }

    [HarmonyPatch(typeof(DifficultySelectButton), "SetDifficulty")]
    internal static class UltraMiseryDifficultySelectionPatch
    {
        private static void Postfix(DifficultySelectButton __instance)
        {
            UltraMiseryState.SetSelected(
                __instance && __instance.GetComponent<UltraMiseryDifficultyMarker>());
        }
    }

    [HarmonyPatch(typeof(DifficultyTitle), "Check")]
    internal static class UltraMiseryDifficultyTitlePatch
    {
        private static void Postfix(DifficultyTitle __instance)
        {
            if (!UltraMiseryState.IsActive || !__instance)
            {
                return;
            }

            TMP_Text title = __instance.GetComponent<TMP_Text>();
            if (title)
            {
                title.text = __instance.lines ? "-- ULTRAMISERY --" : "ULTRAMISERY";
            }
        }
    }
}
