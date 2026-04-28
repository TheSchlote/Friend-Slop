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
        [SerializeField] private float detectionRange = 30f;
        [SerializeField] private float attackDistance = 2f;
        [SerializeField] private float moveSpeed = 3.78f;
        [SerializeField] private float knockbackStrength = 9f;
        [SerializeField] private float attackCooldown = 2f;
        [SerializeField] private float roamRadius = 8f;
        [SerializeField] private float surfaceHeight = 0.65f;
        [SerializeField] private float rotationSharpness = 10f;
        [SerializeField] private float investigateDuration = 7f;
        [SerializeField] private float investigateWanderRadius = 5f;
        [SerializeField] private float roamPauseMin = 5f;
        [SerializeField] private float roamPauseMax = 10f;
        [SerializeField] private float roundStartGraceSeconds = 3f;
        [SerializeField] private float spottedPauseMin = 0.4f;
        [SerializeField] private float spottedPauseMax = .8f;
        [SerializeField] private float visionAngle = 60f;
        [SerializeField] private float visionOriginHeight = 1.2f;
        [SerializeField] private float proximityDetectionRange = 2.5f;
        [SerializeField] private float targetSwitchRange = 5f;
        [SerializeField] private float stunDuration = 1.5f;
        [SerializeField] private int attackDamage = 20;
        [SerializeField] private int maxHealth = 100;

        private NetworkVariable<int> _health = new(100);

        private enum State { Roaming, Chasing, Investigating }

        private static readonly float[] BodySampleHeights = { 0.25f, 0.5f, 0.75f, 0.95f };

        private bool _isDead;
        private Renderer[] _renderers;
        private CapsuleCollider _capsuleCollider;

        private Vector3 spawnPosition;
        private Vector3 roamTarget;
        private float nextAttackTime;
        private State _state = State.Roaming;
        private Vector3 _lastKnownPosition;
        private float _investigateTimer;
        private Vector3 _investigateTarget;
        private float _roamPauseTimer;
        private float _spottedPauseTimer;
        private NetworkFirstPersonController _chaseTarget;
        private NetworkFirstPersonController _stunnedPlayer;
        private float _stunnedPlayerUntil;
        private bool _roundActive;

        public override void OnNetworkSpawn()
        {
            if (_health.Value <= 0)
                ApplyDeadState();
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _capsuleCollider = GetComponent<CapsuleCollider>();

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
            if (!IsServer || _isDead || RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active)
            {
                _roundActive = false;
                return;
            }

            var world = SphereWorld.GetClosest(transform.position);
            if (world == null)
            {
                return;
            }

            if (!_roundActive)
            {
                // Give players a brief head-start, then pick a fresh target so the monster
                // actually leaves spawn instead of potentially camping there.
                _roundActive = true;
                _state = State.Roaming;
                _chaseTarget = null;
                _roamPauseTimer = Mathf.Max(0f, roundStartGraceSeconds);
                PickRoamTarget(world);
            }

            var nearest = FindNearestPlayer(world);

            switch (_state)
            {
                case State.Roaming:
                    if (nearest != null)
                    {
                        _chaseTarget = nearest;
                        _state = State.Chasing;
                        _spottedPauseTimer = Random.Range(spottedPauseMin, spottedPauseMax);
                        break;
                    }
                    if (_roamPauseTimer > 0f)
                    {
                        _roamPauseTimer -= Time.deltaTime;
                        break;
                    }
                    if (Vector3.Distance(transform.position, roamTarget) < 1.1f)
                    {
                        _roamPauseTimer = Random.Range(roamPauseMin, roamPauseMax);
                        PickRoamTarget(world);
                        break;
                    }
                    MoveToward(world, roamTarget);
                    break;

                case State.Chasing:
                    if (_spottedPauseTimer > 0f)
                    {
                        _spottedPauseTimer -= Time.deltaTime;
                        break;
                    }
                    // Switch to a closer visible player if one enters targetSwitchRange
                    if (nearest != null && nearest != _chaseTarget
                        && Vector3.Distance(transform.position, nearest.transform.position) < targetSwitchRange)
                    {
                        _chaseTarget = nearest;
                    }
                    if (CanDetectPlayer(world, _chaseTarget))
                    {
                        _lastKnownPosition = _chaseTarget.transform.position;
                        MoveToward(world, _chaseTarget.transform.position);
                        if (TryAttack(world, _chaseTarget))
                        {
                            _lastKnownPosition = _chaseTarget.transform.position;
                            _state = State.Roaming;
                            PickRetreatTarget(world, _chaseTarget.transform.position);
                        }
                    }
                    else
                    {
                        _state = State.Investigating;
                        _investigateTimer = investigateDuration;
                        PickInvestigateTarget(world);
                    }
                    break;

                case State.Investigating:
                    if (nearest != null)
                    {
                        _chaseTarget = nearest;
                        _state = State.Chasing;
                        _spottedPauseTimer = 0f;
                        goto case State.Chasing;
                    }
                    _investigateTimer -= Time.deltaTime;
                    if (_investigateTimer <= 0f)
                    {
                        _state = State.Roaming;
                        PickRoamTarget(world);
                        break;
                    }
                    if (Vector3.Distance(transform.position, _investigateTarget) < 1.1f)
                        PickInvestigateTarget(world);
                    MoveToward(world, _investigateTarget);
                    break;
            }
        }

        public void ServerTakeDamage(int damage)
        {
            if (!IsServer || _isDead) return;
            _health.Value = Mathf.Max(0, _health.Value - damage);
            if (_health.Value <= 0)
            {
                _isDead = true;
                DieClientRpc();
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DieClientRpc()
        {
            _isDead = true;
            ApplyDeadState();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReviveClientRpc()
        {
            _isDead = false;
            ApplyAliveState();
        }

        private void ApplyDeadState()
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = false;
            if (_capsuleCollider != null) _capsuleCollider.enabled = false;
        }

        private void ApplyAliveState()
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = true;
            if (_capsuleCollider != null) _capsuleCollider.enabled = true;
        }

        public void ServerReset()
        {
            if (!IsServer)
            {
                return;
            }

            _health.Value = maxHealth;
            _isDead = false;
            ReviveClientRpc();

            transform.position = spawnPosition;
            nextAttackTime = 0f;
            _state = State.Roaming;
            _investigateTimer = 0f;
            _roamPauseTimer = 0f;
            _roundActive = false;
            _chaseTarget = null;
            _stunnedPlayer = null;
            _stunnedPlayerUntil = 0f;
            var world = SphereWorld.GetClosest(transform.position);
            AlignToSurface(world, transform.forward, 1f);
            PickRoamTarget(world);
        }

        private NetworkFirstPersonController FindNearestPlayer(SphereWorld world)
        {
            NetworkFirstPersonController bestPlayer = null;
            var bestDistance = detectionRange;

            var up = world.GetUp(transform.position);
            var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
            if (surfaceForward.sqrMagnitude < 0.001f)
                surfaceForward = transform.forward;
            else
                surfaceForward.Normalize();

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
                var visibility = ComputeBodyVisibility(world, player);
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

        private float ComputeBodyVisibility(SphereWorld world, NetworkFirstPersonController player)
        {
            if (player == null || world == null) return 0f;

            var origin = transform.position + world.GetUp(transform.position) * visionOriginHeight;
            var up = world.GetUp(player.transform.position);
            var bodyHeight = Mathf.Max(0.1f, player.CurrentBodyHeight);
            var visible = 0;

            for (var i = 0; i < BodySampleHeights.Length; i++)
            {
                var point = player.transform.position + up * (bodyHeight * BodySampleHeights[i]);
                if (!Physics.Linecast(origin, point, out var hit))
                {
                    visible++;
                    continue;
                }

                if (hit.collider != null && hit.collider.transform.root == player.transform.root)
                    visible++;
            }

            return (float)visible / BodySampleHeights.Length;
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

            var up = world.GetUp(transform.position);
            var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
            if (surfaceForward.sqrMagnitude < 0.001f)
                surfaceForward = transform.forward;
            else
                surfaceForward.Normalize();

            var toPlayer = Vector3.ProjectOnPlane(player.transform.position - transform.position, up);
            if (toPlayer.sqrMagnitude < 0.001f)
                return false;

            if (Vector3.Angle(surfaceForward, toPlayer) > visionAngle * 0.5f)
                return false;

            return !IsOccluded(world, player);
        }

        private bool IsOccluded(SphereWorld world, NetworkFirstPersonController player)
        {
            var origin      = transform.position              + world.GetUp(transform.position)              * visionOriginHeight;
            var destination = player.transform.position + world.GetUp(player.transform.position) * visionOriginHeight;

            if (!Physics.Linecast(origin, destination, out var hit))
                return false;

            return hit.collider.transform.root != player.transform.root;
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
