#if UNITY_EDITOR
using FriendSlop.Core;
using FriendSlop.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FriendSlop.Editor
{
    // Tools menu entry that drops a pair of teleporter pads into the project: one inside
    // the ship interior (sends to the active planet) and one next to the Tier 1 launchpad
    // (sends back to the ship). Idempotent: re-running skips pads that already exist.
    public static class FriendSlopAddTeleporterPads
    {
        private const string ShipInteriorName = "Bigger-On-The-Inside Ship Interior";
        private const string PlanetWrapperName = "Tier 1 Planet";
        private const string PlanetLaunchpadName = "Part Launchpad";

        [MenuItem("Tools/Friend Slop/Add Teleporter Pads")]
        public static void Run()
        {
            var shipResult = TryAddShipPad();
            var planetResult = TryAddPlanetPad();

            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Friend Slop",
                $"Ship pad: {shipResult}\nPlanet pad: {planetResult}\n\n" +
                "Move the pads in the inspector if the placements don't match your level layout.",
                "OK");
        }

        private static string TryAddShipPad()
        {
            var ship = GameObject.Find(ShipInteriorName);
            if (ship == null)
                return $"'{ShipInteriorName}' not found in a loaded scene";

            if (HasPadWithDestination(ship.transform, TeleporterTarget.ActivePlanet))
                return "already present";

            var pad = CreatePadPrimitive("Ship Teleporter Pad", new Color(0.3f, 0.6f, 1f));
            pad.transform.SetParent(ship.transform, false);
            // Local offset from ship origin: a few meters off-center, raised slightly off
            // the floor so the visual doesn't z-fight. The author can move it later.
            pad.transform.localPosition = new Vector3(4f, 0.05f, 0f);
            pad.transform.localRotation = Quaternion.identity;

            ConfigurePad(pad, TeleporterTarget.ActivePlanet);

            var scene = ship.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return "created";
        }

        private static string TryAddPlanetPad()
        {
            var planet = GameObject.Find(PlanetWrapperName);
            if (planet == null)
                return $"'{PlanetWrapperName}' not found in a loaded scene";

            if (HasPadWithDestination(planet.transform, TeleporterTarget.Ship))
                return "already present";

            var launchpad = GameObject.Find(PlanetLaunchpadName);
            if (launchpad == null)
                return $"'{PlanetLaunchpadName}' not found";

            var sphere = SphereWorld.GetClosest(launchpad.transform.position);
            if (sphere == null)
                return "no SphereWorld near the launchpad";

            var pad = CreatePadPrimitive("Ship Return Teleporter Pad", new Color(1f, 0.55f, 0.3f));
            pad.transform.SetParent(planet.transform, true);

            // Tilt the launchpad's surface normal a few degrees so the pad sits next to
            // the launchpad rather than on top of it. Direction of tilt is arbitrary; the
            // tangent-vs-X cross product picks a stable axis on the sphere's tangent plane.
            var center = sphere.Center;
            var launchDir = (launchpad.transform.position - center).normalized;
            var tangent = Vector3.Cross(launchDir, Vector3.right);
            if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.Cross(launchDir, Vector3.forward);
            tangent.Normalize();
            var dir = (Quaternion.AngleAxis(18f, tangent) * launchDir).normalized;
            pad.transform.position = sphere.GetSurfacePoint(dir, 0.12f);
            pad.transform.rotation = sphere.GetSurfaceRotation(dir, Vector3.forward);

            ConfigurePad(pad, TeleporterTarget.Ship);

            var scene = planet.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return "created";
        }

        private static GameObject CreatePadPrimitive(string name, Color color)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = name;
            // Flat puck shape: x/z radius matters, y is thickness.
            pad.transform.localScale = new Vector3(2f, 0.06f, 2f);

            // Keep the cylinder's CapsuleCollider as a solid floor so the player can stand
            // on the pad without sinking. The trigger volume gets added separately below.
            var capsule = pad.GetComponent<Collider>();
            if (capsule != null) capsule.isTrigger = false;

            var trigger = pad.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            // Tall, thin box centered above the pad - catches a player walking onto the
            // pad regardless of their capsule height. Local space: y is up relative to
            // the cylinder's authored axis, so a height of 12 covers any standing player.
            trigger.size = new Vector3(1.6f, 12f, 1.6f);
            trigger.center = new Vector3(0f, 6f, 0f);

            ApplyEmissiveMaterial(pad, color);
            return pad;
        }

        private static void ApplyEmissiveMaterial(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color, name = $"{go.name} Material" };
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.6f);
            }
            renderer.sharedMaterial = mat;
        }

        private static void ConfigurePad(GameObject pad, TeleporterTarget destination)
        {
            var teleporter = pad.AddComponent<TeleporterPad>();
            var so = new SerializedObject(teleporter);
            var prop = so.FindProperty("destination");
            if (prop != null)
            {
                prop.enumValueIndex = (int)destination;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(teleporter);
        }

        private static bool HasPadWithDestination(Transform parent, TeleporterTarget destination)
        {
            var pads = parent.GetComponentsInChildren<TeleporterPad>(true);
            for (var i = 0; i < pads.Length; i++)
                if (pads[i] != null && pads[i].Destination == destination) return true;
            return false;
        }
    }
}
#endif
