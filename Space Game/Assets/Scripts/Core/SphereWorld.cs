using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core
{
    public class SphereWorld : MonoBehaviour
    {
        private static readonly List<SphereWorld> Worlds = new();

        [SerializeField] private float radius = 18f;
        [SerializeField] private float gravityAcceleration = 18f;
        // Linear deceleration (units/sec) applied to a player's tangential velocity once
        // they release input. 0 = snap-stop (default; how every planet behaved before ice
        // was introduced). Higher = longer slide before they come to rest. Read by the
        // player controller when grounded on this world; non-player physics ignore it.
        [SerializeField] private float surfaceSlideDecel;

        public float Radius => radius;
        public float GravityAcceleration => gravityAcceleration;
        public float SurfaceSlideDecel => Mathf.Max(0f, surfaceSlideDecel);
        public Vector3 Center => transform.position;

        // Runtime configuration hooks for procedurally-built sphere worlds (e.g. the flat
        // test world). Editor builders go through SerializedObject so they can persist
        // values into prefabs/scenes; runtime callers don't need persistence and can mutate
        // the fields directly.
        public void SetRadius(float value) => radius = Mathf.Max(0.01f, value);
        public void SetGravityAcceleration(float value) => gravityAcceleration = Mathf.Max(0f, value);
        public void SetSurfaceSlideDecel(float value) => surfaceSlideDecel = Mathf.Max(0f, value);

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
