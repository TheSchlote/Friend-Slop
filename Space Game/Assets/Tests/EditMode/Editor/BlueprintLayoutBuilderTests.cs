using System.Collections.Generic;
using FriendSlop.Interiors;
using FriendSlop.Interiors.Blueprints;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class BlueprintLayoutBuilderTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static RoomDefinition MakeRoom(string name, Vector2Int size,
            SocketDirection[] sockets, RoomCategory cat = RoomCategory.Generic)
        {
            var def = ScriptableObject.CreateInstance<RoomDefinition>();
            var so  = new UnityEditor.SerializedObject(def);
            so.FindProperty("gridSize").vector2IntValue   = size;
            so.FindProperty("category").enumValueIndex    = (int)cat;
            so.FindProperty("weight").intValue            = 10;
            var arr = so.FindProperty("sockets");
            arr.arraySize = sockets.Length;
            for (int i = 0; i < sockets.Length; i++)
                arr.GetArrayElementAtIndex(i).enumValueIndex = (int)sockets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            def.name = name;
            return def;
        }

        private static BuildingDefinition MakeBuilding(params RoomDefinition[] pool)
        {
            var def = ScriptableObject.CreateInstance<BuildingDefinition>();
            var so  = new UnityEditor.SerializedObject(def);
            var arr = so.FindProperty("optionalPool");
            arr.arraySize = pool.Length;
            for (int i = 0; i < pool.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private static BlueprintAsset MakeBlueprint()
        {
            var bp = ScriptableObject.CreateInstance<BlueprintAsset>();
            // Defensive — Unity ScriptableObject CreateInstance may bypass the
            // field initialiser in some import paths.
            bp.Rooms          ??= new List<RoomPlacement>();
            bp.EdgeOverrides  ??= new List<EdgeOverride>();
            return bp;
        }

        private static void AddPlacement(BlueprintAsset bp, RoomDefinition def,
            Vector3Int gridPos, int rotation = 0)
        {
            bp.Rooms.Add(new RoomPlacement
            {
                Definition   = def,
                GridPosition = gridPos,
                Rotation     = rotation,
            });
        }

        private static void AddEdgeOverride(BlueprintAsset bp, Vector3Int a, Vector3Int b, EdgeState state)
        {
            BlueprintAsset.NormalizePair(ref a, ref b);
            bp.EdgeOverrides.Add(new EdgeOverride { CellA = a, CellB = b, State = state });
        }

        private static readonly SocketDirection[] AllFour =
            { SocketDirection.North, SocketDirection.South, SocketDirection.East, SocketDirection.West };

        // ── Empty / null inputs ────────────────────────────────────────────────

        [Test]
        public void Build_NullBlueprint_ReturnsEmptyLayoutWithIntDefaults()
        {
            // Pins current behaviour: a null blueprint short-circuits before the
            // FloorCount/EntryFloor computation, so FloorCount stays at the int
            // default of 0. The empty-blueprint path below returns FloorCount=1.
            // Callers that may receive a null blueprint must tolerate FloorCount=0.
            var layout = BlueprintLayoutBuilder.Build(null, MakeBuilding());

            Assert.IsNotNull(layout);
            Assert.AreEqual(0, layout.Rooms.Count);
            Assert.AreEqual(0, layout.Grid.Count);
            Assert.AreEqual(0, layout.Connections.Count);
            Assert.AreEqual(0, layout.FloorCount);
            Assert.AreEqual(0, layout.EntryFloor);
            Assert.IsNull(layout.ExitRoom);
        }

        [Test]
        public void Build_EmptyRoomList_ReturnsEmptyLayoutWithFloorCountOne()
        {
            // Asymmetric with the null case above: an empty Rooms list still runs
            // the floor-extents pass, which special-cases Count==0 to FloorCount=1.
            var bp = MakeBlueprint();

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding());

            Assert.AreEqual(0, layout.Rooms.Count);
            Assert.AreEqual(1, layout.FloorCount);
            Assert.AreEqual(0, layout.EntryFloor);
            Assert.IsNull(layout.ExitRoom);
        }

        // ── Placement + grid bookkeeping ───────────────────────────────────────

        [Test]
        public void Build_PlacesEveryBlueprintRoom_AndPopulatesGrid()
        {
            var room = MakeRoom("R", Vector2Int.one, AllFour);
            var bp   = MakeBlueprint();
            AddPlacement(bp, room, new Vector3Int(0, 0, 0));
            AddPlacement(bp, room, new Vector3Int(1, 0, 0));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(room));

            Assert.AreEqual(2, layout.Rooms.Count);
            Assert.AreEqual(2, layout.Grid.Count);
            Assert.IsTrue(layout.Grid.ContainsKey(new Vector3Int(0, 0, 0)));
            Assert.IsTrue(layout.Grid.ContainsKey(new Vector3Int(1, 0, 0)));
        }

        [Test]
        public void Build_OverlappingPlacement_IsSkipped()
        {
            var room = MakeRoom("R", Vector2Int.one, AllFour);
            var bp   = MakeBlueprint();
            AddPlacement(bp, room, new Vector3Int(0, 0, 0));
            AddPlacement(bp, room, new Vector3Int(0, 0, 0));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(room));

            Assert.AreEqual(1, layout.Rooms.Count, "Second overlapping placement should be dropped.");
            Assert.AreEqual(1, layout.Grid.Count);
        }

        [Test]
        public void Build_FloorCountAndEntryFloor_AreDerivedFromYExtents()
        {
            var room = MakeRoom("R", Vector2Int.one, AllFour);
            var bp   = MakeBlueprint();
            AddPlacement(bp, room, new Vector3Int(0, 0, 0));
            AddPlacement(bp, room, new Vector3Int(0, 2, 0));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(room));

            Assert.AreEqual(3, layout.FloorCount, "FloorCount = (maxY - minY) + 1.");
            Assert.AreEqual(0, layout.EntryFloor, "EntryFloor = lowest placed Y.");
        }

        // ── Socket adjacency / connections ────────────────────────────────────

        [Test]
        public void Build_AdjacentRoomsWithMatchingSockets_RegisterAConnection()
        {
            var roomA = MakeRoom("A", Vector2Int.one, new[] { SocketDirection.North });
            var roomB = MakeRoom("B", Vector2Int.one, new[] { SocketDirection.South });
            var bp    = MakeBlueprint();
            AddPlacement(bp, roomA, new Vector3Int(0, 0, 0));
            AddPlacement(bp, roomB, new Vector3Int(0, 0, 1));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(roomA, roomB));

            Assert.AreEqual(1, layout.Connections.Count,
                "Two rooms with matching opposite sockets should produce one connection.");
            var conn = layout.Connections[0];
            Assert.AreEqual(SocketDirection.North, conn.SocketA);
            Assert.AreEqual(SocketDirection.South, conn.SocketB);
            Assert.IsFalse(conn.IsOpenPassage,
                "Default blueprint edge should be a closed door, not an open passage.");
        }

        [Test]
        public void Build_AdjacentRoomsWithoutMatchingSocket_RegisterNoConnection()
        {
            // Room A has North socket but neighbour to the north has no South socket.
            var roomA = MakeRoom("A", Vector2Int.one, new[] { SocketDirection.North });
            var roomB = MakeRoom("B", Vector2Int.one, new[] { SocketDirection.North });
            var bp    = MakeBlueprint();
            AddPlacement(bp, roomA, new Vector3Int(0, 0, 0));
            AddPlacement(bp, roomB, new Vector3Int(0, 0, 1));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(roomA, roomB));

            Assert.AreEqual(0, layout.Connections.Count);
        }

        // ── Edge overrides ────────────────────────────────────────────────────

        [Test]
        public void Build_EdgeOverrideWall_SuppressesConnection()
        {
            var roomA = MakeRoom("A", Vector2Int.one, new[] { SocketDirection.North });
            var roomB = MakeRoom("B", Vector2Int.one, new[] { SocketDirection.South });
            var bp    = MakeBlueprint();
            AddPlacement(bp, roomA, new Vector3Int(0, 0, 0));
            AddPlacement(bp, roomB, new Vector3Int(0, 0, 1));
            AddEdgeOverride(bp, new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), EdgeState.Wall);

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(roomA, roomB));

            Assert.AreEqual(0, layout.Connections.Count, "Wall override must suppress the connection.");
        }

        [Test]
        public void Build_EdgeOverrideOpen_MarksConnectionAsOpenPassage()
        {
            var roomA = MakeRoom("A", Vector2Int.one, new[] { SocketDirection.North });
            var roomB = MakeRoom("B", Vector2Int.one, new[] { SocketDirection.South });
            var bp    = MakeBlueprint();
            AddPlacement(bp, roomA, new Vector3Int(0, 0, 0));
            AddPlacement(bp, roomB, new Vector3Int(0, 0, 1));
            AddEdgeOverride(bp, new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), EdgeState.Open);

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(roomA, roomB));

            Assert.AreEqual(1, layout.Connections.Count);
            Assert.IsTrue(layout.Connections[0].IsOpenPassage,
                "Open override must mark the connection as an open passage.");
        }

        [Test]
        public void Build_EdgeOverrideDoor_MarksConnectionAsClosedDoor()
        {
            var roomA = MakeRoom("A", Vector2Int.one, new[] { SocketDirection.North });
            var roomB = MakeRoom("B", Vector2Int.one, new[] { SocketDirection.South });
            var bp    = MakeBlueprint();
            AddPlacement(bp, roomA, new Vector3Int(0, 0, 0));
            AddPlacement(bp, roomB, new Vector3Int(0, 0, 1));
            AddEdgeOverride(bp, new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1), EdgeState.Door);

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(roomA, roomB));

            Assert.AreEqual(1, layout.Connections.Count);
            Assert.IsFalse(layout.Connections[0].IsOpenPassage,
                "Door override must mark the connection as a closed door.");
        }

        // ── Variant picking ───────────────────────────────────────────────────

        [Test]
        public void Build_PicksVariantFromBuildingPool_BothVariantsReachable()
        {
            // Same family (suffix stripped) + same grid size = swappable variants.
            var defA = MakeRoom("Bathroom_2x2.A", new Vector2Int(2, 2), AllFour);
            var defB = MakeRoom("Bathroom_2x2.B", new Vector2Int(2, 2), AllFour);
            var building = MakeBuilding(defA, defB);

            bool sawA = false, sawB = false;
            for (int seed = 0; seed < 64 && !(sawA && sawB); seed++)
            {
                Random.InitState(seed);
                var bp = MakeBlueprint();
                AddPlacement(bp, defA, Vector3Int.zero);
                var layout = BlueprintLayoutBuilder.Build(bp, building);
                var picked = layout.Rooms[0].Definition;
                if (picked == defA) sawA = true;
                else if (picked == defB) sawB = true;
            }

            Assert.IsTrue(sawA, "Variant A must be selectable across seeds.");
            Assert.IsTrue(sawB, "Variant B must be selectable across seeds.");
        }

        [Test]
        public void Build_NoVariantInPool_FallsBackToOriginalDef()
        {
            var def = MakeRoom("Solo_2x2", new Vector2Int(2, 2), AllFour);
            // Building's pool has *no* matching family.
            var unrelated = MakeRoom("Other_2x2", new Vector2Int(2, 2), AllFour);
            var building  = MakeBuilding(unrelated);

            var bp = MakeBlueprint();
            AddPlacement(bp, def, Vector3Int.zero);

            var layout = BlueprintLayoutBuilder.Build(bp, building);

            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.AreSame(def, layout.Rooms[0].Definition,
                "When no variants exist, the blueprint's stored def must be used directly.");
        }

        // ── Per-slot overrides (Phase 4) ──────────────────────────────────────

        [Test]
        public void Build_NoPerSlotOverride_UsesOriginalDefWithoutCloning()
        {
            var def = MakeRoom("R", Vector2Int.one, AllFour);
            var bp  = MakeBlueprint();
            AddPlacement(bp, def, Vector3Int.zero);

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(def));

            Assert.AreSame(def, layout.Rooms[0].Definition,
                "With no overrides, BlueprintLayoutBuilder must NOT clone the def.");
        }

        [Test]
        public void Build_FurnitureCountRangeOverride_AppliesToClone()
        {
            var def = MakeRoom("R", Vector2Int.one, AllFour);
            var bp  = MakeBlueprint();
            bp.Rooms.Add(new RoomPlacement
            {
                Definition                  = def,
                GridPosition                = Vector3Int.zero,
                Rotation                    = 0,
                OverrideFurnitureCountRange = true,
                FurnitureCountRange         = new Vector2Int(7, 9),
            });

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(def));

            var placedDef = layout.Rooms[0].Definition;
            Assert.AreNotSame(def, placedDef,
                "Override path must produce a clone so the original asset is not mutated.");
            Assert.AreEqual(new Vector2Int(7, 9), placedDef.FurnitureCountRange);
            Assert.AreEqual(new Vector2Int(2, 4), def.FurnitureCountRange,
                "Original RoomDefinition must keep its default range unchanged.");
        }

        // ── Exit-room selection ───────────────────────────────────────────────

        [Test]
        public void Build_MarksLowestSouthFacingRoomAsExit()
        {
            // Two floors. The exit room must be the one on the lowest floor that has
            // a south-facing socket in world space.
            var ground = MakeRoom("Ground", Vector2Int.one, new[] { SocketDirection.South, SocketDirection.North });
            var upper  = MakeRoom("Upper",  Vector2Int.one, new[] { SocketDirection.South });
            var bp     = MakeBlueprint();
            AddPlacement(bp, ground, new Vector3Int(0, 0, 0));
            AddPlacement(bp, upper,  new Vector3Int(0, 1, 0));

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(ground, upper));

            Assert.IsNotNull(layout.ExitRoom);
            Assert.AreEqual(0, layout.ExitRoom.GridPosition.y,
                "Exit must sit on the entry floor (lowest Y).");
            Assert.AreEqual(SocketDirection.South, layout.ExitSocket);
        }

        [Test]
        public void Build_NoSouthFacingRoomOnEntryFloor_LeavesExitNull()
        {
            // Only entry-floor room has no south socket -> no eligible exit.
            var room = MakeRoom("R", Vector2Int.one, new[] { SocketDirection.North });
            var bp   = MakeBlueprint();
            AddPlacement(bp, room, Vector3Int.zero);

            var layout = BlueprintLayoutBuilder.Build(bp, MakeBuilding(room));

            Assert.IsNull(layout.ExitRoom);
            Assert.IsFalse(layout.ExitSocket.HasValue);
        }
    }
}
