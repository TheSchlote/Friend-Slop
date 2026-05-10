using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Interiors
{
    [CreateAssetMenu(menuName = "Friend Slop/Interiors/Interior Catalog", fileName = "InteriorCatalog")]
    public class InteriorCatalog : ScriptableObject
    {
        [SerializeField] private BuildingDefinition[] buildings = System.Array.Empty<BuildingDefinition>();

        public IReadOnlyList<BuildingDefinition> Buildings => buildings;
    }
}
