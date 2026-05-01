using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    // Replicates camera pitch so remote viewers see face meshes track vertical look.
    public partial class NetworkFirstPersonController
    {
        private readonly NetworkVariable<float> _headPitch = new(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private void ReparentHeadBonesToCameraRoot()
        {
            if (cameraRoot == null) return;

            List<Transform> bones = null;
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null || child == cameraRoot) continue;
                if (!child.name.StartsWith("Remote Face")) continue;

                bones ??= new List<Transform>();
                bones.Add(child);
            }

            if (bones == null) return;
            foreach (var bone in bones)
                bone.SetParent(cameraRoot, true);
        }

        private void LateUpdate()
        {
            if (IsOwner || cameraRoot == null) return;
            cameraRoot.localRotation = Quaternion.Euler(_headPitch.Value, 0f, 0f);
        }
    }
}
