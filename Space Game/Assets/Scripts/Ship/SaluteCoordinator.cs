using System;
using System.Collections.Generic;
using FriendSlop.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace FriendSlop.Ship
{
    // Per-client salute timestamp record. Replicated so any client UI can
    // mirror the "who has saluted recently" set without an extra RPC.
    public struct SaluteEntry : INetworkSerializable, IEquatable<SaluteEntry>
    {
        public ulong clientId;
        public float saluteTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref saluteTime);
        }

        public bool Equals(SaluteEntry other)
        {
            return clientId == other.clientId
                && Mathf.Approximately(saluteTime, other.saluteTime);
        }

        public override bool Equals(object obj)
        {
            return obj is SaluteEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(clientId, saluteTime);
        }
    }

    // Coordinates the "Perfect Crew" salute: whenever every player who is
    // currently occupying a ship station has saluted within a short rolling
    // window (1.5s default), the coordinator fires a one-shot AudioCue +
    // UnityEvent and clears the log to debounce.
    //
    // No singletons (D-014): owners reach the coordinator via
    // SaluteCoordinatorRegistry.Current, which is populated by this
    // component's own OnNetworkSpawn / OnNetworkDespawn.
    //
    // Server authority: RequestSaluteServerRpc is the only mutator. The
    // window check runs inline at the end of each RPC — that's the only
    // moment new state can satisfy the predicate, so there's no need for a
    // per-frame poll (would also violate the "no FindObjectsByType in
    // Update" rule).
    [RequireComponent(typeof(NetworkObject))]
    public class SaluteCoordinator : NetworkBehaviour
    {
        [SerializeField] private float windowSeconds = 1.5f;

        public NetworkList<SaluteEntry> RecentSalutes;

        // UI hook: clients subscribe to flash a tint when the coordinator
        // resolves a Perfect Crew event.
        public UnityEvent OnPerfectCrew;

        private readonly List<ulong> _occupantsBuffer = new();
        private readonly List<SaluteRecord> _recordsBuffer = new();

        private void Awake()
        {
            RecentSalutes = new NetworkList<SaluteEntry>();
            OnPerfectCrew ??= new UnityEvent();
        }

        public override void OnNetworkSpawn()
        {
            SaluteCoordinatorRegistry.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            SaluteCoordinatorRegistry.Unregister(this);
        }

        public override void OnDestroy()
        {
            SaluteCoordinatorRegistry.Unregister(this);
            base.OnDestroy();
        }

        // Public entry point — call from station code when a player presses
        // the salute key while occupying a station.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSaluteServerRpc(ulong clientId, RpcParams rpcParams = default)
        {
            if (!IsServer || RecentSalutes == null) return;

            if (clientId != rpcParams.Receive.SenderClientId) return;

            // Replace any previous entry from this client; we only ever care
            // about the latest salute timestamp per client.
            for (var i = RecentSalutes.Count - 1; i >= 0; i--)
            {
                if (RecentSalutes[i].clientId == clientId)
                    RecentSalutes.RemoveAt(i);
            }

            RecentSalutes.Add(new SaluteEntry
            {
                clientId = clientId,
                saluteTime = Time.time,
            });

            TryFirePerfectCrew();
        }

        // Server-side window evaluation. Called right after the salute log
        // changes — that's the only moment the predicate can transition to
        // true, so polling in Update is unnecessary (and forbidden by the
        // "no FindObjectsByType in per-frame methods" guardrail).
        private void TryFirePerfectCrew()
        {
            CollectOccupants(_occupantsBuffer);
            if (_occupantsBuffer.Count == 0) return;

            CollectRecords(_recordsBuffer);
            var now = Time.time;

            if (!SaluteWindow.AllOccupiedSaluteWithinWindow(
                    _occupantsBuffer, _recordsBuffer, now, windowSeconds))
                return;

            PerfectCrewClientRpc(ComputeCentre());

            // Debounce — clear the log so we don't fire again on the next
            // salute press.
            RecentSalutes.Clear();
        }

        [ClientRpc]
        private void PerfectCrewClientRpc(Vector3 centerPosition)
        {
            // Placeholder cue: AudioCueId.LaunchIgnition stands in until a
            // dedicated PerfectCrew enum entry lands in a later PR (the
            // parent branch owns the enum; this PR doesn't extend it).
            AudioCue.Play(AudioCueId.LaunchIgnition, centerPosition);
            OnPerfectCrew?.Invoke();
        }

        // Returns the set of client ids currently occupying a ShipStation.
        // Called from the RPC handler (NOT Update) so the FindObjectsByType
        // call is bounded by player input rate, not framerate. Uses the
        // FindObjectsByType + IsSpawned filter pattern from CLAUDE §9 —
        // NetworkPrefabsList parks inactive templates in the bootstrap
        // scene that we must not count as "occupied".
        private void CollectOccupants(List<ulong> buffer)
        {
            buffer.Clear();
            var stations = FindObjectsByType<ShipStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < stations.Length; i++)
            {
                var station = stations[i];
                if (station == null) continue;
                var netObj = station.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.IsSpawned) continue;
                if (!station.IsOccupied) continue;
                buffer.Add(station.OccupantClientId.Value);
            }
        }

        private void CollectRecords(List<SaluteRecord> buffer)
        {
            buffer.Clear();
            if (RecentSalutes == null) return;
            for (var i = 0; i < RecentSalutes.Count; i++)
            {
                var e = RecentSalutes[i];
                buffer.Add(new SaluteRecord(e.clientId, e.saluteTime));
            }
        }

        private Vector3 ComputeCentre()
        {
            // Cheap stand-in: just use our own transform. The actual cue
            // attenuation is "good enough" since ShipInterior is small.
            return transform.position;
        }
    }
}
