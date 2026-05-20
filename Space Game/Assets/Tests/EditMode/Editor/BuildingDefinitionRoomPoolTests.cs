using System;
using System.Linq;
using FriendSlop.Interiors;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Pins the BuildingDefinition.RoomPool compatibility shim's semantics:
    // RoomPool == optionalPool ∪ requiredRooms.Definition (dedup, nulls skipped).
    // The roomPool -> optionalPool rename + FormerlySerializedAs already bit
    // InteriorLayoutGeneratorTests once. A direct contract test on RoomPool
    // catches the next combine-logic regression before it reaches consumers.
    public class BuildingDefinitionRoomPoolTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static RoomDefinition MakeRoom(string name)
        {
            var def = ScriptableObject.CreateInstance<RoomDefinition>();
            def.name = name;
            return def;
        }

        private static BuildingDefinition MakeBuilding(
            RoomDefinition[] optionalPool,
            (RoomDefinition def, int count)[] requiredRooms)
        {
            var bd = ScriptableObject.CreateInstance<BuildingDefinition>();
            var so = new UnityEditor.SerializedObject(bd);

            var opt = so.FindProperty("optionalPool");
            opt.arraySize = optionalPool.Length;
            for (int i = 0; i < optionalPool.Length; i++)
                opt.GetArrayElementAtIndex(i).objectReferenceValue = optionalPool[i];

            var req = so.FindProperty("requiredRooms");
            req.arraySize = requiredRooms.Length;
            for (int i = 0; i < requiredRooms.Length; i++)
            {
                var elem = req.GetArrayElementAtIndex(i);
                elem.FindPropertyRelative("definition").objectReferenceValue = requiredRooms[i].def;
                elem.FindPropertyRelative("count").intValue = Mathf.Max(1, requiredRooms[i].count);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return bd;
        }

        // ── Tests ──────────────────────────────────────────────────────────────

        [Test]
        public void RoomPool_EmptyWhenBothPoolsEmpty()
        {
            var bd = MakeBuilding(
                Array.Empty<RoomDefinition>(),
                Array.Empty<(RoomDefinition, int)>());

            Assert.AreEqual(0, bd.RoomPool.Count);
        }

        [Test]
        public void RoomPool_EqualsOptionalWhenNoRequiredRooms()
        {
            var a = MakeRoom("A");
            var b = MakeRoom("B");
            var bd = MakeBuilding(
                new[] { a, b },
                Array.Empty<(RoomDefinition, int)>());

            CollectionAssert.AreEquivalent(new[] { a, b }, bd.RoomPool);
        }

        [Test]
        public void RoomPool_IncludesRequiredRoomDefinitions()
        {
            var opt = MakeRoom("Optional");
            var req = MakeRoom("Required");
            var bd = MakeBuilding(
                new[] { opt },
                new[] { (req, 1) });

            CollectionAssert.AreEquivalent(new[] { opt, req }, bd.RoomPool);
        }

        [Test]
        public void RoomPool_DeduplicatesRequiredAlsoInOptional()
        {
            var shared = MakeRoom("Shared");
            var other  = MakeRoom("Other");
            var bd = MakeBuilding(
                new[] { shared, other },
                new[] { (shared, 1) });

            Assert.AreEqual(2, bd.RoomPool.Count, "Shared room should appear exactly once");
            CollectionAssert.AreEquivalent(new[] { shared, other }, bd.RoomPool);
        }

        [Test]
        public void RoomPool_IgnoresRequiredRoomsWithNullDefinition()
        {
            var opt = MakeRoom("Optional");
            var bd = MakeBuilding(
                new[] { opt },
                new[] { ((RoomDefinition)null, 1) });

            CollectionAssert.AreEquivalent(new[] { opt }, bd.RoomPool);
        }

        [Test]
        public void RoomPool_PreservesOptionalOrderBeforeRequired()
        {
            var opt1 = MakeRoom("Opt1");
            var opt2 = MakeRoom("Opt2");
            var req  = MakeRoom("Req");
            var bd = MakeBuilding(
                new[] { opt1, opt2 },
                new[] { (req, 1) });

            // Documents the current contract: optional pool first (in declared order),
            // then any required-room definitions not already present.
            CollectionAssert.AreEqual(new[] { opt1, opt2, req }, bd.RoomPool.ToList());
        }

        [Test]
        public void RoomPool_HandlesMultipleRequiredRoomsAndDedupes()
        {
            var opt    = MakeRoom("Optional");
            var reqA   = MakeRoom("ReqA");
            var reqB   = MakeRoom("ReqB");
            var bd = MakeBuilding(
                new[] { opt, reqA },
                new[] { (reqA, 2), (reqB, 1) });

            // reqA shows once (already in optional), reqB shows once (new), opt shows once.
            Assert.AreEqual(3, bd.RoomPool.Count);
            CollectionAssert.AreEquivalent(new[] { opt, reqA, reqB }, bd.RoomPool);
        }
    }
}
