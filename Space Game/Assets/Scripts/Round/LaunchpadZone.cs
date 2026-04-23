using System.Collections.Generic;
using FriendSlop.Loot;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public class LaunchpadZone : MonoBehaviour
    {
        [SerializeField] private float submitRadius = 3f;
        [SerializeField] private float submitHeight = 4f;

        private readonly HashSet<ulong> playersInsideSubmitArea = new();
        private readonly List<ulong> stalePlayerIds = new();
        private NetworkLootItem[] cachedLootItems;
        private float nextLootCacheTime;

        private void Awake()
        {
            RemoveOwnCollider();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying || GetComponent<Collider>() == null)
            {
                return;
            }

            UnityEditor.EditorApplication.delayCall -= RemoveEditorCollider;
            UnityEditor.EditorApplication.delayCall += RemoveEditorCollider;
        }
#endif

        private void RemoveOwnCollider()
        {
            var ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            Destroy(ownCollider);
        }

#if UNITY_EDITOR
        private void RemoveEditorCollider()
        {
            if (this == null || Application.isPlaying)
            {
                return;
            }

            var ownCollider = GetComponent<Collider>();
            if (ownCollider == null)
            {
                return;
            }

            DestroyImmediate(ownCollider);
            UnityEditor.EditorUtility.SetDirty(gameObject);
        }
#endif

        private void FixedUpdate()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || RoundManager.Instance == null)
            {
                return;
            }

            SyncBoardedPlayers();
            TrySubmitLooseItems();
            TrySubmitHeldItems();
        }

        private void SyncBoardedPlayers()
        {
            stalePlayerIds.Clear();
            foreach (var clientId in playersInsideSubmitArea)
            {
                stalePlayerIds.Add(clientId);
            }

            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null)
                {
                    continue;
                }

                var clientId = player.OwnerClientId;
                stalePlayerIds.Remove(clientId);
                var isInsideSubmitArea = IsPointInsideSubmitArea(player.transform.position);
                if (isInsideSubmitArea)
                {
                    if (playersInsideSubmitArea.Add(clientId))
                    {
                        RoundManager.Instance.ServerPlayerBoarded(clientId);
                    }
                }
                else if (playersInsideSubmitArea.Remove(clientId))
                {
                    RoundManager.Instance.ServerPlayerUnboarded(clientId);
                }
            }

            foreach (var staleClientId in stalePlayerIds)
            {
                if (!playersInsideSubmitArea.Remove(staleClientId))
                {
                    continue;
                }

                RoundManager.Instance.ServerPlayerUnboarded(staleClientId);
            }
        }

        private void TrySubmitLooseItems()
        {
            if (Time.time >= nextLootCacheTime || cachedLootItems == null)
            {
                cachedLootItems = FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                nextLootCacheTime = Time.time + 0.5f;
            }

            foreach (var loot in cachedLootItems)
            {
                if (loot == null || loot.IsDeposited.Value || loot.IsCarried.Value || !IsPointInsideSubmitArea(loot.transform.position))
                {
                    continue;
                }

                RoundManager.Instance.ServerSubmitToLaunchpad(loot);
            }
        }

        private void TrySubmitHeldItems()
        {
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || player.HeldItem == null || !IsPointInsideSubmitArea(player.transform.position))
                {
                    continue;
                }

                RoundManager.Instance.ServerSubmitToLaunchpad(player.HeldItem);
            }
        }

        private bool IsPointInsideSubmitArea(Vector3 position)
        {
            var offset = position - transform.position;
            var heightFromPad = Mathf.Abs(Vector3.Dot(offset, transform.up));
            if (heightFromPad > submitHeight)
            {
                return false;
            }

            return Vector3.ProjectOnPlane(offset, transform.up).sqrMagnitude <= submitRadius * submitRadius;
        }
    }
}
