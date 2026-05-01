using System.Collections.Generic;
using FriendSlop.Loot;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    public class LaunchpadZone : MonoBehaviour, IItemDepositSurface
    {
        [SerializeField] private float submitRadius = 3f;
        [SerializeField] private float submitHeight = 4f;

        private readonly HashSet<ulong> playersInsideSubmitArea = new();
        private readonly List<ulong> stalePlayerIds = new();
        private RoundManager _subscribedRoundManager;

        public string DepositLabel => "deposit at launchpad";

        private void Awake()
        {
            RemoveOwnCollider();
        }

        private void OnEnable()
        {
            ItemDepositSurface.Register(this);
        }

        private void OnDisable()
        {
            ItemDepositSurface.Unregister(this);
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

        // RoundManager clears its boarded set when loading a new round. Players can stay
        // physically on the pad across that transition, so clear our cache and let the next
        // server tick report them again.
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

            // Auto-submission was removed - players now press F to deposit. Only the
            // boarding-presence sync still needs server-side polling, since the launch
            // condition depends on knowing which players are standing on the pad.
            TrySubscribeToRoundManager();
            SyncBoardedPlayers();
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

        public bool ContainsPlayer(NetworkFirstPersonController player)
        {
            return player != null && IsPointInsideSubmitArea(player.transform.position);
        }

        public bool Accepts(NetworkLootItem item)
        {
            // Launchpad is the catch-all: ship parts are required here, but it also
            // accepts junk loot so players can hand-deliver in either zone.
            return item != null;
        }

        public void ServerSubmit(NetworkLootItem item)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || RoundManager.Instance == null) return;
            RoundManager.Instance.ServerSubmitToLaunchpad(item);
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
