using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltraMisery
{
    internal sealed class UltraMiseryMeshStatue : MonoBehaviour
    {
        public EnemyType Type;
        public Renderer Body;
        public Mesh BodyMesh;
        public Renderer[] Variants = new Renderer[0];
        internal bool HasBody => (Body && Body.enabled && Body.gameObject.activeInHierarchy &&
            MeshStatueDiscovery.MeshOf(Body) == BodyMesh) || Variants.Any(r => r && r.enabled && r.gameObject.activeInHierarchy);
        private void OnEnable() => StatueEncounterRules.Register(this);
    }

    internal static class MeshStatueDiscovery
    {
        private sealed class Signature
        {
            internal Mesh Mesh;
            internal EnemyType Type;
        }
        private static readonly List<Signature> Signatures = new List<Signature>();
        private static readonly HashSet<string> Loaded = new HashSet<string>();
        private static bool captureQueued;
        private static readonly Dictionary<(Mesh, Mesh), bool> MeshMatches = new Dictionary<(Mesh, Mesh), bool>();
        private static readonly HashSet<int> Reported = new HashSet<int>();
        private static int reportedScene = -1;
        internal static bool IsCerberusBodyVariant(string meshName) =>
            meshName == "Cerberus" || meshName == "Cerberus_Damaged" || meshName == "Cerberus_Fused";
        internal static bool SameBody(Mesh a, Mesh b)
        {
            if (a == b) return true;
            if (!a || !b) return false;
            var key = (a, b);
            if (MeshMatches.TryGetValue(key, out bool result)) return result;
            result = a.vertexCount == b.vertexCount && a.subMeshCount == b.subMeshCount &&
                a.isReadable && b.isReadable && a.vertices.SequenceEqual(b.vertices) && a.triangles.SequenceEqual(b.triangles);
            MeshMatches[key] = result;
            return result;
        }
        internal static void QueueCapture()
        {
            if (!UltraMiseryState.IsActive || captureQueued || !Plugin.Instance) return;
            captureQueued = true;
            Plugin.Instance.StartCoroutine(CaptureAfterCloning());
        }
        private static IEnumerator CaptureAfterCloning()
        {
            yield return null;
            captureQueued = false;
            Capture(SceneManager.GetActiveScene());
        }
        internal static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter ? filter.sharedMesh : null;
        }
        private static void Load(string name, EnemyType type)
        {
            if (Loaded.Contains(name)) return;
            try
            {
                GameObject prefab = AssetHelper.LoadPrefab("Assets/Prefabs/Levels/Decorations/" + name + ".prefab");
                if (!prefab) return;
                Renderer body = null;
                Mesh mesh = null;
                // Match the principal model, never a lone pedestal or particle.
                foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh candidate = MeshOf(renderer);
                    if (candidate && (!mesh || candidate.vertexCount > mesh.vertexCount)) { body = renderer; mesh = candidate; }
                }
                if (!body || !mesh) return;
                Signatures.Add(new Signature { Mesh = mesh, Type = type });
                if (type == EnemyType.Cerberus)
                    foreach (Renderer variant in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        Mesh variantMesh = MeshOf(variant);
                        // Names select trusted prefab assets only. Scene objects
                        // are still identified by the actual mesh, not their names.
                        if (variantMesh && variantMesh != mesh && IsCerberusBodyVariant(variantMesh.name))
                            Signatures.Add(new Signature { Mesh = variantMesh, Type = type });
                    }
                Loaded.Add(name);
                Plugin.Log.LogInfo($"Statue signature loaded: {name}, body mesh {mesh.name}.");
            }
            catch (Exception error) { Plugin.Log.LogWarning($"Statue signature {name} unavailable: {error.Message}"); }
        }
        internal static bool SameNodeName(string actual, string expected) =>
            actual == expected || actual == expected + "(Clone)" || actual.StartsWith(expected + " (", StringComparison.Ordinal);

        // The decorative glow is a sibling of CerberusStatic, not a child of
        // the body. In baked scenes its mesh is merged, so mesh identity alone
        // cannot recognize it. Never search a room or remove live projectiles.
        internal static bool IsHeldOrbNode(string name) => name == "Sphere_8";
        internal static void RetireHeldOrb(MonoBehaviour decoration)
        {
            var marker = decoration as UltraMiseryMeshStatue;
            if (!marker || marker.Type != EnemyType.Cerberus || !marker.transform.parent) return;
            Transform owner = marker.transform.parent;
            // A shared parent containing several statues is not an ownership
            // boundary; leave ambiguous decorations alone.
            foreach (Transform sibling in owner)
                if (sibling != marker.transform && sibling.GetComponent<UltraMiseryMeshStatue>()) return;
            foreach (Transform child in owner)
            {
                if (!IsHeldOrbNode(child.name) || child == marker.transform ||
                    child.GetComponentInParent<EnemyIdentifier>(true) ||
                    child.GetComponentInChildren<Projectile>(true) ||
                    !child.GetComponent<Renderer>()) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
                Plugin.Log.LogInfo($"Removed converted Cerberus held orb: scene={owner.gameObject.scene.name}, statue={owner.name}, orb={child.name}.");
            }
        }

        internal static void Capture(Scene scene)
        {
            if (!UltraMiseryState.IsActive || SceneHelper.IsPlayingCustom || !scene.IsValid() || !scene.isLoaded ||
                MonoSingleton<SandboxNavmesh>.Instance) return;
            if (reportedScene != scene.handle) { Reported.Clear(); reportedScene = scene.handle; }
            Load("StatuePower", EnemyType.Power);
            Load("StatuePowerOutside", EnemyType.Power);
            Load("Statue", EnemyType.Cerberus);
            Load("StatueOutside", EnemyType.Cerberus);
            Load("MannequinPoser", EnemyType.Mannequin);
            Load("MannequinLight", EnemyType.Mannequin);
            Load("StatueFerryman", EnemyType.Ferryman);
            Load("StatueFerrymanOutside", EnemyType.Ferryman);
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (Renderer renderer in sceneRoot.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is ParticleSystemRenderer || renderer.GetComponentInParent<EnemyIdentifier>(true)) continue;
                    Mesh mesh = MeshOf(renderer);
                    if (!mesh) continue;
                    // Authored animated statues already have an activation path.
                    if (renderer.GetComponentInParent<StatueFake>(true) ||
                        renderer.GetComponentInParent<MannequinPoses>(true)) continue;
                    bool matched = false;
                    foreach (Signature signature in Signatures)
                    {
                        if (!SameBody(signature.Mesh, mesh)) continue;
                        // Own only the matched body, never climb by names into
                        // a shared room parent or convert an empty pedestal.
                        Transform root = renderer.transform;
                        if (signature.Type == EnemyType.Cerberus && root.parent)
                        {
                            // The three alternate bodies share CerberusStatic.
                            // Group only direct sibling body meshes, not rooms.
                            int siblingBodies = 0;
                            foreach (Transform child in root.parent)
                            {
                                Renderer sibling = child.GetComponent<Renderer>();
                                if (sibling && Signatures.Any(s => s.Type == EnemyType.Cerberus && SameBody(s.Mesh, MeshOf(sibling)))) siblingBodies++;
                            }
                            if (siblingBodies > 1) root = root.parent;
                        }
                        var marker = root.GetComponent<UltraMiseryMeshStatue>();
                        if (!marker)
                        {
                            marker = root.gameObject.AddComponent<UltraMiseryMeshStatue>();
                            Plugin.Log.LogInfo($"Recognized decorative {signature.Type} body: scene={scene.name}, object={root.name}, mesh={mesh.name}.");
                        }
                        marker.Type = signature.Type;
                        marker.Body = renderer;
                        marker.BodyMesh = mesh;
                        if (!marker.Variants.Contains(renderer)) marker.Variants = marker.Variants.Concat(new[] { renderer }).ToArray();
                        StatueEncounterRules.Register(marker);
                        matched = true;
                        break;
                    }
                    if (!matched && Reported.Add(renderer.GetInstanceID()))
                    {
                        string path = renderer.name;
                        for (Transform parent = renderer.transform.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
                        string hint = (path + " " + mesh.name).ToLowerInvariant();
                        if (hint.Contains("statue") || hint.Contains("cerber") || hint.Contains("mannequin") ||
                            Signatures.Any(s => s.Mesh.vertexCount == mesh.vertexCount))
                            Plugin.Log.LogWarning($"Unmatched statue candidate: scene={scene.name}, path={path}, mesh={mesh.name}, vertices={mesh.vertexCount}, readable={mesh.isReadable}, staticBatch={renderer.isPartOfStaticBatch}. No body match; left unchanged.");
                    }
                }
        }
    }

    [HarmonyPatch]
    internal static class CheckpointStatueDiscoveryPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "Start", "ResetRoom" })
                yield return typeof(CheckPoint).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        private static void Postfix() => MeshStatueDiscovery.QueueCapture();
    }
}
