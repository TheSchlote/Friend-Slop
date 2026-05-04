using FriendSlop.Core;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Hazards
{
    public partial class RoamingMonster
    {
        private NetworkFirstPersonController FindNearestPlayer(SphereWorld world)
        {
            NetworkFirstPersonController bestPlayer = null;
            var bestDistance = detectionRange;

            var up = _frameUp;
            var surfaceForward = _frameSurfaceForward;

            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned)
                    continue;

                if (player.IsDead)
                    continue;

                if (player == _stunnedPlayer && Time.time < _stunnedPlayerUntil)
                    continue;

                var distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance >= bestDistance)
                    continue;

                // Stealth model: more body parts blocked → harder to spot. Crouching shrinks
                // the sampled body height, so cover hides more of the player.
                var visibility = ComputeBodyVisibility(world, player, distance);
                if (visibility <= 0f)
                    continue;

                if (distance < proximityDetectionRange)
                {
                    // Point-blank: still detected as long as something is exposed.
                    bestDistance = distance;
                    bestPlayer = player;
                    continue;
                }

                var effectiveRange = detectionRange * visibility;
                if (distance >= effectiveRange)
                    continue;

                var toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, up);
                if (toPlayer.sqrMagnitude < 0.001f)
                    continue;

                if (Vector3.Angle(surfaceForward, toPlayer) > visionAngle * 0.5f)
                    continue;

                bestDistance = distance;
                bestPlayer = player;
            }

            return bestPlayer;
        }

        private float ComputeBodyVisibility(SphereWorld world, NetworkFirstPersonController player, float distance)
        {
            if (player == null || world == null) return 0f;

            var origin = transform.position + _frameUp * visionOriginHeight;
            var up = world.GetUp(player.transform.position);
            var bodyHeight = Mathf.Max(0.1f, player.CurrentBodyHeight);

            // Linecasts are the per-frame cost in this method. At distance, sub-meter precision
            // doesn't help — sample the torso only. At medium range, two points (waist + chest).
            // At close range, full sweep — that's where stealth matters most.
            var sampleCount = distance < proximityDetectionRange * 2f
                ? BodySampleHeights.Length
                : distance < detectionRange * 0.5f
                    ? 2
                    : 1;

            var visible = 0;
            for (var i = 0; i < sampleCount; i++)
            {
                // When sampling fewer points, prefer middle samples so we don't bias toward
                // feet/head — those are the points cover most often hides.
                var heightIndex = sampleCount switch
                {
                    1 => 1,
                    2 => i == 0 ? 1 : 2,
                    _ => i,
                };
                var point = player.transform.position + up * (bodyHeight * BodySampleHeights[heightIndex]);
                if (!Physics.Linecast(origin, point, out var hit))
                {
                    visible++;
                    continue;
                }

                if (hit.collider != null && hit.collider.transform.root == player.transform.root)
                    visible++;
            }

            return (float)visible / sampleCount;
        }

        private bool CanDetectPlayer(SphereWorld world, NetworkFirstPersonController player)
        {
            if (player == null || !player.IsSpawned)
                return false;

            if (player.IsDead)
                return false;

            if (player == _stunnedPlayer && Time.time < _stunnedPlayerUntil)
                return false;

            var distance = Vector3.Distance(transform.position, player.transform.position);

            if (distance < proximityDetectionRange)
                return true;

            if (distance >= detectionRange)
                return false;

            var up = _frameUp;
            var surfaceForward = _frameSurfaceForward;

            var toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, up);
            if (toPlayer.sqrMagnitude < 0.001f)
                return false;

            if (Vector3.Angle(surfaceForward, toPlayer) > visionAngle * 0.5f)
                return false;

            return !IsOccluded(world, player);
        }

        private bool IsOccluded(SphereWorld world, NetworkFirstPersonController player)
        {
            var origin      = transform.position        + _frameUp                                           * visionOriginHeight;
            var destination = player.transform.position + world.GetUp(player.transform.position) * visionOriginHeight;

            if (!Physics.Linecast(origin, destination, out var hit))
                return false;

            return hit.collider.transform.root != player.transform.root;
        }
    }
}
