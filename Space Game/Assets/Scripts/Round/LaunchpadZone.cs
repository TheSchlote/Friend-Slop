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
        private RoundManager _subscribedRoundManager;

        private void Awake()
        {
            RemoveOwnCollider();
        }

        private void OnDestroy()
        {
            UnsubscribeFromRoundManager();
        }

        private void TrySubscribeToRoundManager()
        {
            var rm = RoundManager.Instance;
            if (rm == null || rm == _subscribedRoundManager) return;
            UnsubscribeFromRoundManager();
            rm.Phase.OnValueChanged += OnRoundPhaseChanged;
            _subscribedRoundManager = rm;
        }

        private void UnsubscribeFromRoundManager()
        {
            if (_subscribedRoundManager == null) return;
            _subscribedRoundManager.Phase.OnValueChanged -= OnRoundPhaseChanged;
            _subscribedRoundManager = null;
        }

        // When a new round begins the server clears its boardedPlayerIds set (RoundManager.ServerStartRound).
        // Players who stayed on the pad across round transitions were already in our local
        // playersInsideSubmitArea, so Add() returns false and ServerPlayerBoarded is never
        // re-called for them. Clearing here forces a full re-sync on the next FixedUpdate.
        private void OnRoundPhaseChanged(RoundPhase previous, RoundPhase current)
        {
            if (current == RoundPhase.Loading)
                playersInsideSubmitArea.Clear();
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

            TrySubscribeToRoundManager();
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
                if (player == null || !IsPointInsideSubmitArea(player.transform.position))
                {
                    continue;
                }

                for (var slot = 0; slot < NetworkFirstPersonController.InventorySize; slot++)
                {
                    var item = player.GetInventoryItem(slot);
                    if (item != null)
                        RoundManager.Instance.ServerSubmitToLaunchpad(item);
                }
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
