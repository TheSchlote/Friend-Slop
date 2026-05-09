using System.Collections.Generic;
using FriendSlop.Networking;
using UnityEngine;

namespace FriendSlop.Round
{
    // Procedurally-built planet environment for catalog planets flagged as flat test
    // worlds. RoundManager triggers Build at Awake time but the resulting GameObject sits
    // at the scene root rather than under RoundManager - the showcase instantiates
    // NetworkObject-bearing prefabs (loot, hazards) and NGO refuses to Spawn() a
    // NetworkObject (RoundManager) that has any descendant NetworkObject. Lifecycle is
    // therefore decoupled from RoundManager: instances are tracked in a static list and
    // torn down via DestroyAll, which fires automatically on NetworkSessionManager.SessionEnded.
    // The instance stays inactive until ApplyActivePlanetEnvironment toggles it on, mirroring
    // how nested pre-authored planets in the prototype scene behave.
    // Gravity model: no SphereWorld. The visual ground is a flat plane and the player
    // movement system falls back to Vector3.up + characterController.isGrounded when no
    // SphereWorld is registered. An earlier draft used a 5 km gravity sphere centered below
    // the plane, but the sphere surface only coincided with the plane at the pole - walking
    // outward, SnapToSphereSurface tried to drag the player below the plane while the plane
    // collider pushed them back up, producing a stuck/jittery feel. Other planets keep their
    // own SphereWorlds; ApplyActivePlanetEnvironment disables them when the flat test world
    // becomes active, so SphereWorld.GetClosest correctly returns null here.
    [DisallowMultipleComponent]
    public sealed partial class FlatTestWorldEnvironment : MonoBehaviour
    {
        // Plane primitive is 10 m square at scale 1; scaling by 8 gives an 80 m playfield,
        // comfortably larger than the spawn ring + teleporter offset.
        private const float GroundScale = 8f;
        private const float LaunchpadRadius = 4.4f;
        private const float LaunchpadHeightOffset = 0.04f;
        private const float SpawnRingDistance = LaunchpadRadius + 1.5f;
        private const float TeleporterOffset = LaunchpadRadius + 4f;
        private const int SpawnCount = 4;

        // Display showcase grid: prefabs are arranged behind the launchpad in a flat
        // row-major grid the player can walk through. Wide enough that meshes don't
        // overlap, far enough back that they don't crowd the spawn ring.
        private const int ShowcaseColumnsPerRow = 5;
        private const float ShowcaseColumnSpacing = 3.2f;
        private const float ShowcaseRowSpacing = 3.2f;
        private const float ShowcaseStandHeight = 1.1f;
        private const float ShowcaseStartZ = -10f;
        private const float ShowcaseLabelHeight = 1.6f;

        // Stand-in ship exterior: planted west of the launchpad, far enough out that the
        // model isn't crowded by spawns or showcase rows but still in eyeshot from origin.
        private const float ShipDisplayOffsetX = -18f;

        // Tracks every live env instance so DestroyAll can find them without depending on
        // the active scene's PlanetEnvironment list. RoundManager doesn't own these anymore
        // (they sit at the scene root so NGO doesn't see them as nested NetworkObjects), so
        // this list is the single source of truth for cleanup.
        private static readonly List<FlatTestWorldEnvironment> Instances = new();
        private static bool _sessionEndHookInstalled;

        public static IReadOnlyList<FlatTestWorldEnvironment> ActiveInstances => Instances;

        public static FlatTestWorldEnvironment Build(PlanetDefinition planet, Transform parent, Vector3 worldPosition)
        {
            if (planet == null) return null;

            var root = new GameObject($"FlatTestWorld:{planet.name}");
            if (parent != null) root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.position = worldPosition;
            root.transform.rotation = Quaternion.identity;

            var built = root.AddComponent<FlatTestWorldEnvironment>();
            built.BuildHierarchy(planet);

            // Inactive until RoundManager.ApplyActivePlanetEnvironment enables this for the round.
            root.SetActive(false);
            EnsureSessionEndHookInstalled();
            return built;
        }

        // Destroys every live flat-test-world env. Safe to call from anywhere - the env
        // doesn't depend on RoundManager (it sits at the scene root) so cleanup also can't.
        // Called automatically when NetworkSessionManager.SessionEnded fires; can be called
        // manually for editor / test cleanup.
        public static void DestroyAll()
        {
            // Snapshot first - Destroy triggers OnDestroy which mutates Instances.
            var snapshot = Instances.ToArray();
            Instances.Clear();
            for (var i = 0; i < snapshot.Length; i++)
            {
                var env = snapshot[i];
                if (env != null) Destroy(env.gameObject);
            }
        }

