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

        [Header("Objective (optional — falls back to RoundManager default)")]
        [SerializeField] private RoundObjective objective;

        [Header("Optional Visuals")]
        [SerializeField] private Color skyTint = Color.white;
        [SerializeField] private Sprite previewIcon;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string Description => description;
        public int Tier => Mathf.Clamp(tier, 1, PlanetCatalog.MaxTier);
        public int QuotaOverride => quotaOverride;
        public float RoundLengthOverride => roundLengthOverride;
        public Color SkyTint => skyTint;
        public Sprite PreviewIcon => previewIcon;
        public RoundObjective Objective => objective;
    }
}
