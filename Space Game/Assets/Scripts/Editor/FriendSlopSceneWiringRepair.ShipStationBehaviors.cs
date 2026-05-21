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
    // This pass guarantees every existing ShipStation in ShipInterior has
    // a matching ShipStationBehavior sibling. The mapping promotes each
    // legacy enum role to its specialised behavior so the four new fun
    // activities (Mission Vote / Boombox / Dartboard) become visible
    // without authoring new prefabs:
    //
    //   role == Pilot             -> PilotStationBehavior
    //   role == HolographicBoard  -> MissionVoteBehavior
    //   role == MiniGame          -> DartboardStationBehavior (+ "Target" child)
    //   role == Customization     -> BoomboxStationBehavior
    //   role == Utility           -> LegacyOccupancyBehavior
    //
    // Repair is idempotent: if a sibling already exists we leave it
    // alone, preserving any inspector tuning a designer did. The scene is
    // only marked dirty when something actually changed.
    //
    // Backward compat: if a station's role doesn't match the table above
    // (future enum value, corrupted asset) we still drop in
    // LegacyOccupancyBehavior so the station stays usable.
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

                if (EnsureBehaviorFor(station))
                {
                    EditorUtility.SetDirty(station.gameObject);
                    changed = true;
                }
            }

            return changed;
        }

        // Returns true if any state changed on the station's GameObject.
        // Adds the right behavior sibling for the station's enum role, and
        // performs any per-behavior child wiring (notably the dartboard's
        // "Target" child plane).
        private static bool EnsureBehaviorFor(ShipStation station)
        {
            var existing = station.GetComponent<ShipStationBehavior>();
            var changed = false;

            if (existing == null)
            {
                AddBehaviorForRole(station);
                changed = true;
            }

            // The dartboard behavior needs a Transform child named "Target"
            // it raycasts against. The behavior tolerates a missing target
            // (every throw becomes a miss) but a playtester can't tell
            // "the dartboard is real but I'm aiming wrong" from "the
            // dartboard is just unplayable", so we guarantee a target
            // child exists for any dartboard station even if the behavior
            // was already wired.
            if (station.GetComponent<DartboardStationBehavior>() != null)
            {
                if (EnsureDartboardTarget(station))
                    changed = true;
            }

            return changed;
        }

        private static void AddBehaviorForRole(ShipStation station)
        {
            switch (station.Role)
            {
                case ShipStationRole.Pilot:
                    station.gameObject.AddComponent<PilotStationBehavior>();
                    return;
                case ShipStationRole.HolographicBoard:
                    station.gameObject.AddComponent<MissionVoteBehavior>();
                    return;
                case ShipStationRole.MiniGame:
                    station.gameObject.AddComponent<DartboardStationBehavior>();
                    return;
                case ShipStationRole.Customization:
                    // BoomboxStationBehavior [RequireComponent(AudioSource)] —
                    // Unity auto-adds the AudioSource on AddComponent.
                    station.gameObject.AddComponent<BoomboxStationBehavior>();
                    return;
                case ShipStationRole.Utility:
                default:
                    station.gameObject.AddComponent<LegacyOccupancyBehavior>();
                    return;
            }
        }

        // Creates a "Target" child GameObject on the dartboard station if
        // missing. The child's forward axis is the dartboard's face normal
        // (per DartboardStationBehavior.ResolveThrow), so positioning it
        // at local (0, 1.2, 0.1) with identity rotation puts the target
        // plane at chest height, facing the same way as the station — a
        // player standing in front of the station looks straight at it.
        private static bool EnsureDartboardTarget(ShipStation station)
        {
            var existing = station.transform.Find("Target");
            if (existing != null) return false;

            var target = new GameObject("Target");
            target.transform.SetParent(station.transform, worldPositionStays: false);
            target.transform.localPosition = new Vector3(0f, 1.2f, 0.1f);
            target.transform.localRotation = Quaternion.identity;
            EditorUtility.SetDirty(target);
            return true;
        }
    }
}
#endif
