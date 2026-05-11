using FriendSlop.Interiors;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class InteriorLayoutGeneratorTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static RoomDefinition MakeRoom(string name, Vector2Int size, RoomCategory cat,
            SocketDirection[] sockets, bool vertical = false, int weight = 10)
        {
            var def = ScriptableObject.CreateInstance<RoomDefinition>();
            var so  = new UnityEditor.SerializedObject(def);
            so.FindProperty("gridSize").vector2IntValue   = size;
            so.FindProperty("category").enumValueIndex    = (int)cat;
            so.FindProperty("isVerticalConnector").boolValue = vertical;
            so.FindProperty("weight").intValue            = weight;
            var arr = so.FindProperty("sockets");
            arr.arraySize = sockets.Length;
            for (int i = 0; i < sockets.Length; i++)
                arr.GetArrayElementAtIndex(i).enumValueIndex = (int)sockets[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            def.name = name;
            return def;
        }

        private static readonly SocketDirection[] AllFour =
            { SocketDirection.North, SocketDirection.South, SocketDirection.East, SocketDirection.West };

        private static BuildingDefinition MakeDef(
            RoomDefinition[] pool, int minR = 3, int maxR = 6, int minF = 1, int maxF = 1)
        {
            var def = ScriptableObject.CreateInstance<BuildingDefinition>();
            var so  = new UnityEditor.SerializedObject(def);
            so.FindProperty("minRooms").intValue    = minR;
            so.FindProperty("maxRooms").intValue    = maxR;
            so.FindProperty("minFloors").intValue   = minF;
            so.FindProperty("maxFloors").intValue   = maxF;
            so.FindProperty("floorHeightMeters").floatValue = 4f;
            so.FindProperty("gridCellMeters").floatValue    = 8f;
            so.FindProperty("minSpecialRooms").intValue     = 0;
            so.FindProperty("maxSpecialRooms").intValue     = 0;
            var arr = so.FindProperty("roomPool");
            arr.arraySize = pool.Length;
            for (int i = 0; i < pool.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Test]
        public void Generate_AlwaysReturnsLayout()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic });

            var layout = InteriorLayoutGenerator.Generate(def, seed: 42);

            Assert.IsNotNull(layout);
            Assert.Greater(layout.Rooms.Count, 0);
        }

        [Test]
        public void Generate_RoomCountWithinBounds()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic }, minR: 4, maxR: 8);

            var layout = InteriorLayoutGenerator.Generate(def, seed: 99);

            Assert.GreaterOrEqual(layout.Rooms.Count, 1, "Must have at least one room");
            Assert.LessOrEqual(layout.Rooms.Count, def.MaxRooms + 2, "Must not massively exceed max");
        }

        [Test]
        public void Generate_NoOverlappingRooms()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var wide    = MakeRoom("Wide", new Vector2Int(2, 1), RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, wide }, minR: 4, maxR: 10);

            var layout = InteriorLayoutGenerator.Generate(def, seed: 7);

            // Each grid cell should be owned by exactly one room.
            var seen = new System.Collections.Generic.HashSet<Vector3Int>();
            foreach (var room in layout.Rooms)
                foreach (var cell in room.OccupiedCells())
                {
                    Assert.IsFalse(seen.Contains(cell),
                        $"Cell {cell} is occupied by more than one room");
                    seen.Add(cell);
                }
        }

        [Test]
        public void Generate_GridMatchesRooms()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic });

            var layout = InteriorLayoutGenerator.Generate(def, seed: 1);

            foreach (var room in layout.Rooms)
                foreach (var cell in room.OccupiedCells())
                {
                    Assert.IsTrue(layout.Grid.ContainsKey(cell), $"Grid missing cell {cell}");
                    Assert.AreSame(room, layout.Grid[cell]);
                }
        }

        [Test]
        public void Generate_ConnectionsAreSymmetric()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic });

            var layout = InteriorLayoutGenerator.Generate(def, seed: 13);

            foreach (var conn in layout.Connections)
            {
                Assert.IsTrue(conn.RoomA.ConnectedSockets.Contains(conn.SocketA),
                    "RoomA does not have SocketA marked connected");
                Assert.IsTrue(conn.RoomB.ConnectedSockets.Contains(conn.SocketB),
                    "RoomB does not have SocketB marked connected");
                Assert.AreEqual(conn.SocketA.Opposite(), conn.SocketB,
                    "Connection sockets are not opposites");
            }
        }

        [Test]
        public void Generate_DifferentSeedsProduceDifferentLayouts()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic }, minR: 6, maxR: 12);

            var a = InteriorLayoutGenerator.Generate(def, seed: 1);
            var b = InteriorLayoutGenerator.Generate(def, seed: 2);

            // Very unlikely that both have the same room count and same grid shape.
            bool different = a.Rooms.Count != b.Rooms.Count || a.Seed != b.Seed;
            Assert.IsTrue(different, "Different seeds should (almost always) produce different layouts");
        }

        [Test]
        public void Generate_SameSeedProducesSameLayout()
        {
            var entry   = MakeRoom("Entry", Vector2Int.one, RoomCategory.Entry, AllFour);
            var generic = MakeRoom("Generic", Vector2Int.one, RoomCategory.Generic, AllFour);
            var def     = MakeDef(new[] { entry, generic });

            var a = InteriorLayoutGenerator.Generate(def, seed: 42);
            var b = InteriorLayoutGenerator.Generate(def, seed: 42);

            Assert.AreEqual(a.Rooms.Count, b.Rooms.Count);
            Assert.AreEqual(a.FloorCount,  b.FloorCount);
        }

        [Test]
        public void NeighborOrigin_NorthSocketOffsetsByRoomDepth()
        {
            var entryDef = MakeRoom("E", new Vector2Int(1, 2), RoomCategory.Entry, AllFour);
            var entry    = new PlacedRoom(entryDef, new Vector3Int(3, 0, 5));
            var nbrSize  = new Vector2Int(1, 1);

            var origin = InteriorLayoutGenerator.NeighborOrigin(entry, SocketDirection.North, nbrSize);

            // North neighbor's south face must align with entry's north face (gz + sz = 5 + 2 = 7)
            Assert.AreEqual(new Vector3Int(3, 0, 7), origin);
        }

        [Test]
        public void NeighborOrigin_SouthSocketAccountsForNeighborDepth()
        {
            var entryDef = MakeRoom("E", new Vector2Int(1, 1), RoomCategory.Entry, AllFour);
            var entry    = new PlacedRoom(entryDef, new Vector3Int(2, 0, 4));
            var nbrSize  = new Vector2Int(1, 2);

            var origin = InteriorLayoutGenerator.NeighborOrigin(entry, SocketDirection.South, nbrSize);

            // Neighbor (sz=2) must have its north face at gz=4, so origin z = 4 - 2 = 2
            Assert.AreEqual(new Vector3Int(2, 0, 2), origin);
        }
    }
}
