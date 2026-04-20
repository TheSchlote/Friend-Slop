using UnityEngine;

namespace FriendSlop.Round
{
    public class RocketAssemblyDisplay : MonoBehaviour
    {
        [SerializeField] private GameObject cockpitVisual;
        [SerializeField] private GameObject wingsVisual;
        [SerializeField] private GameObject engineVisual;
        [SerializeField] private GameObject readyBeacon;

        private void Update()
        {
            var round = RoundManager.Instance;
            SetActive(cockpitVisual, round != null && round.HasCockpit.Value);
            SetActive(wingsVisual, round != null && round.HasWings.Value);
            SetActive(engineVisual, round != null && round.HasEngine.Value);
            SetActive(readyBeacon, round != null && round.RocketAssembled.Value);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
