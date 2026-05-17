using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Safety-net: detects when a player falls below the interior floor (most often
    // during a live-edit regenerate, where the room geometry briefly disappears
    // and the player drops through the void) and teleports them back to the entry
    // spawn point. Server-only — uses ServerTeleport which requires authority.
    //
    // Polled in Update on a sub-second cadence with unscaled time so it keeps
    // working while the 3D editor pauses Time.timeScale.
    public partial class InteriorSceneBootstrapper
    {
        private const float RescueDepthMetres = 8f;
        private const float RescuePollSeconds = 0.25f;
        private float _safetyPollTimer;

        private void Update()
        {
            if (!IsServer) return;
            // Don't poll if we don't have a valid interior up yet.
            if (_interiorRoot == null) return;

            _safetyPollTimer -= Time.unscaledDeltaTime;
            if (_safetyPollTimer > 0f) return;
            _safetyPollTimer = RescuePollSeconds;
            RescueAnyFallenPlayers();
        }

        // External hook (called by the block editor on F3-open) so a player who
        // was mid-fall doesn't have to wait for the next poll tick before being
        // teleported somewhere safe.
        public void RescueNow()
        {
            if (!IsServer) return;
            RescueAnyFallenPlayers();
            _safetyPollTimer = RescuePollSeconds;
        }

        // Unconditional: teleport EVERY interior player to a known-safe spot,
        // even if they aren't below the fall threshold. Used when closing the
        // 2D editor, where the player may have been walled-in by geometry
        // rebuilt around them while editing top-down.
        public void ForceTeleportPlayersToSafe()
        {
            if (!IsServer) return;
            var spawn = ComputeSafeRescuePoint();
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned) continue;
                Vector3 toPlayer = player.transform.position - _origin.Value;
                if (toPlayer.sqrMagnitude > 500f * 500f) continue;
                player.ServerTeleport(spawn, Quaternion.identity);
            }
        }

        private void RescueAnyFallenPlayers()
        {
            float floorHeight = CurrentDefinition != null
                ? CurrentDefinition.FloorHeightMeters : 4f;
            // Use the lowest occupied floor index rather than _entryFloor — a
            // building with a basement has its entry above its lowest floor,
            // and a player legitimately standing in the basement shouldn't get
            // teleported just because they're below the entry.
            int lowestFloor = LowestOccupiedFloor(_entryFloor);
            float floorY = _origin.Value.y + lowestFloor * floorHeight;
            float threshold = floorY - RescueDepthMetres;

            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned) continue;
                if (player.transform.position.y >= threshold) continue;

                // Only rescue players notionally inside this building. The interior
                // sits at InteriorWorldOrigin (~y = 2000) which is far above the
                // planet surface, so a player below floorY here is almost
                // certainly one of ours; distance check guards against rescuing
                // players in a separately-loaded interior.
                Vector3 toPlayer = player.transform.position - _origin.Value;
                if (toPlayer.sqrMagnitude > 500f * 500f) continue;

                var spawn = ComputeSafeRescuePoint();
                Debug.LogWarning(
                    $"[Interior] Rescuing player {player.OwnerClientId} from the void at " +
                    $"{player.transform.position} → {spawn}");
                player.ServerTeleport(spawn, Quaternion.identity);
            }
        }

        // Pick a teleport target that actually has something to stand on. For
        // block-mode interiors with at least one Floor block, returns a point
        // 1.2 m above the highest Floor block — so the player lands ON the
        // building, not in it. For the room path (or block buildings with no
        // Floor block yet), falls back to GetSpawnPosition (default spawn).
        // If even that's void, lifts the player to a safe height above origin
        // so they can press F3 and place floor blocks without re-falling.
        private Vector3 ComputeSafeRescuePoint()
        {
            var blockBp = InteriorSessionData.BlockBlueprint;
            if (blockBp != null)
            {
                Vector3Int? bestCell = null;
                foreach (var b in blockBp.Blocks)
                {
                    if (b.Kind != Blocks.BlockKind.Floor) continue;
                    if (bestCell == null || b.Cell.y > bestCell.Value.y) bestCell = b.Cell;
                }
                if (bestCell.HasValue)
                {
                    float c = blockBp.CellMetres;
                    float h = blockBp.WallHeightMetres;
                    var cell = bestCell.Value;
                    return _origin.Value + new Vector3(
                        (cell.x + 0.5f) * c,
                         cell.y * h + 1.2f,
                        (cell.z + 0.5f) * c);
                }
                // Block-mode but no floor tiles authored yet — lift the player
                // well above origin so they don't immediately re-fall. Pressing
                // F3 and dropping a tile gets them back on solid ground.
                return _origin.Value + new Vector3(0f, 50f, 0f);
            }
            return GetSpawnPosition();
        }

        // Scan _roomGoMap (the live room registry) for the lowest GridPosition.y.
        // Falls back to the caller-supplied default when the interior hasn't been
        // built yet (no rooms registered) so we don't hand back int.MaxValue.
        private int LowestOccupiedFloor(int fallback)
        {
            int min = int.MaxValue;
            foreach (var room in _roomGoMap.Keys)
                if (room.GridPosition.y < min) min = room.GridPosition.y;
            return min == int.MaxValue ? fallback : min;
        }
    }
}
