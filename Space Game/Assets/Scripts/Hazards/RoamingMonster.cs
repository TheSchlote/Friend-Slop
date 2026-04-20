using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    [RequireComponent(typeof(NetworkObject))]
    public class RoamingMonster : NetworkBehaviour
    {
        [SerializeField] private float detectionRange = 19f;
        [SerializeField] private float attackDistance = 1.8f;
        [SerializeField] private float moveSpeed = 3.7f;
        [SerializeField] private float knockbackStrength = 9f;
        [SerializeField] private float attackCooldown = 2f;
        [SerializeField] private float roamRadius = 8f;

        private Vector3 spawnPosition;
        private Vector3 roamTarget;
        private float nextAttackTime;

        private void Awake()
        {
            spawnPosition = transform.position;
            PickRoamTarget();
        }

        private void Update()
        {
            if (!IsServer || RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active)
            {
                return;
            }

            var target = FindNearestPlayer();
            if (target != null)
            {
                MoveToward(target.transform.position);
                TryAttack(target);
                return;
            }

            if (Vector3.Distance(transform.position, roamTarget) < 1f)
            {
                PickRoamTarget();
            }

            MoveToward(roamTarget);
        }

        public void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            transform.position = spawnPosition;
            nextAttackTime = 0f;
            PickRoamTarget();
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

        private void MoveToward(Vector3 targetPosition)
        {
            var direction = targetPosition - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.01f)
            {
                return;
            }

            direction.Normalize();
            transform.position += direction * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), 12f * Time.deltaTime);
        }

        private void TryAttack(NetworkFirstPersonController player)
        {
            if (Time.time < nextAttackTime || Vector3.Distance(transform.position, player.transform.position) > attackDistance)
            {
                return;
            }

            nextAttackTime = Time.time + attackCooldown;
            var impulse = (player.transform.position - transform.position).normalized * knockbackStrength + Vector3.up * 2.5f;
            player.ServerForceDropHeld(impulse);
            player.KnockbackClientRpc(impulse);
        }

        private void PickRoamTarget()
        {
            var offset = new Vector3(Random.Range(-roamRadius, roamRadius), 0f, Random.Range(-roamRadius, roamRadius));
            roamTarget = spawnPosition + offset;
        }
    }
}
