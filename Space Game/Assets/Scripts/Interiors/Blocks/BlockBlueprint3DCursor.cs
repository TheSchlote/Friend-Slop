using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Interiors.Blocks
{
    // Per-frame snap-resolver for the block editor. Reads the player's camera
    // ray + the editor's selected BlockKind and produces a target cell or edge.
    //
    // Cell-occupying blocks (Floor/Stair/FloorHole/Ceiling): hit point converts
    // to a grid cell on the current edit floor. The virtual-plane fallback at
    // editFloor*WallHeight lets the designer place tiles in mid-air.
    //
    // Edge-mounted blocks (Wall/Door/Window): nearest of the 4 cell edges to
    // the hit point. Walls live on the boundary between cells.
    public class BlockBlueprint3DCursor
    {
        public struct Result
        {
            public bool             HasTarget;
            public Vector3Int       Cell;            // for cell-occupying kinds
            public SocketDirection  Edge;            // for edge-mounted kinds
            public bool             Conflict;        // already a block at the target
            public int              ExistingIndex;   // index into bp.Blocks, or -1
            public Vector3          WorldHit;
        }

        private readonly System.Func<Transform> _rootGetter;
        private const float MaxRayDistance = 100f;
        private int _editFloor;
        public int EditFloor { get => _editFloor; set => _editFloor = value; }

        // Dynamic root getter — after a regenerate the bootstrapper's
        // _interiorRoot is destroyed and replaced with a fresh GameObject; a
        // captured Transform reference would be dead after the first edit.
        public BlockBlueprint3DCursor(System.Func<Transform> rootGetter)
        {
            _rootGetter = rootGetter;
        }

        public Result Tick(BlockBlueprintAsset bp, BlockKind kind, bool deleteMode = false)
        {
            var result = new Result { ExistingIndex = -1 };
            var cam = LocalPlayerRegistry.CurrentCamera;
            var root = _rootGetter?.Invoke();
            if (cam == null || bp == null || root == null) return result;

            float cell  = bp.CellMetres;
            float floor = bp.WallHeightMetres;
            var ray = new Ray(cam.transform.position, cam.transform.forward);

            // Hit anywhere — real geometry first, then virtual plane at edit
            // floor as a fallback so the first tile of a fresh blueprint has
            // somewhere to land.
            Vector3 worldHit;
            if (Physics.Raycast(ray, out var hitInfo, MaxRayDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                worldHit = hitInfo.point;
            }
            else
            {
                Vector3 planeNormal = root.up;
                Vector3 planePoint  = root.TransformPoint(new Vector3(0f, _editFloor * floor, 0f));
                float denom = Vector3.Dot(planeNormal, ray.direction);
                if (Mathf.Abs(denom) < 1e-5f) return result;
                float t = Vector3.Dot(planeNormal, planePoint - ray.origin) / denom;
                if (t < 0f) return result;
                worldHit = ray.origin + ray.direction * t;
            }

            var local = root.InverseTransformPoint(worldHit);
            int cx = Mathf.FloorToInt(local.x / cell);
            int cz = Mathf.FloorToInt(local.z / cell);
            result.Cell      = new Vector3Int(cx, _editFloor, cz);
            result.WorldHit  = worldHit;
            result.HasTarget = true;

            var nearestEdge = PickNearestEdge(local, cx, cz, cell);
            if (deleteMode)
            {
                // Delete mode targets ANY block under the cursor regardless of
                // the currently-selected kind. Prefer an edge block on the
                // nearest edge (walls read as "in front of you"); fall back to
                // any cell-occupying block in the cell.
                result.Edge = nearestEdge;
                int idx = FindAnyEdgeBlockIndex(bp, result.Cell, nearestEdge);
                if (idx < 0) idx = FindAnyCellBlockIndex(bp, result.Cell);
                result.ExistingIndex = idx;
                result.Conflict      = idx >= 0;
            }
            else if (kind.IsEdgeMounted())
            {
                result.Edge = nearestEdge;
                result.ExistingIndex = FindEdgeBlockIndex(bp, result.Cell, result.Edge);
                result.Conflict      = result.ExistingIndex >= 0;
            }
            else
            {
                result.Edge = SocketDirection.North;   // unused
                result.ExistingIndex = FindCellBlockIndex(bp, result.Cell, kind);
                result.Conflict      = result.ExistingIndex >= 0;
            }
            return result;
        }

        // Pick which of the 4 cell-boundary edges the hit is closest to. relX,
        // relZ are normalised 0..1 inside the hit cell.
        private static SocketDirection PickNearestEdge(Vector3 local, int cx, int cz, float cell)
        {
            float relX = local.x / cell - cx;
            float relZ = local.z / cell - cz;
            float distW = relX;
            float distE = 1f - relX;
            float distS = relZ;
            float distN = 1f - relZ;
            float min = Mathf.Min(Mathf.Min(distW, distE), Mathf.Min(distS, distN));
            if (min == distS) return SocketDirection.South;
            if (min == distN) return SocketDirection.North;
            if (min == distW) return SocketDirection.West;
            return SocketDirection.East;
        }

        // Find an existing block of the same KIND occupying cell (since you
        // can stack Floor + Stair in the same cell with different kinds; the
        // place-conflict check only counts same-kind overlaps).
        public static int FindCellBlockIndex(BlockBlueprintAsset bp, Vector3Int cell, BlockKind kind)
        {
            for (int i = 0; i < bp.Blocks.Count; i++)
            {
                var b = bp.Blocks[i];
                if (b.Cell == cell && b.Kind == kind && !b.Kind.IsEdgeMounted()) return i;
            }
            return -1;
        }

        // Delete-mode helpers: kind-agnostic lookups.
        public static int FindAnyCellBlockIndex(BlockBlueprintAsset bp, Vector3Int cell)
        {
            for (int i = 0; i < bp.Blocks.Count; i++)
            {
                var b = bp.Blocks[i];
                if (b.Cell == cell && !b.Kind.IsEdgeMounted()) return i;
            }
            return -1;
        }

        public static int FindAnyEdgeBlockIndex(BlockBlueprintAsset bp, Vector3Int cell, SocketDirection edge)
        {
            for (int i = 0; i < bp.Blocks.Count; i++)
            {
                var b = bp.Blocks[i];
                if (!b.Kind.IsEdgeMounted()) continue;
                if (b.Cell == cell && b.Edge == edge) return i;
                if (edge == SocketDirection.East  && b.Edge == SocketDirection.West
                    && b.Cell == cell + new Vector3Int(1, 0, 0)) return i;
                if (edge == SocketDirection.West  && b.Edge == SocketDirection.East
                    && b.Cell == cell + new Vector3Int(-1, 0, 0)) return i;
                if (edge == SocketDirection.North && b.Edge == SocketDirection.South
                    && b.Cell == cell + new Vector3Int(0, 0, 1)) return i;
                if (edge == SocketDirection.South && b.Edge == SocketDirection.North
                    && b.Cell == cell + new Vector3Int(0, 0, -1)) return i;
            }
            return -1;
        }

        public static int FindEdgeBlockIndex(BlockBlueprintAsset bp, Vector3Int cell, SocketDirection edge)
        {
            // Normalise: a wall on cell A's east is the same wall as cell B's
            // west when B = A + (1,0,0). Treat them as identical so the user
            // can delete a wall from either side.
            for (int i = 0; i < bp.Blocks.Count; i++)
            {
                var b = bp.Blocks[i];
                if (!b.Kind.IsEdgeMounted()) continue;
                if (b.Cell == cell && b.Edge == edge) return i;
                // Mirror edges:
                if (edge == SocketDirection.East  && b.Edge == SocketDirection.West
                    && b.Cell == cell + new Vector3Int(1, 0, 0)) return i;
                if (edge == SocketDirection.West  && b.Edge == SocketDirection.East
                    && b.Cell == cell + new Vector3Int(-1, 0, 0)) return i;
                if (edge == SocketDirection.North && b.Edge == SocketDirection.South
                    && b.Cell == cell + new Vector3Int(0, 0, 1)) return i;
                if (edge == SocketDirection.South && b.Edge == SocketDirection.North
                    && b.Cell == cell + new Vector3Int(0, 0, -1)) return i;
            }
            return -1;
        }
    }
}
