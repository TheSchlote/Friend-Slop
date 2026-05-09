using UnityEngine;

namespace FriendSlop.Round
{
    public sealed partial class FlatTestWorldEnvironment
    {
        // Procedural rocket-shaped stand-in for the ship exterior. The actual ship is a
        // box of primitives in ShipInterior.unity with no exterior model, so this is a
        // recognizable silhouette built from the same primitives the rest of the project
        // uses. Decorative only - no colliders, players walk through it.
        private void BuildShipDisplay()
        {
            var shipRoot = new GameObject("Ship Display");
            shipRoot.transform.SetParent(transform, worldPositionStays: false);
            shipRoot.transform.localPosition = new Vector3(ShipDisplayOffsetX, 0f, 0f);
            shipRoot.transform.localRotation = Quaternion.identity;

            var hullColor = new Color(0.78f, 0.80f, 0.84f);
            var accentColor = new Color(0.42f, 0.55f, 0.78f);
            var enginePlume = new Color(1f, 0.62f, 0.22f);

            // Engine bell at the base.
            CreateShipPrimitive("Ship Engine Bell", shipRoot.transform,
                PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), Quaternion.identity,
                new Vector3(2.6f, 0.6f, 2.6f), enginePlume, emissive: true);

            // Main body: a tall cylinder that reads as the rocket fuselage.
            CreateShipPrimitive("Ship Body", shipRoot.transform,
                PrimitiveType.Cylinder, new Vector3(0f, 4.4f, 0f), Quaternion.identity,
                new Vector3(2.4f, 3.6f, 2.4f), hullColor, emissive: false);

            // Capsule nose cone tucked just above the body so the silhouette tapers.
            CreateShipPrimitive("Ship Nose", shipRoot.transform,
                PrimitiveType.Capsule, new Vector3(0f, 9.4f, 0f), Quaternion.identity,
                new Vector3(2.0f, 1.6f, 2.0f), hullColor, emissive: false);

            // Fins: four cubes splayed around the base. Slight outward tilt so the
            // silhouette doesn't look like a stack of cylinders.
            for (var i = 0; i < 4; i++)
            {
                var angle = i * 90f;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var finPos = dir * 1.6f + new Vector3(0f, 1.4f, 0f);
                var finRot = Quaternion.Euler(0f, angle, 12f);
                CreateShipPrimitive($"Ship Fin {i + 1}", shipRoot.transform,
                    PrimitiveType.Cube, finPos, finRot,
                    new Vector3(0.18f, 2.0f, 1.6f), accentColor, emissive: false);
            }

            // Cockpit window stripe so the body has a recognizable orientation.
            CreateShipPrimitive("Ship Cockpit Window", shipRoot.transform,
                PrimitiveType.Cube, new Vector3(0f, 7.2f, 1.18f), Quaternion.identity,
                new Vector3(1.4f, 0.5f, 0.18f), accentColor, emissive: true);

            // Floating label above the ship, same style as the showcase entries.
            var labelGo = new GameObject("Ship Label");
            labelGo.transform.SetParent(shipRoot.transform, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, 12f, 0f);
            // Rotated so the readable face points toward the launchpad rather than the
            // showcase. Without this, the label faces +Z which is wrong since the ship
            // sits at -X relative to the launchpad and players approach from +X.
            labelGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var mesh = labelGo.AddComponent<TextMesh>();
            mesh.text = "Ship";
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 80;
            mesh.characterSize = 0.08f;
            mesh.color = new Color(1f, 1f, 1f, 0.95f);
            var meshRenderer = labelGo.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void CreateShipPrimitive(string name, Transform parent,
            PrimitiveType type, Vector3 localPosition, Quaternion localRotation,
            Vector3 localScale, Color color, bool emissive)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            // Decorative - players should walk through, matching the showcase.
            DestroyComponent(go.GetComponent<Collider>());
            ApplyMaterial(go, color, emissive);
        }
    }
}
