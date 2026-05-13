using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Furniture Definition", fileName = "Furniture")]
    public class FurnitureDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [Tooltip("Identifies what this piece IS (e.g. \"bed\", \"toilet\", \"stove\"). " +
                 "Used by RoomDefinition.furnitureRules to enforce minimum counts and maximum caps.")]
        [SerializeField] private string kind = "";
        [Tooltip("Tag set used to match against RoomDefinition.furnitureTags. " +
                 "If any tag overlaps, the furniture is eligible for that room.")]
        [SerializeField] private string[] tags = Array.Empty<string>();
        [Tooltip("Where this piece is allowed to spawn within a room.")]
        [SerializeField] private AnchorPlacement placement = AnchorPlacement.Wall;
        [Tooltip("XZ footprint of the piece (metres). Used by the spawner to check if " +
                 "the piece overlaps a door's swing zone before placing it.")]
        [SerializeField] private Vector2 footprintXZ = new Vector2(1f, 1f);
        [Tooltip("Selection weight when picking from a candidate pool.")]
        [SerializeField, Range(1, 100)] private int weight = 10;
        [Tooltip("If true, this piece supports gameplay interactions (loot containers, " +
                 "sittable, etc.). Wired up in a later phase.")]
        [SerializeField] private bool interactable;
        [Tooltip("Real-art override. If assigned, the runtime instantiates this prefab " +
                 "instead of building from primitives. Null = build from `primitives`.")]
        [SerializeField] private GameObject prefab;
        [Tooltip("Modular primitives used when `prefab` is null. Each entry describes a " +
                 "single cube/cylinder/sphere placed under the spawned furniture root.")]
        [SerializeField] private PrimitiveBox[] primitives = Array.Empty<PrimitiveBox>();
        [Tooltip("Spots ON TOP of this piece where smaller tabletop-tagged items can spawn. " +
                 "Empty for non-table pieces. A Vase or Lamp at one of these positions sits on " +
                 "the piece's surface.")]
        [SerializeField] private TabletopAnchor[] tabletopAnchors = Array.Empty<TabletopAnchor>();
        [Tooltip("Floor-level slots AROUND this piece where chairs (or similar) face inward " +
                 "toward the table. Each anchor carries its own yaw so the chair points at the " +
                 "table from whichever side it sits on.")]
        [SerializeField] private AroundTableAnchor[] aroundTableAnchors = Array.Empty<AroundTableAnchor>();

        public string DisplayName        => displayName;
        public string Kind                => kind ?? "";
        public IReadOnlyList<string> Tags => tags;
        public AnchorPlacement Placement => placement;
        public Vector2 FootprintXZ       => footprintXZ;
        public int Weight                => weight;
        public bool Interactable         => interactable;
        public GameObject Prefab         => prefab;
        public IReadOnlyList<PrimitiveBox> Primitives => primitives;
        public IReadOnlyList<TabletopAnchor> TabletopAnchors => tabletopAnchors;
        public IReadOnlyList<AroundTableAnchor> AroundTableAnchors => aroundTableAnchors;

        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            foreach (var t in tags)
                if (t == tag) return true;
            return false;
        }
    }

    // A spot on top of a piece of furniture where a tabletop-tagged item can spawn.
    // localPosition is the centre of the surface area; footprintXZ caps the size of
    // items that fit (a desk-top might allow 0.6 × 0.6 m items but not bigger).
    [Serializable]
    public struct TabletopAnchor
    {
        public Vector3 localPosition;
        public Vector2 footprintXZ;
    }

    // A floor-level slot around a piece of furniture, used for chairs around tables.
    // Same fields as TabletopAnchor plus a Y-rotation so the spawned chair faces the
    // table regardless of which side it's on.
    [Serializable]
    public struct AroundTableAnchor
    {
        public Vector3 localPosition;
        public Vector2 footprintXZ;
        public float yawDegrees;
    }

    [Serializable]
    public struct PrimitiveBox
    {
        public PrimitiveType shape;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector3 localEulerAngles;
        public Color   tint;

        public PrimitiveBox(PrimitiveType shape, Vector3 pos, Vector3 scale, Color tint)
        {
            this.shape = shape;
            this.localPosition = pos;
            this.localScale = scale;
            this.localEulerAngles = Vector3.zero;
            this.tint = tint;
        }
    }
}
