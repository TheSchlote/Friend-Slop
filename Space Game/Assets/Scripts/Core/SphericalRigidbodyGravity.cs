using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Core
{
    [RequireComponent(typeof(Rigidbody))]
    public class SphericalRigidbodyGravity : MonoBehaviour
    {
        [SerializeField] private bool alignUpToSurface;
        [SerializeField] private float alignSpeed = 6f;

        private Rigidbody body;
        private NetworkObject networkObject;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkObject = GetComponent<NetworkObject>();
            body.useGravity = false;
        }

        private void FixedUpdate()
        {
            if (body == null || body.isKinematic)
            {
                return;
            }

            if (networkObject != null && networkObject.IsSpawned && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
            {
                return;
            }

            var world = SphereWorld.GetClosest(body.position);
            if (world == null)
            {
                return;
            }

            var up = (body.position - world.Center).normalized;
            body.AddForce(-up * world.GravityAcceleration, ForceMode.Acceleration);

            if (!alignUpToSurface)
            {
                return;
            }

            var targetRotation = Quaternion.FromToRotation(transform.up, up) * body.rotation;
            body.MoveRotation(Quaternion.Slerp(body.rotation, targetRotation, alignSpeed * Time.fixedDeltaTime));
        }
    }
}
