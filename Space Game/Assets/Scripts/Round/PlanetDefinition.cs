using FriendSlop.SceneManagement;
using UnityEngine;

namespace FriendSlop.Round
{
    [CreateAssetMenu(menuName = "Friend Slop/Planet Definition", fileName = "Planet")]
    public class PlanetDefinition : ScriptableObject
    {
        [SerializeField] private string displayName = "Unnamed Planet";
        [TextArea] [SerializeField] private string description;
        [SerializeField, Range(1, PlanetCatalog.MaxTier)] private int tier = 1;

        [Header("Round Overrides (0 = keep RoundManager defaults)")]
        [SerializeField] private int quotaOverride;
        [SerializeField] private float roundLengthOverride;

        [Header("Loot Budget (0 = legacy spawn-points × rolls)")]
        [Tooltip("Target loot value for this planet. When > 0, PlanetLootSpawner ignores " +
                 "rollsPerSpawnPoint and instead rolls until cumulative item value hits " +
                 "1.2× this budget (guaranteed minimum); individual rolls that would push " +
                 "past 2.0× (ceiling) are skipped. See BACKLOG §7.")]
        [SerializeField, Min(0)] private int lootValueBudget;

        [Header("Objective (optional - falls back to RoundManager default)")]
        [SerializeField] private RoundObjective objective;

        [Header("Optional Visuals")]
        [SerializeField] private Color skyTint = Color.white;
        [SerializeField] private Sprite previewIcon;

        // When set, the round manager additively loads this scene on travel and unloads
        // any previously-loaded planet scene. Leave null for legacy planets that still
        // live as nested GameObjects inside the prototype scene.
        [Header("Scene (optional - per-planet scene asset)")]
        [SerializeField] private GameSceneDefinition planetScene;

        // When true, the global AnomalySpawner skips this planet at TrySpawnOrb time so
        // anomalies never appear during the round. Per-scene AnomalySpawner instances
        // (e.g. Ice Planet's IceMine spawner) are unaffected - they live in their own
        // scene and run their own list. Used by the tier 4 sandbox where the host wants
        // to iterate on procgen without orbs underfoot.
        [Header("Hazards")]
        [SerializeField] private bool suppressAnomalies;

        // When true, this planet is hidden from the normal tier-progression menus and is
        // only reachable via the host's Test Mode picker. Pair with flatTestWorld for the
        // built-in flat sandbox; the flag is also the hook for any future test-only planets
        // (asset bundles, smoke tests, etc.).
        [Header("Test Mode")]
        [SerializeField] private bool testModeOnly;
        // When true, RoundManager builds the planet's environment procedurally at runtime
        // (flat ground, launchpad, ship-return teleporter, four spawns). No scene file or
        // nested GameObjects required - the env exists only while the planet is selected.
        [SerializeField] private bool flatTestWorld;
        // Optional showcase the flat test world spawns next to the launchpad - one
        // display-only instance of each prefab so authors can eyeball every loot item,
        // hazard, and anomaly model in one place. Ignored on non-flat-test-world planets.
        [SerializeField] private TestWorldDisplaySet displaySet;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description;
        public int Tier => Mathf.Clamp(tier, 1, PlanetCatalog.MaxTier);
        public int QuotaOverride => quotaOverride;
        public float RoundLengthOverride => roundLengthOverride;
        public int LootValueBudget => Mathf.Max(0, lootValueBudget);
        public Color SkyTint => skyTint;
        public Sprite PreviewIcon => previewIcon;
        public RoundObjective Objective => objective;
        public GameSceneDefinition PlanetScene => planetScene;
        public bool HasPlanetScene => planetScene != null && planetScene.IsConfigured;
        // FlatTestWorld implies TestModeOnly; the explicit flag covers test-only planets
        // that aren't the procedural flat world.
        public bool SuppressAnomalies => suppressAnomalies;
        public bool IsTestModeOnly => testModeOnly || flatTestWorld;
        public bool IsFlatTestWorld => flatTestWorld;
        public TestWorldDisplaySet DisplaySet => displaySet;
    }
}
