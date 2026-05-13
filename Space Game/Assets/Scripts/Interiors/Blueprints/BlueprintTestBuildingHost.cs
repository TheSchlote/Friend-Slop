using UnityEngine;

namespace FriendSlop.Interiors.Blueprints
{
    // Permanent in-scene anchor for the blueprint test building. Sits at a fixed
    // location near the launchpad in Hills and Valleys (and any other test scene
    // you choose), serialised into the scene file so it survives play-mode exit
    // and never depends on runtime spawn timing.
    //
    // The blueprint editor's RefreshTestBuilding looks for this host before
    // creating its own root — if a host exists, it routes children there and
    // the host's transform / parent / position are honoured exactly.
    public class BlueprintTestBuildingHost : MonoBehaviour
    {
        [Tooltip("Blueprint that gets materialised into this host's children at " +
                 "runtime. The blueprint editor overrides this when the user " +
                 "switches blueprints during play mode.")]
        public BlueprintAsset blueprint;
        [Tooltip("Cell metres used when laying out the rooms. Should match the " +
                 "BuildingDefinition the blueprint is intended for.")]
        public float cellMetres = 3.4f;
        [Tooltip("Floor height in metres (Y per grid level).")]
        public float floorHeightMeters = 4f;
        [Tooltip("Tint applied to every renderer in the spawned rooms so the test " +
                 "building reads as distinctly the blueprint preview, not a real " +
                 "interior. Set alpha to 0 to disable tinting.")]
        public Color tintColor = new Color(0.30f, 0.55f, 1f, 1f);
        [Tooltip("If true, on each Refresh the host re-positions itself 30 m to the " +
                 "right of the launchpad (snapped to the sphere surface). Disable " +
                 "if you've manually placed the host somewhere specific.")]
        public bool autoSnapToLaunchpad = true;
        [Tooltip("Distance in metres along the launchpad's right axis to place the " +
                 "host before snapping to the sphere surface.")]
        public float launchpadOffset = 30f;

        // Container for the spawned rooms. Cleared and refilled on each Refresh.
        private Transform _spawnedRoot;

        private void OnEnable()
        {
            if (autoSnapToLaunchpad && Application.isPlaying)
            {
                // Defer until the launchpad has spawned; the network bootstrapper
                // creates it at runtime. Try for up to ~5 seconds before giving up
                // and refreshing in place (which leaves the building at origin if
                // it never appears).
                StartCoroutine(WaitForLaunchpadThenRefresh());
            }
            else
            {
                Refresh();
            }
        }

        private System.Collections.IEnumerator WaitForLaunchpadThenRefresh()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var launchpadRoot = GameObject.Find("Launchpad Assembly Site");
                var sphereWorld   = FindFirstObjectByType<FriendSlop.Core.SphereWorld>();
                if (launchpadRoot != null && sphereWorld != null) break;
                yield return null;
            }
            Refresh();
        }

        public void Refresh()
        {
            // Auto-snap to launchpad surface position. The launchpad is a runtime
            // spawn (not in the saved scene file), so the setup menu's edit-time
            // position usually ends up at world origin — inside the planet.
            // Re-snapping at runtime fixes that.
            if (autoSnapToLaunchpad)
            {
                var launchpadRoot = GameObject.Find("Launchpad Assembly Site");
                var sphereWorld   = FindFirstObjectByType<FriendSlop.Core.SphereWorld>();
                if (launchpadRoot != null && sphereWorld != null)
                {
                    Vector3 offset = launchpadRoot.transform.position
                                   + launchpadRoot.transform.right * launchpadOffset;
                    Vector3 fromCentre = (offset - sphereWorld.Center).normalized;
                    transform.position = sphereWorld.Center + fromCentre * sphereWorld.Radius;
                    transform.rotation = Quaternion.FromToRotation(Vector3.up, fromCentre);
                }
            }

            // Tear down the old children if any.
            if (_spawnedRoot != null)
            {
                if (Application.isPlaying) Destroy(_spawnedRoot.gameObject);
                else                       DestroyImmediate(_spawnedRoot.gameObject);
                _spawnedRoot = null;
            }
            if (blueprint == null) return;

            var root = new GameObject("Rooms").transform;
            root.SetParent(transform, false);
            _spawnedRoot = root;

            int spawned = 0;
            foreach (var room in blueprint.Rooms)
            {
                if (room.Definition == null || room.Definition.Prefab == null) continue;
                var size = room.Definition.GridSize;
                Vector3 localPos = new Vector3(
                    room.GridPosition.x * cellMetres,
                    room.GridPosition.y * floorHeightMeters,
                    room.GridPosition.z * cellMetres);
                // Rotation around the prefab's SW corner pivot shifts the room out
                // of the +X+Z quadrant. Compensate so the rotated room's SW corner
                // stays at its grid-position local coords.
                Vector3 rotShift = (room.Rotation & 3) switch
                {
                    1 => new Vector3(0f,                       0f, size.x * cellMetres),
                    2 => new Vector3(size.x * cellMetres, 0f, size.y * cellMetres),
                    3 => new Vector3(size.y * cellMetres, 0f, 0f),
                    _ => Vector3.zero,
                };
                Quaternion localRot = Quaternion.Euler(0f, room.Rotation * 90f, 0f);
                var go = Instantiate(room.Definition.Prefab, root);
                go.transform.localPosition = localPos + rotShift;
                go.transform.localRotation = localRot;
                spawned++;
            }

            // Tint every spawned renderer so the preview reads as obviously distinct.
            // Uses MaterialPropertyBlock so we don't allocate per-instance materials.
            if (tintColor.a > 0f && spawned > 0)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", tintColor);   // URP/Lit
                mpb.SetColor("_Color",     tintColor);   // Standard fallback
                foreach (var r in _spawnedRoot.GetComponentsInChildren<MeshRenderer>(true))
                    r.SetPropertyBlock(mpb);
            }

            Debug.Log($"[BlueprintTestBuildingHost] Refresh: spawned {spawned} rooms from " +
                      $"'{blueprint.DisplayName}' at world {transform.position}");
        }
    }
}