        private void Awake()
        {
            if (!Instances.Contains(this)) Instances.Add(this);
        }

        private void OnDestroy()
        {
            Instances.Remove(this);
        }

        private static void EnsureSessionEndHookInstalled()
        {
            if (_sessionEndHookInstalled) return;
            // The +/- pair is paranoia for domain-reload cases where the static stays alive
            // but a fresh subscription is desired; -= on a non-subscribed handler is a no-op.
            NetworkSessionManager.SessionEnded -= DestroyAll;
            NetworkSessionManager.SessionEnded += DestroyAll;
            _sessionEndHookInstalled = true;
        }

        private void BuildHierarchy(PlanetDefinition planet)
        {
            BuildVisualGround();
            var zone = BuildLaunchpad();
            var spawns = BuildSpawnPoints();
            BuildShipReturnTeleporter();
            BuildPrefabShowcase(planet.DisplaySet);
            BuildShipDisplay();

            // Wrap it all in PlanetEnvironment so RoundManager / round-readiness checks bind correctly.
            // Intentionally don't call SetSphereWorld - see class comment for the gravity model.
            var env = gameObject.AddComponent<PlanetEnvironment>();
            env.Configure(planet, zone, spawns);
        }

        private void BuildVisualGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Flat Ground";
            ground.transform.SetParent(transform, worldPositionStays: false);
            ground.transform.localPosition = Vector3.zero;
            ground.transform.localRotation = Quaternion.identity;
            ground.transform.localScale = new Vector3(GroundScale, 1f, GroundScale);
            ApplyMaterial(ground, new Color(0.32f, 0.34f, 0.38f), emissive: false);
        }

