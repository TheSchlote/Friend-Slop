using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Spawns the contents of a BlockBlueprintAsset into the scene as child
    // GameObjects under a single root transform. The root is positioned at the
    // building origin (typically InteriorWorldOrigin from InteriorSessionData)
    // and rotated to identity.
    //
    // Cell math (host-local):
    //   Cell (cx, cy, cz) occupies the box from (cx, cy, cz) * (cellMetres,
    //   wallHeightMetres, cellMetres) for one cell-cube. Floor sits at y=0 of
    //   that cube. Walls sit on the cell's edges.
    //
    // Prefab pivot assumption: most Synty / low-poly walls have the pivot at
    // the SW corner of the mesh, extending +X by cellMetres and +Y by
    // wallHeightMetres. Floor prefabs same — pivot at SW corner extending
    // +X +Z by cellMetres. If a pack uses different pivots, tweak the
    // per-kind offsets below.
    public static class BlockBlueprintMaterializer
    {
        // Spawns all blocks under `root`. Returns the count of placed instances.
        public static int Materialize(BlockBlueprintAsset bp, BlockPrefabCatalog catalog,
                                       Transform root, int seed = 0)
        {
            if (bp == null || catalog == null || root == null) return 0;
            float c   = bp.CellMetres;
            float h   = bp.WallHeightMetres;
            int    n  = 0;

            foreach (var b in bp.Blocks)
            {
                Vector3   localPos;
                Quaternion localRot;
                if (!ComputeTransform(b, c, h, out localPos, out localRot))
                    continue;

                var wrapper = new GameObject($"{b.Kind}_{b.Cell.x}_{b.Cell.y}_{b.Cell.z}_{b.Edge}");
                wrapper.transform.SetParent(root, false);
                wrapper.transform.localPosition = localPos;
                wrapper.transform.localRotation = localRot;

                // Walls + Doors are TWO-SIDED: spawn the front side's variant
                // facing the wall's own cell, plus a 180°-flipped instance of
                // the back side's variant facing the cell across the edge.
                // Each side resolves its room (via adjacent floor Label) →
                // RoomStyle, with per-wall Paint overrides winning, and void
                // sides mirror the interior side.
                if (b.Kind == BlockKind.Wall || b.Kind == BlockKind.Door)
                {
                    var frontStyle = ResolveSideStyle(bp, b, front: true,  seed);
                    var backStyle  = ResolveSideStyle(bp, b, front: false, seed);

                    var frontGo = SpawnSidedVisual(catalog, b.Kind, frontStyle,
                                                    wrapper.transform, Quaternion.identity, c, h);
                    SpawnSidedVisual(catalog, b.Kind, backStyle,
                                      wrapper.transform, Quaternion.Euler(0f, 180f, 0f), c, h);

                    if (frontGo != null)
                        AddCollisionProxyFromVisual(wrapper, frontGo, b.Kind, h);
                    n += 2;
                    continue;
                }

                // Single-sided: Floor / Stair / Window / etc.
                var pick = catalog.Pick(b.Kind, b.StyleTag, VariantSeed(bp, b, seed));
                if (pick == null || pick.Prefab == null)
                {
                    if (b.Kind != BlockKind.FloorHole && b.Kind != BlockKind.Ceiling)
                        Debug.LogWarning($"[BlockMaterializer] No catalog variant for {b.Kind} (style '{b.StyleTag}') — skipping cell {b.Cell}.");
                    Object.Destroy(wrapper);
                    continue;
                }
                var go = Object.Instantiate(pick.Prefab, wrapper.transform, false);
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;
                FitVisualToCell(go, b.Kind, c, h);
                AddCollisionProxyFromVisual(wrapper, go, b.Kind, h);
                n++;
            }
            return n;
        }

        // Resolved appearance for one side of a two-sided wall/door.
        private struct SideStyle { public string PrefabName; public int ColorIndex; }

        // Resolve a wall/door side's prefab + colour:
        //   1. Per-wall Paint override (Front/Back) wins.
        //   2. Else the RoomStyle of the floor tile on that side.
        //   3. Else (void / no floor) mirror the OTHER side's room.
        //   4. Else blank (random variant, no tint).
        private static SideStyle ResolveSideStyle(BlockBlueprintAsset bp, BlockEntry b,
                                                   bool front, int seed)
        {
            // 1. Per-wall override.
            if (front && b.OverrideFront)
                return new SideStyle { PrefabName = b.FrontPrefabName, ColorIndex = b.FrontColorIndex };
            if (!front && b.OverrideBack)
                return new SideStyle { PrefabName = b.BackPrefabName,  ColorIndex = b.BackColorIndex };

            // Which cell is this side?
            Vector3Int ownCell = b.Cell;
            Vector3Int acrossCell = b.Cell;
            switch (b.Edge)
            {
                case SocketDirection.North: acrossCell.z += 1; break;
                case SocketDirection.South: acrossCell.z -= 1; break;
                case SocketDirection.East:  acrossCell.x += 1; break;
                case SocketDirection.West:  acrossCell.x -= 1; break;
            }
            var sideCell  = front ? ownCell : acrossCell;
            var otherCell = front ? acrossCell : ownCell;

            string sideLabel  = FloorLabelAt(bp, sideCell);
            string otherLabel = FloorLabelAt(bp, otherCell);
            // 2/3. This side's room, or mirror the other side if void.
            string label = !string.IsNullOrEmpty(sideLabel) ? sideLabel : otherLabel;
            if (string.IsNullOrEmpty(label))
                return new SideStyle { PrefabName = null, ColorIndex = -1 };

            var rs = bp.GetRoomStyle(label);
            return b.Kind == BlockKind.Door
                ? new SideStyle { PrefabName = rs.DoorPrefabName, ColorIndex = rs.DoorColorIndex }
                : new SideStyle { PrefabName = rs.WallPrefabName, ColorIndex = rs.WallColorIndex };
        }

        private static string FloorLabelAt(BlockBlueprintAsset bp, Vector3Int cell)
        {
            foreach (var o in bp.Blocks)
                if (o.Kind == BlockKind.Floor && o.Cell == cell
                    && !string.IsNullOrEmpty(o.Label)) return o.Label;
            return null;
        }

        // Spawn one side's visual under the wall wrapper with an extra local
        // rotation (identity for front, 180° for back), fit it, and tint it.
        private static GameObject SpawnSidedVisual(BlockPrefabCatalog catalog, BlockKind kind,
                                                    SideStyle style, Transform parent,
                                                    Quaternion extraLocalRot, float c, float h)
        {
            var variant = !string.IsNullOrEmpty(style.PrefabName)
                ? catalog.GetByName(kind, style.PrefabName)
                : catalog.Pick(kind, "", 0);
            if (variant == null || variant.Prefab == null) return null;

            var go = Object.Instantiate(variant.Prefab, parent, false);
            go.transform.localRotation = extraLocalRot;
            go.transform.localPosition = Vector3.zero;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            FitVisualToCell(go, kind, c, h);
            if (BlockColorPalette.TryGet(style.ColorIndex, out var tint))
                ApplyTint(go, tint);
            return go;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static void ApplyTint(GameObject go, Color tint)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, tint);
            mpb.SetColor(ColorId,     tint);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                r.SetPropertyBlock(mpb);
        }

        // Scale the prefab uniformly so its world-space bounds fit the cell.
        // Uses Renderer.bounds (world-space AABB, transform-aware) so we don't
        // have to track prefab pivots / scales by hand. Idempotent: bounds shrink
        // proportionally with each scale, so calling this once is enough.
        // Public so the editor ghost can apply the EXACT same scaling the
        // materialiser uses, keeping the preview size-accurate.
        public static void FitVisualToCell(GameObject visual, BlockKind kind,
                                             float cellMetres, float wallHeight)
        {
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;

            // Union of all renderer world-space bounds.
            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);
            var size = combined.size;
            if (size == Vector3.zero) return;

            // Walls / Doors / Windows keep native height + thickness (so window
            // frame textures aren't distorted), but get a small overscale along
            // their LONG horizontal axis so adjacent perpendicular walls
            // overlap slightly at corners — kills the wonky corner gap. ~6%
            // is barely perceptible on a centred window frame.
            if (kind == BlockKind.Wall || kind == BlockKind.Door || kind == BlockKind.Window)
            {
                const float cornerOverlap = 1.06f;
                var wls = visual.transform.localScale;
                bool wxLong = size.x >= size.z;
                visual.transform.localScale = wxLong
                    ? new Vector3(wls.x * cornerOverlap, wls.y, wls.z)
                    : new Vector3(wls.x, wls.y, wls.z * cornerOverlap);
                return;
            }

            // Floors / Stairs / Ceilings ARE scaled (non-uniform) to fill the
            // cell footprint exactly — the user explicitly wants floors resized
            // to match the wall grid so windows stay crisp.
            var ls = visual.transform.localScale;
            float sx = size.x > 0.001f ? cellMetres / size.x : 1f;
            float sz = size.z > 0.001f ? cellMetres / size.z : 1f;
            visual.transform.localScale = new Vector3(ls.x * sx, ls.y, ls.z * sz);
        }

        // Attach a non-trigger BoxCollider to `wrapper` sized to match the
        // visual's actual world bounds — guarantees the collider lines up with
        // what the player sees, regardless of whether the prefab's pivot is
        // SW-corner, centre, or anywhere else.
        //
        // For Floors / Stairs / Ceilings the collider is flattened vertically
        // (a thin slab at the visual's top) so a tall floor mesh doesn't act
        // like a wall.
        // For Walls / Doors / Windows the collider is thinned horizontally
        // (across the wall direction) so the wall blocks crossing but doesn't
        // overflow into the adjacent cells.
        private static void AddCollisionProxyFromVisual(GameObject wrapper, GameObject visual,
                                                         BlockKind kind, float wallHeightMetres)
        {
            const float wallThickness = 0.2f;
            const float floorThickness = 0.2f;
            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) return;
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);

            // Convert world-space bounds → wrapper local space.
            var wt = wrapper.transform;
            Vector3 localCenter = wt.InverseTransformPoint(worldBounds.center);
            Vector3 worldSize   = worldBounds.size;
            // Approximate local size: bounds size is axis-aligned in world, but
            // wrappers are at most rotated 0° or 90° around Y, so swapping X/Z
            // when rotated 90° keeps it accurate. Easier: just take axis-aligned
            // size and pass through, since our wrapper rotations preserve the
            // X/Z plane.
            Vector3 localSize = wt.InverseTransformVector(worldSize);
            // InverseTransformVector preserves sign-and-magnitude — make sure
            // all components are positive.
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

            var box = wrapper.AddComponent<BoxCollider>();
            box.isTrigger = false;
            switch (kind)
            {
                case BlockKind.Floor:
                case BlockKind.Stair:
                case BlockKind.Ceiling:
                    // Thin slab at the top of the visual so the player walks on
                    // the floor surface, not the floor's full vertical mesh.
                    box.size   = new Vector3(localSize.x, floorThickness, localSize.z);
                    box.center = new Vector3(localCenter.x,
                                              localCenter.y + localSize.y * 0.5f - floorThickness * 0.5f,
                                              localCenter.z);
                    break;
                case BlockKind.Wall:
                case BlockKind.Door:
                case BlockKind.Window:
                    // Thin slab along the wall axis (longer of X / Z) with wall
                    // height. Centre on the visual's centroid so the collider
                    // sits exactly where the wall renders.
                    bool xLong = localSize.x >= localSize.z;
                    box.size   = xLong
                        ? new Vector3(localSize.x, wallHeightMetres, wallThickness)
                        : new Vector3(wallThickness, wallHeightMetres, localSize.z);
                    box.center = localCenter;
                    break;
                default:
                    box.center = localCenter;
                    box.size   = Vector3.one * 0.1f;
                    break;
            }
        }

        // Stable hash for "which variant should this cell get?" — shared by the
        // editor ghost and the runtime materialiser so previews match output.
        //
        // Room-aware: tiles within the same room (defined as Floor tiles sharing
        // a Label) get the same variant. Walls / ceilings adopt the Label of
        // the Floor tile they sit on (or adjacent to for edge-mounted walls).
        // Blocks without a resolvable room fall back to a per-cell hash.
        public static int VariantSeed(BlockBlueprintAsset bp, BlockEntry b, int salt = 0)
        {
            int blueprintSeed = bp != null ? bp.VariantSeed : 0;
            string roomKey = ResolveRoomKey(bp, b);
            unchecked
            {
                int h = 17;
                if (!string.IsNullOrEmpty(roomKey))
                {
                    // Room mode: blueprintSeed + roomKey hash. All same-Label
                    // tiles produce the same variant; changing VariantSeed on
                    // the blueprint re-rolls every room.
                    h = h * 31 + blueprintSeed;
                    h = h * 31 + roomKey.GetHashCode();
                    h = h * 31 + (int)b.Kind;       // floor/wall/ceiling differ within a room
                    h = h * 31 + salt;
                }
                else
                {
                    // Fallback: per-cell hash for orphan blocks (e.g. walls on
                    // the boundary of nothing). Stable but not room-grouped.
                    h = h * 31 + blueprintSeed;
                    h = h * 31 + b.Cell.x;
                    h = h * 31 + b.Cell.y;
                    h = h * 31 + b.Cell.z;
                    h = h * 31 + (int)b.Kind;
                    h = h * 31 + (int)b.Edge;
                    h = h * 31 + salt;
                }
                return h;
            }
        }

        // Find the "room key" for a block. For Floor / Stair / FloorHole tiles
        // it's their own Label. For Wall / Door / Window (edge-mounted) it's
        // the Label of the Floor tile at `b.Cell` (the cell the wall is
        // anchored to). For Ceiling: same as Floor at the same cell.
        // Returns null when no room association can be made.
        private static string ResolveRoomKey(BlockBlueprintAsset bp, BlockEntry b)
        {
            if (bp == null) return null;
            if (b.Kind == BlockKind.Floor || b.Kind == BlockKind.Stair || b.Kind == BlockKind.FloorHole)
                return string.IsNullOrEmpty(b.Label) ? null : b.Label;

            // Edge-mounted / ceiling: look up the floor tile sharing this cell.
            foreach (var other in bp.Blocks)
            {
                if (other.Cell != b.Cell) continue;
                if (other.Kind != BlockKind.Floor) continue;
                if (string.IsNullOrEmpty(other.Label)) continue;
                return other.Label;
            }
            // For walls specifically, also try the cell on the OTHER side of
            // the edge — a hallway wall borders two rooms; pick the first one
            // we find a Label for so the wall doesn't fall back to per-cell.
            if (b.Kind == BlockKind.Wall || b.Kind == BlockKind.Door || b.Kind == BlockKind.Window)
            {
                Vector3Int other = b.Cell;
                switch (b.Edge)
                {
                    case SocketDirection.North: other.z += 1; break;
                    case SocketDirection.South: other.z -= 1; break;
                    case SocketDirection.East:  other.x += 1; break;
                    case SocketDirection.West:  other.x -= 1; break;
                }
                foreach (var o in bp.Blocks)
                {
                    if (o.Cell != other) continue;
                    if (o.Kind != BlockKind.Floor) continue;
                    if (string.IsNullOrEmpty(o.Label)) continue;
                    return o.Label;
                }
            }
            return null;
        }

        // World-local placement for a single block. Returns false for FloorHole
        // and Ceiling (no visual in v1).
        //
        // Cell-occupying blocks are placed at the CELL CENTRE so Synty-style
        // centred-pivot prefabs sit flush inside the cell after auto-scale.
        // Edge-mounted blocks are placed at the EDGE MIDPOINT for the same
        // reason — walls extend ±cellMetres/2 from the edge midpoint, and
        // centred-pivot prefabs land in the right spot automatically.
        public static bool ComputeTransform(BlockEntry b, float cellMetres, float wallHeightMetres,
                                             out Vector3 localPos, out Quaternion localRot)
        {
            localPos = Vector3.zero;
            localRot = Quaternion.identity;
            float c = cellMetres;
            float h = wallHeightMetres;
            float bx = b.Cell.x * c;
            float by = b.Cell.y * h;
            float bz = b.Cell.z * c;
            float half = c * 0.5f;
            switch (b.Kind)
            {
                case BlockKind.Floor:
                case BlockKind.Stair:
                    localPos = new Vector3(bx + half, by, bz + half);
                    localRot = Quaternion.Euler(0f, b.Rotation * 90f, 0f);
                    return true;
                case BlockKind.Wall:
                case BlockKind.Door:
                case BlockKind.Window:
                    ComputeEdgeTransform(bx, by, bz, c, b.Edge, b.Rotation,
                                          out localPos, out localRot);
                    return true;
                case BlockKind.FloorHole:
                case BlockKind.Ceiling:
                    return false;
                default:
                    return false;
            }
        }

        // Per-edge midpoint for wall-class blocks. After rotation, the wrapper's
        // local +X is the wall's long axis — auto-scaling shrinks the centred
        // prefab to fit cellMetres along that axis.
        private static void ComputeEdgeTransform(float bx, float by, float bz, float c,
                                                  SocketDirection edge, int rotation,
                                                  out Vector3 pos, out Quaternion rot)
        {
            float half = c * 0.5f;
            // Odd rotation flips the wall 180° so its front face (window frame
            // / trim) points the other way. Even rotation = default facing.
            float flip = (rotation & 1) == 1 ? 180f : 0f;
            switch (edge)
            {
                case SocketDirection.South:
                    pos = new Vector3(bx + half, by, bz);
                    rot = Quaternion.Euler(0f, flip, 0f);
                    break;
                case SocketDirection.North:
                    pos = new Vector3(bx + half, by, bz + c);
                    rot = Quaternion.Euler(0f, flip, 0f);
                    break;
                case SocketDirection.West:
                    pos = new Vector3(bx, by, bz + half);
                    rot = Quaternion.Euler(0f, 90f + flip, 0f);
                    break;
                case SocketDirection.East:
                    pos = new Vector3(bx + c, by, bz + half);
                    rot = Quaternion.Euler(0f, 90f + flip, 0f);
                    break;
                default:
                    pos = new Vector3(bx + half, by, bz + half);
                    rot = Quaternion.Euler(0f, flip, 0f);
                    break;
            }
        }
    }
}
