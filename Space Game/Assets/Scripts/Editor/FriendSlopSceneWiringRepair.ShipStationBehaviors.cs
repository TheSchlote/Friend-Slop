#if UNITY_EDITOR
using FriendSlop.Ship;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Editor
{
    // PR §6 "Scene wiring". Per D-001 nobody hand-edits scene YAML; the
    // canonical way to wire a new component onto a scene GameObject is a
    // repair-tool pass.
    //
    // This pass guarantees every existing ShipStation in ShipInterior has a
    // matching ShipStationBehavior sibling component:
    //
    //   role == Pilot         ->  PilotStationBehavior
    //   anything else         ->  LegacyOccupancyBehavior
    //
    // It does NOT instantiate new GameObjects for MissionVote / Boombox /
    // Dartboard stations — those want fresh GameObjects with collider +
    // visual children that the repair tool can't synthesise from code
    // alone. Authoring those stations is a follow-up PR.
    //
    // Repair is idempotent: if the sibling already exists we leave it
    // alone, preserving any inspector tuning a designer did. The scene is
    // only marked dirty when something actually changed.
    public static partial class FriendSlopSceneWiringRepair
    {
        private static bool EnsureStationBehaviorSiblings(Scene scene)
        {
            if (!scene.IsValid()) return false;

            var stations = Object.FindObjectsByType<ShipStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var changed = false;

            for (var i = 0; i < stations.Length; i++)
            {
                var station = stations[i];
                if (station == null || station.gameObject.scene != scene) continue;

                var existing = station.GetComponent<ShipStationBehavior>();
                if (existing != null) continue;

                if (station.Role == ShipStationRole.Pilot)
                {
                    station.gameObject.AddComponent<PilotStationBehavior>();
                }
                else
                {
                    station.gameObject.AddComponent<LegacyOccupancyBehavior>();
                }

                EditorUtility.SetDirty(station.gameObject);
                changed = true;
            }

            return changed;
        }
    }
}
#endif