        private LaunchpadZone BuildLaunchpad()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Flat World Launchpad";
            pad.transform.SetParent(transform, worldPositionStays: false);
            pad.transform.localPosition = new Vector3(0f, LaunchpadHeightOffset, 0f);
            pad.transform.localScale = new Vector3(LaunchpadRadius, 0.08f, LaunchpadRadius);
            ApplyMaterial(pad, new Color(0.85f, 0.78f, 0.32f), emissive: true);
            // LaunchpadZone removes its own collider on Awake; leaving the cylinder collider
            // here would block the boarding box check, so destroy it before the component runs.
            DestroyComponent(pad.GetComponent<Collider>());
            return pad.AddComponent<LaunchpadZone>();
        }

        private Transform[] BuildSpawnPoints()
        {
            var spawnRoot = new GameObject("Flat World Spawn Points");
            spawnRoot.transform.SetParent(transform, worldPositionStays: false);

            var spawns = new Transform[SpawnCount];
            for (var i = 0; i < SpawnCount; i++)
            {
                var spawn = new GameObject($"Flat World Spawn {i + 1}");
                spawn.transform.SetParent(spawnRoot.transform, worldPositionStays: false);
                var angle = i * (Mathf.PI * 2f / SpawnCount);
                var localPos = new Vector3(Mathf.Cos(angle) * SpawnRingDistance, 0.2f, Mathf.Sin(angle) * SpawnRingDistance);
                spawn.transform.localPosition = localPos;
                // Face the launchpad so players spawn looking at the centerpiece.
                var towardCenter = -new Vector3(localPos.x, 0f, localPos.z);
                if (towardCenter.sqrMagnitude > 1e-4f)
                    spawn.transform.localRotation = Quaternion.LookRotation(towardCenter.normalized, Vector3.up);
                spawns[i] = spawn.transform;
            }
            return spawns;
        }

        private void BuildShipReturnTeleporter()
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Flat World Ship Teleporter";
            pad.transform.SetParent(transform, worldPositionStays: false);
            pad.transform.localPosition = new Vector3(TeleporterOffset, LaunchpadHeightOffset, 0f);
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = new Vector3(2f, 0.06f, 2f);

            // Same shape as FriendSlopAddTeleporterPads.CreatePadPrimitive: keep the cylinder
            // collider as a solid floor and add a tall trigger box for the teleport detector.
            var capsule = pad.GetComponent<Collider>();
            if (capsule != null) capsule.isTrigger = false;
            var trigger = pad.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.6f, 12f, 1.6f);
            trigger.center = new Vector3(0f, 6f, 0f);
            ApplyMaterial(pad, new Color(1f, 0.55f, 0.3f), emissive: true);

            var teleporter = pad.AddComponent<TeleporterPad>();
            teleporter.SetDestination(TeleporterTarget.Ship);
        }

        private void BuildPrefabShowcase(TestWorldDisplaySet displaySet)
        {
            if (displaySet == null) return;

            var showcaseRoot = new GameObject("Display Showcase");
            showcaseRoot.transform.SetParent(transform, worldPositionStays: false);
            showcaseRoot.transform.localPosition = Vector3.zero;
            showcaseRoot.transform.localRotation = Quaternion.identity;

            var index = 0;
            // Iterate sections rather than AllPrefabs so each display knows its section
            // label - the picker shows all 26-ish models, and a per-display label makes
            // them easy to identify at a glance.
            foreach (var section in displaySet.Sections)
            {
                if (section?.prefabs == null) continue;
                for (var i = 0; i < section.prefabs.Length; i++)
                {
                    var prefab = section.prefabs[i];
                    if (prefab == null) continue;
                    SpawnDisplay(showcaseRoot.transform, prefab, section.label, index);
                    index++;
                }
            }
        }

        private static void SpawnDisplay(Transform parent, GameObject prefab, string sectionLabel, int index)
        {
            var col = index % ShowcaseColumnsPerRow;
            var row = index / ShowcaseColumnsPerRow;
            var localX = (col - (ShowcaseColumnsPerRow - 1) * 0.5f) * ShowcaseColumnSpacing;
            var localZ = ShowcaseStartZ - row * ShowcaseRowSpacing;

            var slot = new GameObject($"Display [{sectionLabel}] {prefab.name}");
            slot.transform.SetParent(parent, worldPositionStays: false);
            slot.transform.localPosition = new Vector3(localX, 0f, localZ);
            slot.transform.localRotation = Quaternion.identity;

            var instance = Instantiate(prefab, slot.transform);
            instance.name = prefab.name;
            instance.transform.localPosition = new Vector3(0f, ShowcaseStandHeight, 0f);
            instance.transform.localRotation = Quaternion.identity;
            StripBehavioursForDisplay(instance);
            // Some prefabs (NetworkObject / LootPool entries) start inactive in their
            // authored state; force-enable so they're visible even after the strip pass.
            if (!instance.activeSelf) instance.SetActive(true);

            CreateLabel(slot.transform, prefab.name);
        }

        // Neutralize physics and collisions on the display copy without disabling scripts.
        // The prefabs' gameplay paths self-gate on IsServer / IsSpawned / RoundActive and
        // those all return false on an instance that was never Spawn()'d, so leaving the
        // MonoBehaviours enabled lets visual code (AnomalyOrb pulse, Animator, ParticleSystem,
        // Light flickers, custom material updates, etc.) keep running while gameplay logic
        // self-disables.
        // We previously stripped scripts entirely for safety. Two things let us back off:
        //   - NetworkObject is harmless when not spawned. NGO only acts on objects after an
        //     explicit Spawn() call, which we never make on display copies.
        //   - Every gameplay Update in this codebase guards with a server / phase check.
        //     Without those guards an unconditional behaviour would still misbehave; if you
        //     add a new prefab that drives gameplay from an unguarded Update, special-case
        //     it here rather than re-disabling everything.
        private static void StripBehavioursForDisplay(GameObject root)
        {
            // isKinematic = true drops the body out of the physics solver. Don't write
            // linearVelocity / angularVelocity afterwards - Unity rejects velocity writes on
            // kinematic bodies and logs an error per call.
            var bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (body == null) continue;
                body.useGravity = false;
                body.isKinematic = true;
            }

            // Disable colliders so players can walk through the showcase row instead of
            // bumping it. Triggers also stop firing here, which avoids accidentally tripping
            // damage zones / pickup volumes the display copies might otherwise expose.
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        private static void CreateLabel(Transform parent, string text)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, worldPositionStays: false);
            labelGo.transform.localPosition = new Vector3(0f, ShowcaseStandHeight + ShowcaseLabelHeight, 0f);
            // TextMesh glyphs read from local -Z; the showcase grid sits on the -Z side of
            // the launchpad so the player walks toward -Z to see it. Rotate 180 around Y so
            // the readable face points back at the launchpad / spawn ring.
            labelGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var mesh = labelGo.AddComponent<TextMesh>();
            mesh.text = text.Replace('_', ' ');
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 60;
            mesh.characterSize = 0.05f;
            mesh.color = new Color(1f, 1f, 1f, 0.95f);

            var renderer = labelGo.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void ApplyMaterial(GameObject go, Color color, bool emissive)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color, name = $"{go.name} Material" };
            if (emissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.4f);
            }
            renderer.sharedMaterial = mat;
        }

        private static void DestroyComponent(Component component)
        {
            if (component != null) Destroy(component);
        }
    }
}
