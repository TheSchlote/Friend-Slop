using FriendSlop.Core;
using FriendSlop.Player;
using UnityEngine;

namespace FriendSlop.Hazards
{
    public partial class RoamingMonster
    {
        private void MoveToward(SphereWorld world, Vector3 targetPosition)
        {
            var up = world.GetUp(transform.position);
            var tangent = Vector3.ProjectOnPlane(targetPosition - transform.position, up);
            if (tangent.sqrMagnitude < 0.001f)
            {
                return;
            }

            tangent.Normalize();
            var candidatePosition = transform.position + tangent * moveSpeed * Time.deltaTime;
            transform.position = world.GetSurfacePoint(world.GetUp(candidatePosition), surfaceHeight);
            AlignToSurface(world, tangent, Time.deltaTime);
        }

        // Returns true when the attack landed, triggering a retreat.
        private bool TryAttack(SphereWorld world, NetworkFirstPersonController player)
        {
            if (player.IsDead) return false;
            if (Time.time < nextAttackTime || Vector3.Distance(transform.position, player.transform.position) > attackDistance)
                return false;

            nextAttackTime = Time.time + attackCooldown;

            var away = Vector3.ProjectOnPlane(player.transform.position - transform.position, world.GetUp(player.transform.position));
            if (away.sqrMagnitude < 0.001f)
                away = player.transform.forward;

            away.Normalize();
            var impulse = away * knockbackStrength + world.GetUp(player.transform.position) * 3f;

            player.ServerForceDropHeld(impulse);
            player.StunClientRpc(stunDuration, impulse);
            player.ServerTakeDamage(attackDamage);

            _stunnedPlayer = player;
            _stunnedPlayerUntil = Time.time + stunDuration;

            return true;
        }

        private void PickRoamTarget(SphereWorld world)
        {
            if (world == null)
            {
                roamTarget = spawnPosition;
                return;
            }

            // Pick the offset in the local tangent plane so a near-radial random pick
            // doesn't collapse back onto spawnPosition after surface projection.
            var up = world.GetUp(spawnPosition);
            var tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.ProjectOnPlane(Vector3.right, up);
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.ProjectOnPlane(Vector3.forward, up);
            tangent.Normalize();

            var distance = Random.Range(roamRadius * 0.5f, roamRadius);
            var offsetPoint = spawnPosition + tangent * distance;
            roamTarget = world.GetSurfacePoint(world.GetUp(offsetPoint), surfaceHeight);
        }

        private void PickRetreatTarget(SphereWorld world, Vector3 fromPosition)
        {
            var up = world.GetUp(transform.position);
            var awayDir = Vector3.ProjectOnPlane(transform.position - fromPosition, up);
            if (awayDir.sqrMagnitude < 0.001f)
                awayDir = transform.forward;
            awayDir.Normalize();
            var offsetPoint = transform.position + awayDir * roamRadius;
            roamTarget = world.GetSurfacePoint(world.GetUp(offsetPoint), surfaceHeight);
        }

        private void PickInvestigateTarget(SphereWorld world)
        {
            if (world == null)
            {
                _investigateTarget = _lastKnownPosition;
                return;
            }

            var offsetPoint = _lastKnownPosition + Random.onUnitSphere * investigateWanderRadius;
            _investigateTarget = world.GetSurfacePoint(world.GetUp(offsetPoint), surfaceHeight);
        }

        private void AlignToSurface(SphereWorld world, Vector3 forwardHint, float deltaTime)
        {
            if (world == null)
            {
                return;
            }

            var up = world.GetUp(transform.position);
            var targetRotation = world.GetSurfaceRotation(up, forwardHint);
            transform.rotation = deltaTime >= 1f
                ? targetRotation
                : Quaternion.Slerp(transform.rotation, targetRotation, rotationSharpness * deltaTime);
        }
    }
}
