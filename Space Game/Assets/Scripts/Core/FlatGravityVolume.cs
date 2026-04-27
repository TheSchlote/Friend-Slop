using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core
{
    public sealed class FlatGravityVolume : MonoBehaviour
    {
        private static readonly List<FlatGravityVolume> Volumes = new();

        [SerializeField] private Vector3 size = new(24f, 8f, 18f);
        [SerializeField] private int priority = 0;

        public Vector3 Size => size;
        public int Priority => priority;
        public Vector3 Up => transform.up;

        private void OnEnable()
        {
            if (!Volumes.Contains(this))
            {
                Volumes.Add(this);
            }
        }

        private void OnDisable()
        {
            Volumes.Remove(this);
        }

        public bool Contains(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            var halfSize = size * 0.5f;
            const float padding = 0.001f;
            return Mathf.Abs(local.x) <= halfSize.x + padding
                && Mathf.Abs(local.y) <= halfSize.y + padding
                && Mathf.Abs(local.z) <= halfSize.z + padding;
        }

        public static bool TryGetContaining(Vector3 worldPosition, out FlatGravityVolume volume)
        {
            volume = null;
            for (var i = 0; i < Volumes.Count; i++)
            {
                var candidate = Volumes[i];
                if (candidate == null || !candidate.Contains(worldPosition))
                {
                    continue;
                }

                if (volume == null || candidate.priority > volume.priority)
                {
                    volume = candidate;
                }
            }

            return volume != null;
        }

        public static Vector3 GetGravityUp(Vector3 worldPosition)
        {
            return TryGetContaining(worldPosition, out var volume)
                ? volume.Up
                : SphereWorld.GetGravityUp(worldPosition);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.22f);
            var matrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = matrix;
        }
#endif
    }
}
