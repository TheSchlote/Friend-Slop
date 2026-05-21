using System;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Ship
{
    // Per-player aggregate score on this dartboard. The clientId+totalScore
    // pair plus best-throw are enough for the scoreboard UI.
    public struct DartScoreEntry : INetworkSerializable, IEquatable<DartScoreEntry>
    {
        public ulong clientId;
        public int totalScore;
        public int bestThrow;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref totalScore);
            serializer.SerializeValue(ref bestThrow);
        }

        public bool Equals(DartScoreEntry other)
        {
            return clientId == other.clientId
                && totalScore == other.totalScore
                && bestThrow == other.bestThrow;
        }

        public override bool Equals(object obj)
        {
            return obj is DartScoreEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(clientId, totalScore, bestThrow);
        }
    }

    // Hitscan-style dart toss. No projectile prefab — the owner sends the
    // camera ray + charge to the server, the server raycasts against the
    // dartboard's target plane (clamping range and validating origin
    // against the player to prevent trust-the-client hits), scores via
    // DartScoring.ScoreForRadius, and broadcasts a hit ClientRpc so every
    // client can show a local floating "Direct hit! +25" overlay.
    //
    // Charge is bookkept locally on the owner (per CLAUDE.md "trust the
    // server" rule — charge01 is clamped and only used to scale a fudge
    // factor for the hit position, not the score itself).
    //
    // No NetworkObjects are spawned per throw. Floating-text UI is a UI-
    // layer responsibility and gets the event via an `event System.Action`
    // on this component.
    public class DartboardStationBehavior : ShipStationBehavior
    {
        // The mesh / collider that represents the target face. If null the
        // behavior tries to find a child named "Target".
        [SerializeField] private Transform target;

        // Radius (world units) of the dartboard at radius01 = 1. Used to
        // convert hit distance into normalized radius for ScoreForRadius.
        [SerializeField] private float targetRadius = 0.5f;

        // Maximum throw distance the server will accept. Throws beyond this
        // are rejected (trust-the-server invariant on client-supplied
        // vectors per CLAUDE.md §3).
        [SerializeField] private float maxThrowDistance = 25f;

        public NetworkList<DartScoreEntry> Scoreboard;

        // Client-side floating-text hook. UI layer subscribes; the event
        // fires once per throw on every client when the server broadcasts.
        public event System.Action<DartHitInfo> LocalThrowResolved;

        public struct DartHitInfo
        {
            public ulong clientId;
            public Vector3 hitPosition;
            public int score;
            public bool isMiss;
        }

        // ── Lifecycle ──────────────────────────────────────────────────

        private void Awake()
        {
            Scoreboard = new NetworkList<DartScoreEntry>();
            if (target == null)
                target = transform.Find("Target");
        }

        public override bool AllowsInteract(NetworkFirstPersonController player, ShipStation host)
        {
            // Like the boombox: anyone alive can throw.
            return player != null && !player.IsDead && !player.IsBeingCarried.Value;
        }

        public override string BuildPrompt(NetworkFirstPersonController player, ShipStation host)
        {
            if (host == null) return string.Empty;

            var top = FormatTop3();
            if (string.IsNullOrEmpty(top))
                return $"E throw dart at {host.DisplayName}";

            return $"E throw dart at {host.DisplayName} | {top}";
        }

        // The interact RPC path on ShipStation is the "I want to throw"
        // press; for the dartboard the actual throw goes through a separate
        // RPC below so it can carry the camera vector + charge. We treat
        // the standard interact as a "ready to throw" toast.
        public override void HandleInteractServer(ulong senderClientId, ShipStation host)
        {
            // Default press = nothing happens on the server. The owner-side
            // input handler reads "interact pressed" as "start charge" and
            // "interact released" as "fire RequestThrowServerRpc". Charge
            // bookkeeping is fully owner-local — see the UI layer when
            // dart-charge input gets wired.
        }

        // ── Throw RPC ──────────────────────────────────────────────────

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestThrowServerRpc(
            Vector3 cameraOrigin,
            Vector3 cameraForward,
            float charge01,
            RpcParams rpcParams = default)
        {
            if (!IsServer) return;

            // Validate client-supplied vectors (CLAUDE.md §3).
            if (!IsFiniteVector(cameraOrigin) || !IsFiniteVector(cameraForward))
                return;

            var sender = rpcParams.Receive.SenderClientId;

            if (cameraForward.sqrMagnitude < 0.0001f)
                return;
            cameraForward = cameraForward.normalized;

            charge01 = Mathf.Clamp01(charge01);

            var hitInfo = ResolveThrow(cameraOrigin, cameraForward);
            if (!hitInfo.HasValue)
            {
                BroadcastMissClientRpc(sender);
                return;
            }

            var hit = hitInfo.Value;
            var score = DartScoring.ScoreForRadius(hit.radius01);

            AddScoreForClient(sender, score);

            BroadcastHitClientRpc(sender, hit.worldPosition, score);
        }

        private struct HitData
        {
            public Vector3 worldPosition;
            public float radius01;
        }

        // Raycast against the target's plane. Returns null when the ray
        // misses the plane, hits behind the player, or exceeds
        // maxThrowDistance.
        private HitData? ResolveThrow(Vector3 cameraOrigin, Vector3 cameraForward)
        {
            if (target == null) return null;
            if (targetRadius <= 0f) return null;

            // Plane defined by target.position + target.forward as normal.
            var planeOrigin = target.position;
            var planeNormal = target.forward;
            if (planeNormal.sqrMagnitude < 0.0001f) return null;
            planeNormal.Normalize();

            var denominator = Vector3.Dot(planeNormal, cameraForward);
            if (Mathf.Abs(denominator) < 0.0001f) return null;

            var t = Vector3.Dot(planeOrigin - cameraOrigin, planeNormal) / denominator;
            if (t < 0f) return null;
            if (t > maxThrowDistance) return null;

            var worldHit = cameraOrigin + cameraForward * t;
            var planar = worldHit - planeOrigin;

            // Distance from the target center along the dartboard's face.
            // We project away the normal component (should already be ~0)
            // and divide by targetRadius for the normalized hit radius.
            var alongNormal = Vector3.Dot(planar, planeNormal);
            var inPlane = planar - planeNormal * alongNormal;
            var radius = inPlane.magnitude;
            var radius01 = radius / targetRadius;

            return new HitData
            {
                worldPosition = worldHit,
                radius01 = radius01,
            };
        }

        // ── Scoreboard mutation ────────────────────────────────────────

        private void AddScoreForClient(ulong clientId, int score)
        {
            if (Scoreboard == null) return;

            for (var i = 0; i < Scoreboard.Count; i++)
            {
                if (Scoreboard[i].clientId != clientId) continue;

                var existing = Scoreboard[i];
                Scoreboard[i] = new DartScoreEntry
                {
                    clientId = clientId,
                    totalScore = existing.totalScore + score,
                    bestThrow = Mathf.Max(existing.bestThrow, score),
                };
                return;
            }

            Scoreboard.Add(new DartScoreEntry
            {
                clientId = clientId,
                totalScore = score,
                bestThrow = score,
            });
        }

        // ── Floating-text fan-out ──────────────────────────────────────

        [ClientRpc]
        private void BroadcastHitClientRpc(ulong clientId, Vector3 position, int score)
        {
            LocalThrowResolved?.Invoke(new DartHitInfo
            {
                clientId = clientId,
                hitPosition = position,
                score = score,
                isMiss = score <= 0,
            });
        }

        [ClientRpc]
        private void BroadcastMissClientRpc(ulong clientId)
        {
            LocalThrowResolved?.Invoke(new DartHitInfo
            {
                clientId = clientId,
                hitPosition = Vector3.zero,
                score = 0,
                isMiss = true,
            });
        }

        // ── Prompt formatting ──────────────────────────────────────────

        private string FormatTop3()
        {
            if (Scoreboard == null || Scoreboard.Count == 0) return string.Empty;

            // Find top three totalScore values; ties broken by clientId asc
            // for determinism.
            Span<DartScoreEntry> top = stackalloc DartScoreEntry[3];
            var topCount = 0;
            for (var i = 0; i < Scoreboard.Count; i++)
            {
                var entry = Scoreboard[i];
                InsertIntoTop(top, ref topCount, entry);
            }

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < topCount; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append('#').Append(i + 1).Append(':').Append(top[i].totalScore);
            }
            return sb.ToString();
        }

        private static void InsertIntoTop(Span<DartScoreEntry> top, ref int count, DartScoreEntry candidate)
        {
            // Compare against the slot with the lowest score, replace if
            // candidate beats it (ties broken by lower clientId winning).
            if (count < top.Length)
            {
                top[count] = candidate;
                count++;
                SortTop(top, count);
                return;
            }

            var worstIdx = count - 1;
            var worst = top[worstIdx];
            if (candidate.totalScore > worst.totalScore
                || (candidate.totalScore == worst.totalScore && candidate.clientId < worst.clientId))
            {
                top[worstIdx] = candidate;
                SortTop(top, count);
            }
        }

        private static void SortTop(Span<DartScoreEntry> top, int count)
        {
            // Simple insertion sort, n=3 max — descending totalScore, then
            // ascending clientId.
            for (var i = 1; i < count; i++)
            {
                var key = top[i];
                var j = i - 1;
                while (j >= 0 && IsBetter(key, top[j]))
                {
                    top[j + 1] = top[j];
                    j--;
                }
                top[j + 1] = key;
            }
        }

        private static bool IsBetter(DartScoreEntry a, DartScoreEntry b)
        {
            if (a.totalScore != b.totalScore) return a.totalScore > b.totalScore;
            return a.clientId < b.clientId;
        }

        private static bool IsFiniteVector(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                  || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
