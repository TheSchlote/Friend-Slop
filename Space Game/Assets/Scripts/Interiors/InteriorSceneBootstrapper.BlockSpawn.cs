using FriendSlop.Interiors.Blocks;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Block-blueprint dispatch on the bootstrapper. Called from GenerateAndTeleport
    // before any procedural / room-blueprint logic. When the session data carries
    // a BlockBlueprint, this materialises it and returns true — the caller skips
    // the rest of its pipeline. Otherwise returns false and the room path runs.
    public partial class InteriorSceneBootstrapper
    {
        // True if the session is using the block system. Used by other partials
        // (safety net, regen) to skip room-only logic.
        public bool IsBlockMode => InteriorSessionData.BlockBlueprint != null;

        // Anchor surface consumed by the block editor — exposes the bootstrapper's
        // private interior state without forcing callers to know about partials.
        public Transform InteriorRoomsRoot => _interiorRoot != null ? _interiorRoot.transform : null;
        public Vector3 InteriorOrigin => _origin.Value;
        public BuildingDefinition CurrentDefinition => ResolveDefinition();

        public bool TrySpawnFromBlockBlueprint()
        {
            var bp = InteriorSessionData.BlockBlueprint;
            if (bp == null) return false;
            var def = ResolveDefinition();
            var catalog = def != null ? def.BlockCatalog : null;
            if (catalog == null)
            {
                Debug.LogWarning("[Interior] Block blueprint set but BuildingDefinition has no BlockPrefabCatalog. Run Tools/Friend Slop/Interiors/Repair Block Catalog and wire it on the BuildingDefinition.");
                return false;
            }

            // Reuse _interiorRoot for the block tree so the bootstrapper's
            // existing cleanup (DestroyInterior, scene unload) still owns the
            // teardown without special-casing.
            var root = new GameObject("Interior_Blocks");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.position = _origin.Value;
            root.transform.rotation = Quaternion.identity;
            _interiorRoot = root;
            // Block buildings have no "entry floor" concept beyond floor 0 for
            // now — multi-floor is supported via PageUp/PageDown in the editor
            // but spawn is always at the lowest floor present.
            _entryFloor = LowestFloorInBlueprint(bp);

            int placed = BlockBlueprintMaterializer.Materialize(bp, catalog, root.transform);
            PositionExitDoorForBlocks(bp);
            Debug.Log($"[Interior] Block blueprint '{bp.DisplayName}' materialised: {placed} blocks.");
            return true;
        }

        // The Building_Interior scene ships a single InteriorExitDoor. The room
        // path positions it via PositionExitDoor (which uses BuildingDefinition
        // cell sizes — wrong for blocks). For block mode we drop it on the floor
        // tile nearest grid origin at the entry floor, so the player spawns
        // right beside it.
        private void PositionExitDoorForBlocks(BlockBlueprintAsset bp)
        {
            var all = Object.FindObjectsByType<InteriorExitDoor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length == 0) return;
            var exit = all[0];
            for (int i = 1; i < all.Length; i++)
                if (all[i] != null) Destroy(all[i].gameObject);

            // Pick the floor tile at the entry floor closest to (0,0).
            bool found = false;
            Vector3Int best = Vector3Int.zero;
            int bestDist = int.MaxValue;
            foreach (var b in bp.Blocks)
            {
                if (b.Kind != Blocks.BlockKind.Floor) continue;
                if (b.Cell.y != _entryFloor) continue;
                int d = b.Cell.x * b.Cell.x + b.Cell.z * b.Cell.z;
                if (d < bestDist) { bestDist = d; best = b.Cell; found = true; }
            }
            float c = bp.CellMetres;
            float h = bp.WallHeightMetres;
            Vector3 cellCentre = found
                ? _origin.Value + new Vector3((best.x + 0.5f) * c, best.y * h, (best.z + 0.5f) * c)
                : _origin.Value;
            // Sit it on the SOUTH edge of that tile (slightly inset) so it
            // reads as a doorway against the wall instead of floating mid-room.
            exit.transform.position = cellCentre - new Vector3(0f, 0f, c * 0.5f - 0.15f);
            exit.transform.rotation = Quaternion.identity;
        }

        // Server-only: re-spawn the block layout from the (now-mutated) blueprint.
        // Mirrors RegenerateFromBlueprintFast for the room path so the editor's
        // OnEditApplied callback works uniformly.
        public void RegenerateFromBlockBlueprintFast()
        {
            if (!IsServer) return;
            DestroyInterior();
            TrySpawnFromBlockBlueprint();
        }

        public void RegenerateFromBlockBlueprint()
        {
            // Block buildings don't have furniture / NavMesh complexity yet —
            // the "full" path is identical to the fast path for v1.
            RegenerateFromBlockBlueprintFast();
        }

        private static int LowestFloorInBlueprint(BlockBlueprintAsset bp)
        {
            int min = 0;
            bool first = true;
            foreach (var b in bp.Blocks)
            {
                if (first || b.Cell.y < min) { min = b.Cell.y; first = false; }
            }
            return first ? 0 : min;
        }
    }
}
