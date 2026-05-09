using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Round
{
    // Configures the static visual showcase a flat test world spawns alongside its
    // launchpad. Sections exist so authors can group prefabs by category in the picker
    // and in tests, but at runtime the whole list is laid out as one flat grid - the
    // section labels are purely organizational.
    [CreateAssetMenu(menuName = "Friend Slop/Test World Display Set", fileName = "TestWorldDisplaySet")]
    public sealed class TestWorldDisplaySet : ScriptableObject
    {
        [System.Serializable]
        public class Section
        {
            public string label;
            public GameObject[] prefabs;
        }

        [SerializeField] private List<Section> sections = new();
        public IReadOnlyList<Section> Sections => sections;

        // Flatten the section-of-prefabs layout into a single ordered list. Used by the
        // runtime showcase (which doesn't care about sections) and by tests asserting set
        // membership.
        public IEnumerable<GameObject> AllPrefabs()
        {
            if (sections == null) yield break;
            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                if (section?.prefabs == null) continue;
                for (var j = 0; j < section.prefabs.Length; j++)
                {
                    var prefab = section.prefabs[j];
                    if (prefab != null) yield return prefab;
                }
            }
        }
    }
}
