using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UltraMisery
{
    // Runs after the EventSystem so a normally delivered scroll is never doubled.
    internal sealed class DifficultyWheelInput : MonoBehaviour
    {
        private ScrollRect scroll;
        private Canvas canvas;
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        internal int LastScrollFrame = -1;

        internal static void Ensure(Transform button)
        {
            ScrollRect owner = button.GetComponentInParent<ScrollRect>();
            if (!owner || owner.GetComponent<DifficultyWheelInput>()) return;
            DifficultyWheelInput input = owner.gameObject.AddComponent<DifficultyWheelInput>();
            input.scroll = owner;
            input.canvas = owner.GetComponentInParent<Canvas>();
        }

        private void LateUpdate()
        {
            if (!scroll || !scroll.IsActive() || !scroll.viewport ||
                LastScrollFrame == Time.frameCount || Mouse.current == null || !EventSystem.current)
                return;

            // Input System reports Windows wheel motion in units of 120 per notch;
            // ScrollRect expects the normalized delta used by the UI input module.
            Vector2 delta = Mouse.current.scroll.ReadValue() / 120f;
            if (Mathf.Approximately(delta.y, 0f)) return;
            Vector2 position = Mouse.current.position.ReadValue();
            Camera camera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, position, camera))
                return;

            PointerEventData data = new PointerEventData(EventSystem.current)
            {
                position = position,
                scrollDelta = delta
            };
            hits.Clear();
            EventSystem.current.RaycastAll(data, hits);
            // Respect dialogs covering the menu. Button-level event handlers in
            // this scroll view may swallow scrolling, but outside overlays must not.
            if (hits.Count > 0 && !hits[0].gameObject.transform.IsChildOf(scroll.transform))
                return;

            scroll.OnScroll(data);
        }
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnScroll))]
    internal static class DifficultyWheelDeliveredPatch
    {
        private static void Postfix(ScrollRect __instance)
        {
            DifficultyWheelInput input = __instance.GetComponent<DifficultyWheelInput>();
            if (input) input.LastScrollFrame = Time.frameCount;
        }
    }
}
