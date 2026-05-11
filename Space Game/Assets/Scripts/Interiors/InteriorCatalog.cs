using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Interior Catalog", fileName = "InteriorCatalog")]
    public class InteriorCatalog : ScriptableObject
    {
        [SerializeField] private BuildingDefinition[] buildings = System.Array.Empty<BuildingDefinition>();
        [Tooltip("Every FurnitureDefinition the game can spawn. The runtime spawner filters " +
                 "this list by room tags + anchor placement when picking pieces.")]
        [SerializeField] private FurnitureDefinition[] furniture = System.Array.Empty<FurnitureDefinition>();

        public IReadOnlyList<BuildingDefinition> Buildings => buildings;
        public IReadOnlyList<FurnitureDefinition> Furniture => furniture;
    }
}
