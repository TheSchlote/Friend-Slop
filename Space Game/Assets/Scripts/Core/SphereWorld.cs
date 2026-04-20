using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core
{
    public class SphereWorld : MonoBehaviour
    {
        private static readonly List<SphereWorld> Worlds = new();

        [SerializeField] private float radius = 18f;
        [SerializeField] private float gravityAcceleration = 28f;

        public float Radius => radius;
        public float GravityAcceleration => gravityAcceleration;
        public Vector3 Center => transform.position;

        private void OnEnable()
        {
            if (!Worlds.Contains(this))
            {
                Worlds.Add(this);
            }
        }

        private void OnDisable()
        {
            Worlds.Remove(this);
        }

        public static SphereWorld GetClosest(Vector3 position)
        {
            SphereWorld closest = null;
            var closestDistance = float.MaxValue;

            foreach (var world in Worlds)
            {
                if (world == null)
                {
                    continue;
                }

                var distance = (position - world.Center).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = world;
                }
            }

            return closest;
        }

        public static Vector3 GetGravityUp(Vector3 position)
        {
            var world = GetClosest(position);
            if (world == null)
            {
                return Vector3.up;
            }

            return world.GetUp(position);
        }

        public Vector3 GetUp(Vector3 position)
        {
            var up = position - Center;
            return up.sqrMagnitude > 0.001f ? up.normalized : transform.up;
        }

        public float GetSurfaceDistance(Vector3 position)
        {
            return Vector3.Distance(position, Center) - radius;
        }

        public Vector3 GetSurfacePoint(Vector3 normal, float heightOffset = 0f)
        {
            return Center + normal.normalized * (radius + heightOffset);
        }

        public Quaternion GetSurfaceRotation(Vector3 normal, Vector3 forwardHint)
        {
            var up = normal.normalized;
            var forward = Vector3.ProjectOnPlane(forwardHint, up);
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.Cross(up, Vector3.right);
            }

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.Cross(up, Vector3.forward);
            }

            return Quaternion.LookRotation(forward.normalized, up);
        }
    }
}
