using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    [RequireComponent(typeof(NetworkObject))]
    public partial class RoamingMonster : NetworkBehaviour
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

        // Declaration order matters for NGO: deserializes in declaration order.
        // Keep _health before _isDead so any future reader of _isDead.OnValueChanged can
        // also read the matching health value if it ever needs to.
        private NetworkVariable<int> _health = new(100);
        private NetworkVariable<bool> _isDead = new(false);

        private enum State { Roaming, Chasing, Investigating }

        private static readonly float[] BodySampleHeights = { 0.25f, 0.5f, 0.75f, 0.95f };

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

        // Cached per-frame so FindNearestPlayer/CanDetectPlayer/IsOccluded don't each
        // recompute the same surface basis.
        private Vector3 _frameUp;
        private Vector3 _frameSurfaceForward;

        public override void OnNetworkSpawn()
        {
            _isDead.OnValueChanged += OnDeadChanged;
            if (_isDead.Value)
                ApplyDeadState();
        }

        public override void OnNetworkDespawn()
        {
            _isDead.OnValueChanged -= OnDeadChanged;
        }

        private void OnDeadChanged(bool previous, bool current)
        {
            if (current) ApplyDeadState();
            else ApplyAliveState();
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
            if (!IsServer || _isDead.Value || !RoundManagerRegistry.IsCurrentPhase(RoundPhase.Active))
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

            _frameUp = world.GetUp(transform.position);
            _frameSurfaceForward = Vector3.ProjectOnPlane(transform.forward, _frameUp);
            if (_frameSurfaceForward.sqrMagnitude < 0.001f)
                _frameSurfaceForward = transform.forward;
            else
                _frameSurfaceForward.Normalize();

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
            if (!IsServer || _isDead.Value) return;
            _health.Value = Mathf.Max(0, _health.Value - damage);
            if (_health.Value <= 0)
                _isDead.Value = true;
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
            _isDead.Value = false;

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

    }
}
