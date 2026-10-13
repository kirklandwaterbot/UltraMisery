using HarmonyLib;
using UnityEngine;

namespace UltraMisery
{
    internal static class StatueSpawnRules
    {
        private static GameObject powerPrefab;
        private static float nextLoad;
        internal static bool SpawnInFront(EnemyType type) => type == EnemyType.Power || type == EnemyType.Ferryman;
        internal static GameObject Prefab(EnemyType type)
        {
            if (type != EnemyType.Power)
                return MonoSingleton<DefaultReferenceManager>.Instance ? MonoSingleton<DefaultReferenceManager>.Instance.GetEnemyPrefab(type) : null;
            if (powerPrefab || Time.unscaledTime < nextLoad) return powerPrefab;
            nextLoad = Time.unscaledTime + 10f;
            try { powerPrefab = AssetHelper.LoadPrefab("Assets/Prefabs/Enemies/PowerWithSpawnEffect.prefab"); }
            catch (System.Exception error) { Plugin.Log.LogWarning("Power statue prefab load failed: " + error.Message); }
            return powerPrefab;
        }

        internal static Vector3 PowerForward(Transform decoration)
        {
            // Imported statue meshes may be pitched 90 degrees. Their full
            // model rotation must not become the combat prefab's world rotation.
            for (Transform node = decoration; node; node = node.parent)
            {
                Vector3 forward = Vector3.ProjectOnPlane(node.forward, Vector3.up);
                if (forward.sqrMagnitude > 0.01f) return forward.normalized;
            }
            return Vector3.forward;
        }

        internal static Quaternion PowerRotation(Transform decoration) =>
            Quaternion.LookRotation(PowerForward(decoration), Vector3.up);

        internal static Vector3 PowerPosition(MonoBehaviour decoration)
        {
            var marker = decoration as UltraMiseryMeshStatue;
            Renderer body = marker ? marker.Body : decoration.GetComponentInChildren<Renderer>();
            Vector3 forward = PowerForward(decoration.transform);
            Bounds bounds = body ? body.bounds : new Bounds(decoration.transform.position, Vector3.one);
            float extent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
            Vector3 point = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + forward * (extent + 2f);
            if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit ground, 12f,
                LayerMaskDefaults.Get(LMD.Environment), QueryTriggerInteraction.Ignore)) point.y = ground.point.y;
            return point;
        }
    }

    [HarmonyPatch(typeof(StatueActivator), "Start")]
    internal static class ConvertedStatueActivatorPatch
    {
        private static bool Prefix(StatueActivator __instance)
        {
            if (!UltraMiseryState.IsActive) return true;
            Transform parent = __instance.transform.parent;
            if (parent && parent.GetComponentInChildren<StatueFake>()) return true;
            // A statue already converted by UltraMisery no longer has StatueFake.
            Object.Destroy(__instance);
            return false;
        }
    }
}
