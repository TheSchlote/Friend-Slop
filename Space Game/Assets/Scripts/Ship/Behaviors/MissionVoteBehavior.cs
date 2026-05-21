using System;
using FriendSlop.Player;
using Unity.Netcode;

namespace FriendSlop.Ship
{
    // Per-client vote record. NetworkList<T> requires INetworkSerializable +
    // IEquatable<T>; the latter must match by content (clientId+candidate)
    // so removals via NetworkList<T>.Remove find the right entry.
    public struct MissionVoteEntry : INetworkSerializable, IEquatable<MissionVoteEntry>
    {
        public ulong clientId;
        public byte candidateIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref candidateIndex);
        }

        public bool Equals(MissionVoteEntry other)
        {
            return clientId == other.clientId && candidateIndex == other.candidateIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is MissionVoteEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(clientId, candidateIndex);
        }
    }

    // Mission-vote station: three candidate slots, one vote per client, the
    // press cycles the caller's vote (no vote -> 0 -> 1 -> 2 -> no vote).
    //
    // This behavior intentionally doesn't pick the next planet — Pilot still
    // owns ServerStartRound, which still asks RoundManager for the next
    // planet the way it did before. A downstream PR wires
    // MissionVoteTally.WinningCandidate into PlanetCatalog selection.
    //
    // Server authority: vote state is a NetworkList<MissionVoteEntry> owned
    // by the server. Clients send a parameterless RPC; the server reads the
    // RPC sender, cycles their vote, replicates.
    public class MissionVoteBehavior : ShipStationBehavior
    {
        public const int CandidateCount = 3;

        public NetworkList<MissionVoteEntry> Votes;

        private void Awake()
        {
            Votes = new NetworkList<MissionVoteEntry>();
        }

        public override bool AllowsInteract(NetworkFirstPersonController player, ShipStation host)
        {
            // Anyone alive can vote — no occupancy gate.
            return player != null && !player.IsDead && !player.IsBeingCarried.Value;
        }

        public override string BuildPrompt(NetworkFirstPersonController player, ShipStation host)
        {
            if (host == null) return string.Empty;

            var tallies = TallyVotes();
            var label = $"[{tallies[0]}:{tallies[1]}:{tallies[2]}] E vote {host.DisplayName}";

            if (player == null) return label;

            var currentVote = FindCurrentVote(player.OwnerClientId);
            if (currentVote == MissionVoteTally.NoVote)
                return $"{label} (you: no vote)";

            return $"{label} (you: candidate {currentVote + 1})";
        }

        public override void HandleInteractServer(ulong senderClientId, ShipStation host)
        {
            if (!IsServer || Votes == null) return;

            var current = FindCurrentVote(senderClientId);
            var next = MissionVoteTally.CycleVote(current, CandidateCount);

            // Remove the existing entry (if any) so we don't accumulate
            // ghost votes when a client cycles past "no vote".
            RemoveVoteFor(senderClientId);

            if (next != MissionVoteTally.NoVote)
            {
                Votes.Add(new MissionVoteEntry
                {
                    clientId = senderClientId,
                    candidateIndex = next,
                });
            }
        }

        // Server-side: removes the existing entry for this client without
        // depending on Equals (which has to match candidate index too).
        private void RemoveVoteFor(ulong clientId)
        {
            for (var i = Votes.Count - 1; i >= 0; i--)
            {
                if (Votes[i].clientId == clientId)
                    Votes.RemoveAt(i);
            }
        }

        // Looks up the caller's current vote; NoVote if they haven't voted.
        public byte FindCurrentVote(ulong clientId)
        {
            if (Votes == null) return MissionVoteTally.NoVote;
            for (var i = 0; i < Votes.Count; i++)
            {
                if (Votes[i].clientId == clientId)
                    return Votes[i].candidateIndex;
            }
            return MissionVoteTally.NoVote;
        }

        // Returns the per-candidate vote count snapshot. Index = candidate.
        public int[] TallyVotes()
        {
            var tallies = new int[CandidateCount];
            if (Votes == null) return tallies;

            for (var i = 0; i < Votes.Count; i++)
            {
                var idx = Votes[i].candidateIndex;
                if (idx < CandidateCount) tallies[idx]++;
            }
            return tallies;
        }

        public int? WinningCandidate()
        {
            return MissionVoteTally.WinningCandidate(TallyVotes());
        }
    }
}
