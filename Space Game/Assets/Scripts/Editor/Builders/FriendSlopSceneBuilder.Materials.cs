#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.Editor
{
    // Material library used by the prefab and scene builders.
    // Materials are load-or-create: re-running Repair never churns their GUIDs.
    public static partial class FriendSlopSceneBuilder
    {
        private static Dictionary<string, Material> CreateMaterials()
        {
            var materials = new Dictionary<string, Material>
            {
                ["Concrete"] = CreateMaterial("Concrete", new Color(0.45f, 0.47f, 0.45f)),
                ["DarkWall"] = CreateMaterial("DarkWall", new Color(0.18f, 0.2f, 0.2f)),
                ["SafetyYellow"] = CreateMaterial("SafetyYellow", new Color(0.95f, 0.78f, 0.12f)),
                ["Extraction"] = CreateMaterial("Extraction", new Color(0.1f, 0.8f, 0.35f)),
                ["PlanetGrass"] = CreateMaterial("PlanetGrass", new Color(0.18f, 0.62f, 0.34f)),
                ["PlanetDirt"] = CreateMaterial("PlanetDirt", new Color(0.38f, 0.28f, 0.2f)),
                ["Launchpad"] = CreateMaterial("Launchpad", new Color(0.18f, 0.18f, 0.2f)),
                ["ShipFloor"] = CreateMaterial("ShipFloor", new Color(0.33f, 0.36f, 0.34f)),
                ["ShipWall"] = CreateMaterial("ShipWall", new Color(0.12f, 0.15f, 0.16f)),
                ["Console"] = CreateMaterial("Console", new Color(0.08f, 0.28f, 0.38f)),
                ["Hologram"] = CreateMaterial("Hologram", new Color(0.16f, 0.92f, 0.98f)),
                ["Window"] = CreateMaterial("Window", new Color(0.04f, 0.08f, 0.12f)),
                ["WarningRed"] = CreateMaterial("WarningRed", new Color(0.82f, 0.14f, 0.1f)),
                ["ShipPart"] = CreateMaterial("ShipPart", new Color(0.92f, 0.92f, 0.88f)),
                ["Player"] = CreateMaterial("Player", new Color(0.1f, 0.55f, 0.9f)),
                ["Monster"] = CreateMaterial("Monster", new Color(0.85f, 0.08f, 0.06f)),
                ["LootBlue"] = CreateMaterial("LootBlue", new Color(0.15f, 0.35f, 0.95f)),
                ["LootGreen"] = CreateMaterial("LootGreen", new Color(0.15f, 0.75f, 0.45f)),
                ["LootPink"] = CreateMaterial("LootPink", new Color(0.95f, 0.22f, 0.55f)),
                ["LootMetal"] = CreateMaterial("LootMetal", new Color(0.55f, 0.58f, 0.6f)),
                ["GlowCube"] = CreateMaterial("GlowCube", new Color(0.3f, 1f, 0.95f))
            };

            return materials;
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var path = $"Assets/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            var changed = false;
            if (material.color != color)
            {
                material.color = color;
                changed = true;
            }

            if (name == "GlowCube")
            {
                if (!material.IsKeywordEnabled("_EMISSION"))
                {
                    material.EnableKeyword("_EMISSION");
                    changed = true;
                }

                var emissionColor = color * 1.4f;
                if (material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor") != emissionColor)
                {
                    material.SetColor("_EmissionColor", emissionColor);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(material);
            }

            return material;
        }

        private static void SetMaterial(GameObject gameObject, Material material)
        {
            var renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }
}
#endif
