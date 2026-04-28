using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    public enum AnomalyType { Friendly, Hostile, NeutralStationary, NeutralWandering, ElectricHostile }

    [RequireComponent(typeof(NetworkObject))]
    public class AnomalyOrb : NetworkBehaviour
    {
        [SerializeField] private AnomalyType anomalyType = AnomalyType.NeutralStationary;
        [SerializeField] private float hoverHeight = 1.8f;
        [SerializeField] private float bobAmplitude = 0.25f;
        [SerializeField] private float bobFrequency = 1.1f;
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float detectionRange = 22f;
        [SerializeField] private float friendlyStopDistance = 3f;
        [SerializeField] private float hostileContactRange = 1.0f;
        [SerializeField] private int hostileDamage = 15;
        [SerializeField] private float hostileAttackCooldown = 1.5f;
        [SerializeField] private float wanderRadius = 12f;
        [SerializeField] private float wanderPauseMin = 3f;
        [SerializeField] private float wanderPauseMax = 8f;
        [SerializeField] private int healAmount = 50;
        [SerializeField] private int shockDamage = 20;
        [SerializeField] private float shockStunDuration = 2f;
        [SerializeField] private float shockCooldown = 4f;

        private Vector3 _spawnPosition;
        private Vector3 _wanderTarget;
        private float _wanderPauseTimer;
        private float _nextAttackTime;
        private float _bobPhaseOffset;
        private bool _initialized;
        private bool _wasRoundActive;

        public void ServerInitialize(Vector3 spawnPosition)
        {
            if (!IsServer) return;
            _spawnPosition = spawnPosition;
            _bobPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
            _wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
            _initialized = true;

            var world = SphereWorld.GetClosest(spawnPosition);
            if (world != null)
                transform.position = world.GetSurfacePoint(world.GetUp(spawnPosition), hoverHeight);
        }

        public void ServerReset()
        {
            if (!IsServer || !_initialized) return;
            _nextAttackTime = 0f;
            _wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
            var world = SphereWorld.GetClosest(_spawnPosition);
            if (world != null)
                transform.position = world.GetSurfacePoint(world.GetUp(_spawnPosition), hoverHeight);
        }

        private void Update()
        {
            if (!IsServer || !_initialized) return;

            var world = SphereWorld.GetClosest(transform.position);
            if (world == null) return;

            var roundActive = RoundManager.Instance?.Phase.Value == RoundPhase.Active;

            if (_wasRoundActive && !roundActive)
            {
                _wasRoundActive = false;
                NetworkObject.Despawn();
                return;
            }
            _wasRoundActive = roundActive;

            if (roundActive)
            {
                switch (anomalyType)
                {
                    case AnomalyType.Friendly:
                        UpdateFriendly(world);
                        break;
                    case AnomalyType.Hostile:
                        UpdateHostile(world);
                        break;
                    case AnomalyType.NeutralWandering:
                        UpdateWander(world);
                        break;
                    case AnomalyType.ElectricHostile:
                        UpdateElectric(world);
                        break;
                    // NeutralStationary: no movement, only hover
                }

                var contacted = CheckPlayerContact();
                if (contacted != null)
                {
                    if (anomalyType == AnomalyType.Friendly)
                        contacted.ServerHeal(healAmount);
                    NetworkObject.Despawn();
                    return;
                }
            }

            ApplyHover(world);
        }

        private void UpdateFriendly(SphereWorld world)
        {
            var target = FindNearestVisiblePlayer();
            if (target == null) return;
            var dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > friendlyStopDistance + 0.3f)
                MoveHorizontallyToward(world, target.transform.position);
        }

        private void UpdateHostile(SphereWorld world)
        {
            var target = FindNearestVisiblePlayer();
            if (target == null) return;

            MoveHorizontallyToward(world, target.transform.position);

            var dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist <= hostileContactRange && Time.time >= _nextAttackTime && !target.IsDead)
            {
                _nextAttackTime = Time.time + hostileAttackCooldown;
                target.ServerTakeDamage(hostileDamage);
            }
        }

        private void UpdateElectric(SphereWorld world)
        {
            var target = FindNearestVisiblePlayer();
            if (target == null) return;

            MoveHorizontallyToward(world, target.transform.position);

            var dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist <= hostileContactRange && Time.time >= _nextAttackTime && !target.IsDead)
            {
                _nextAttackTime = Time.time + shockCooldown;
                target.ServerTakeDamage(shockDamage);
                target.StunClientRpc(shockStunDuration, Vector3.zero);
            }
        }

        private void UpdateWander(SphereWorld world)
        {
            if (_wanderPauseTimer > 0f)
            {
                _wanderPauseTimer -= Time.deltaTime;
                return;
            }

            if (Vector3.Distance(transform.position, _wanderTarget) < 1.2f)
            {
                _wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
                PickWanderTarget(world);
                return;
            }

            MoveHorizontallyToward(world, _wanderTarget, moveSpeed * 0.5f);
        }

        private void MoveHorizontallyToward(SphereWorld world, Vector3 target, float speed = -1f)
        {
            if (speed < 0f) speed = moveSpeed;
            var up = world.GetUp(transform.position);
            var flat = Vector3.ProjectOnPlane(target - transform.position, up);
            if (flat.sqrMagnitude < 0.001f) return;
            transform.position += flat.normalized * speed * Time.deltaTime;
        }

        private void ApplyHover(SphereWorld world)
        {
            var bob = Mathf.Sin(Time.time * bobFrequency + _bobPhaseOffset) * bobAmplitude;
            var up = world.GetUp(transform.position);
            transform.position = world.GetSurfacePoint(up, hoverHeight + bob);
        }

        private NetworkFirstPersonController CheckPlayerContact()
        {
            var range = anomalyType == AnomalyType.Friendly ? friendlyStopDistance : hostileContactRange;
            NetworkFirstPersonController closest = null;
            var closestDist = float.MaxValue;
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned || player.IsDead) continue;
                var dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist <= range && dist < closestDist)
                {
                    closestDist = dist;
                    closest = player;
                }
            }
            return closest;
        }

        private NetworkFirstPersonController FindNearestVisiblePlayer()
        {
            NetworkFirstPersonController best = null;
            var bestDistSq = detectionRange * detectionRange;

            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned || player.IsDead) continue;
                var distSq = (transform.position - player.transform.position).sqrMagnitude;
                if (distSq >= bestDistSq) continue;
                if (!HasLineOfSight(player)) continue;
                bestDistSq = distSq;
                best = player;
            }

            return best;
        }

        private bool HasLineOfSight(NetworkFirstPersonController player)
        {
            if (!Physics.Linecast(transform.position, player.transform.position, out var hit))
                return true;
            return hit.collider != null && hit.collider.transform.root == player.transform.root;
        }

        private void PickWanderTarget(SphereWorld world)
        {
            if (world == null) { _wanderTarget = _spawnPosition; return; }
            var up = world.GetUp(_spawnPosition);
            var tangent = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.ProjectOnPlane(Vector3.right, up);
            tangent.Normalize();
            var dist = Random.Range(wanderRadius * 0.3f, wanderRadius);
            var offset = _spawnPosition + tangent * dist;
            _wanderTarget = world.GetSurfacePoint(world.GetUp(offset), hoverHeight);
        }
    }
}
