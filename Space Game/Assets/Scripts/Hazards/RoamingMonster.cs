using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    [RequireComponent(typeof(NetworkObject))]
    public class RoamingMonster : NetworkBehaviour
    {
        [SerializeField] private float detectionRange = 22f;
        [SerializeField] private float attackDistance = 2f;
        [SerializeField] private float moveSpeed = 4.2f;
        [SerializeField] private float knockbackStrength = 9f;
        [SerializeField] private float attackCooldown = 2f;
        [SerializeField] private float roamRadius = 8f;
        [SerializeField] private float surfaceHeight = 0.65f;
        [SerializeField] private float rotationSharpness = 10f;

        private Vector3 spawnPosition;
        private Vector3 roamTarget;
        private float nextAttackTime;

        private void Awake()
        {
            var world = SphereWorld.GetClosest(transform.position);
            if (world != null)
            {
                spawnPosition = world.GetSurfacePoint(world.GetUp(transform.position), Mathf.Max(surfaceHeight, world.GetSurfaceDistance(transform.position)));
                transform.position = spawnPosition;
                AlignToSurface(world, transform.forward, 1f);
            }
            else
            {
                spawnPosition = transform.position;
            }

            PickRoamTarget(world);
        }

        private void Update()
        {
            if (!IsServer || RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active)
            {
                return;
            }

            var world = SphereWorld.GetClosest(transform.position);
            if (world == null)
            {
                return;
            }

            var target = FindNearestPlayer();
            if (target != null)
            {
                MoveToward(world, target.transform.position);
                TryAttack(world, target);
                return;
            }

            if (Vector3.Distance(transform.position, roamTarget) < 1.1f)
            {
                PickRoamTarget(world);
            }

            MoveToward(world, roamTarget);
        }

        public void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            transform.position = spawnPosition;
            nextAttackTime = 0f;
            var world = SphereWorld.GetClosest(transform.position);
            AlignToSurface(world, transform.forward, 1f);
            PickRoamTarget(world);
        }

        private NetworkFirstPersonController FindNearestPlayer()
        {
            NetworkFirstPersonController bestPlayer = null;
            var bestDistance = detectionRange;

            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned)
                {
                    continue;
                }

                var distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPlayer = player;
                }
            }

            return bestPlayer;
        }

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

        private void TryAttack(SphereWorld world, NetworkFirstPersonController player)
        {
            if (Time.time < nextAttackTime || Vector3.Distance(transform.position, player.transform.position) > attackDistance)
            {
                return;
            }

            nextAttackTime = Time.time + attackCooldown;

            var away = Vector3.ProjectOnPlane(player.transform.position - transform.position, world.GetUp(player.transform.position));
            if (away.sqrMagnitude < 0.001f)
            {
                away = player.transform.forward;
            }

            away.Normalize();
            var impulse = away * knockbackStrength + world.GetUp(player.transform.position) * 3f;
            player.ServerForceDropHeld(impulse);
            player.KnockbackClientRpc(impulse);
        }

        private void PickRoamTarget(SphereWorld world)
        {
            if (world == null)
            {
                roamTarget = spawnPosition;
                return;
            }

            var offsetPoint = spawnPosition + Random.onUnitSphere * roamRadius;
            roamTarget = world.GetSurfacePoint(world.GetUp(offsetPoint), surfaceHeight);
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
