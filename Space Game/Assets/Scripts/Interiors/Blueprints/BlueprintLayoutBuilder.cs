using UnityEngine;

namespace FriendSlop.Interiors.Blueprints
{
    // Converts a BlueprintAsset into the InteriorLayout shape that the existing
    // InteriorSceneBootstrapper consumes. Bypasses the procedural generator —
    // rooms come directly from the blueprint's RoomPlacements, and connections
    // are derived by walking each room's sockets and finding adjacent rooms
    // with matching opposite sockets.
    //
    // Edge overrides ARE applied (Phase 6 done): each connection's open-passage
    // flag is set from the user-authored EdgeState (Wall suppresses the connection,
    // Open marks open passage, Door forces a door). ApplyDoorPolicy isn't called
    // for blueprint layouts — the editor's edge choices are the final word.
    public static class BlueprintLayoutBuilder
    {
        public static InteriorLayout Build(BlueprintAsset blueprint, BuildingDefinition def)
        {
            var layout = new InteriorLayout { Seed = 0 };
            if (blueprint == null) return layout;

            foreach (var p in blueprint.Rooms)
            {
                if (p.Definition == null) continue;
                // Phase 5 — variant picking. Same family + grid size = swappable
                // at spawn time. The blueprint stores one specific def but the
                // building's pool may contain Bathroom_2x2.A / .B / .C; we roll
                // one per spawn for spawn-time variety.
                var variants = RoomVariants.FindVariants(p.Definition,
                    def != null ? def.RoomPool : null);
                var pickedDef = variants[UnityEngine.Random.Range(0, variants.Count)];
                // Per-slot overrides (Phase 4): clone the def and rewrite the
                // overridden fields so the existing furniture pipeline reads them
                // transparently. Skip the clone if no overrides are set.
                var defForSlot = p.HasAnyOverride
                    ? CloneWithOverrides(pickedDef, p)
                    : pickedDef;
                var room = new PlacedRoom(defForSlot, p.GridPosition, p.Rotation);
                bool overlap = false;
                foreach (var cell in room.OccupiedCells())
                {
                    if (layout.Grid.ContainsKey(cell)) { overlap = true; break; }
                }
                if (overlap) continue;
                foreach (var cell in room.OccupiedCells())
                    layout.Grid[cell] = room;
                layout.Rooms.Add(room);
            }

            // Floor count + entry floor — entry sits on the lowest-Y room's floor.
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var r in layout.Rooms)
            {
                if (r.GridPosition.y < minY) minY = r.GridPosition.y;
                if (r.GridPosition.y > maxY) maxY = r.GridPosition.y;
            }
            layout.FloorCount = layout.Rooms.Count == 0 ? 1 : (maxY - minY) + 1;
            layout.EntryFloor = layout.Rooms.Count == 0 ? 0 : minY;

            // Register connections by socket adjacency, applying any per-edge
            // overrides authored in the editor. Default for a blueprint connection
            // is a closed door — Open/Door/Wall overrides flip behaviour:
            //   Wall: skip the connection entirely (room stays walled here)
            //   Open: register as open passage (bootstrapper strips the wall + lintel)
            //   Door: register as a regular door (default behaviour)
            //   Default: same as Door
            foreach (var room in layout.Rooms)
            {
                foreach (var defSocket in room.Definition.Sockets)
                {
                    if (defSocket.IsVertical()) continue;
                    var worldSocket = SocketDirectionExtensions.Rotate(defSocket, room.Rotation);
                    if (room.ConnectedSockets.Contains(worldSocket)) continue;

                    var doorCell = InteriorLayoutGenerator.WorldDoorCellOffset(
                        room.Definition, room.Rotation, worldSocket);
                    int dx = 0, dz = 0;
                    switch (worldSocket)
                    {
                        case SocketDirection.North: dz =  1; break;
                        case SocketDirection.South: dz = -1; break;
                        case SocketDirection.East:  dx =  1; break;
                        case SocketDirection.West:  dx = -1; break;
                    }
                    var doorWorldCell = new Vector3Int(
                        room.GridPosition.x + doorCell.x,
                        room.GridPosition.y,
                        room.GridPosition.z + doorCell.y);
                    var beyond = new Vector3Int(
                        doorWorldCell.x + dx,
                        doorWorldCell.y,
                        doorWorldCell.z + dz);
                    if (!layout.Grid.TryGetValue(beyond, out var neighbor)) continue;
                    if (neighbor == room) continue;

                    var nbrWorldSocket = worldSocket.Opposite();
                    if (neighbor.ConnectedSockets.Contains(nbrWorldSocket)) continue;
                    var nbrDefSocket = SocketDirectionExtensions.Rotate(nbrWorldSocket, -neighbor.Rotation);
                    if (!neighbor.Definition.HasSocket(nbrDefSocket)) continue;

                    var edgeState = blueprint.GetEdgeState(doorWorldCell, beyond);
                    bool isOpenPassage;
                    switch (edgeState)
                    {
                        case EdgeState.Wall: continue;          // suppress the connection
                        case EdgeState.Open: isOpenPassage = true;  break;
                        case EdgeState.Door: isOpenPassage = false; break;
                        default:             isOpenPassage = false; break;
                    }

                    room.ConnectedSockets.Add(worldSocket);
                    neighbor.ConnectedSockets.Add(nbrWorldSocket);
                    layout.Connections.Add(new InteriorLayout.Connection(
                        room, worldSocket, neighbor, nbrWorldSocket, isOpenPassage: isOpenPassage));
                }
            }

            // Pick an exit room: the lowest-Y room with a south-facing world socket.
            // Marks the door used to exit back to the planet.
            foreach (var r in layout.Rooms)
            {
                if (r.GridPosition.y != layout.EntryFloor) continue;
                if (!r.Definition.HasSocket(
                    SocketDirectionExtensions.Rotate(SocketDirection.South, -r.Rotation))) continue;
                var southWorld = SocketDirection.South;
                if (r.ConnectedSockets.Contains(southWorld)) continue;
                r.ConnectedSockets.Add(southWorld);
                layout.ExitRoom   = r;
                layout.ExitSocket = southWorld;
                break;
            }

            return layout;
        }

        // Creates a runtime clone of `original` with the placement's per-slot
        // overrides applied. The clone uses Object.Instantiate which works in
        // build + editor — fields are written via reflection so we don't depend
        // on UnityEditor APIs at runtime. The clone is throwaway: garbage-
        // collected with the layout when the interior scene unloads.
        private static RoomDefinition CloneWithOverrides(RoomDefinition original, RoomPlacement placement)
        {
            var clone = Object.Instantiate(original);
            clone.name = original.name + " (override)";
            var t = typeof(RoomDefinition);
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;

            if (placement.OverrideFurnitureCountRange)
            {
                var f = t.GetField("furnitureCountRange", flags);
                if (f != null) f.SetValue(clone, placement.FurnitureCountRange);
            }
            if (placement.OverrideFurnitureTags)
            {
                var f = t.GetField("furnitureTags", flags);
                if (f != null)
                    f.SetValue(clone, placement.FurnitureTags ?? System.Array.Empty<string>());
            }
            if (placement.OverrideFurnitureRules)
            {
                var f = t.GetField("furnitureRules", flags);
                if (f != null)
                    f.SetValue(clone, placement.FurnitureRules ?? System.Array.Empty<FurnitureRule>());
            }
            return clone;
        }
    }
}
