using FriendSlop.Core;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Hazards
{
    public enum AnomalyMovement { ChaseHostile, ChaseFriendly, Stationary, Wander }

    [RequireComponent(typeof(NetworkObject))]
    public class AnomalyOrb : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private AnomalyMovement movement = AnomalyMovement.Stationary;
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float detectionRange = 22f;
        [SerializeField] private float wanderRadius = 12f;
        [SerializeField] private float wanderPauseMin = 3f;
        [SerializeField] private float wanderPauseMax = 8f;

        [Header("Contact")]
        [SerializeField] private float contactRange = 1f;
        [SerializeField] private bool damageOnContact = false;
        [SerializeField] private int contactDamage = 15;
        [SerializeField] private bool healOnContact = false;
        [SerializeField] private int healAmount = 50;
        [SerializeField] private bool stunOnContact = false;
        [SerializeField] private float stunDuration = 2f;

        [Header("Hover")]
        [SerializeField] private float hoverHeight = 1f;
        [SerializeField] private float bobAmplitude = 0.75f;
        [SerializeField] private float bobFrequency = 2f;

        [Header("Visuals")]
        [SerializeField] private Color orbColor = Color.white;
        [SerializeField] private float glowPulseSpeed = 1.8f;
        [SerializeField] private float minAlpha = 0.1f;
        [SerializeField] private float maxAlpha = 0.3f;
        [SerializeField] private float minGlow = 0.6f;
        [SerializeField] private float maxGlow = 1.8f;

        // Cached shader property IDs — string lookups are dictionary hits per call.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        // Shader.Find is a registry string lookup — cache once across all orbs.
        private static Shader _cachedOrbShader;
        private static bool _orbShaderIsUrp;

        // Target acquisition is throttled — players don't teleport; 10Hz is invisible.
        private const float TargetSearchInterval = 0.1f;

        private Vector3 _spawnPosition;
        private Vector3 _wanderTarget;
        private float _wanderPauseTimer;
        private float _bobPhaseOffset;
        private bool _initialized;
        private bool _wasRoundActive;
        private Material _orbMaterial;
        private NetworkFirstPersonController _cachedTarget;
        private float _nextTargetSearchTime;

        private void Awake()
        {
            _bobPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Start()
        {
            SetupVisuals();
        }

        private static Shader GetOrbShader()
        {
            if (_cachedOrbShader != null) return _cachedOrbShader;
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s != null) { _cachedOrbShader = s; _orbShaderIsUrp = true; return s; }
            s = Shader.Find("Standard");
            _cachedOrbShader = s;
            _orbShaderIsUrp = false;
            return s;
        }

        private void SetupVisuals()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;

            var shader = GetOrbShader();
            _orbMaterial = new Material(shader);

            if (_orbShaderIsUrp)
            {
                _orbMaterial.SetFloat(SurfaceId, 1f);
                _orbMaterial.SetFloat(BlendId, 0f);
                _orbMaterial.SetFloat(SrcBlendId, 5f);
                _orbMaterial.SetFloat(DstBlendId, 10f);
                _orbMaterial.SetFloat(ZWriteId, 0f);
                _orbMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                _orbMaterial.SetFloat(ModeId, 3f);
                _orbMaterial.SetInt(SrcBlendId, 5);
                _orbMaterial.SetInt(DstBlendId, 10);
                _orbMaterial.SetInt(ZWriteId, 0);
                _orbMaterial.EnableKeyword("_ALPHABLEND_ON");
            }

            _orbMaterial.renderQueue = 3000;
            _orbMaterial.SetFloat(SmoothnessId, 0.8f);
            _orbMaterial.EnableKeyword("_EMISSION");
            rend.material = _orbMaterial;
        }

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
            _wanderPauseTimer = Random.Range(wanderPauseMin, wanderPauseMax);
            var world = SphereWorld.GetClosest(_spawnPosition);
            if (world != null)
                transform.position = world.GetSurfacePoint(world.GetUp(_spawnPosition), hoverHeight);
        }

        private void Update()
        {
            UpdateVisuals();

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
                switch (movement)
                {
                    case AnomalyMovement.ChaseHostile:
                        UpdateChaseHostile(world);
                        break;
                    case AnomalyMovement.ChaseFriendly:
                        UpdateChaseFriendly(world);
                        break;
                    case AnomalyMovement.Wander:
                        UpdateWander(world);
                        break;
                    // Stationary: hover only
                }

                var contacted = CheckPlayerContact(world);
                if (contacted != null)
                {
                    if (healOnContact)   contacted.ServerHeal(healAmount);
                    if (damageOnContact) contacted.ServerTakeDamage(contactDamage);
                    if (stunOnContact)   contacted.StunClientRpc(stunDuration, Vector3.zero);
                    NetworkObject.Despawn();
                    return;
                }
            }

            ApplyHover(world);
        }

        private void UpdateVisuals()
        {
            if (_orbMaterial == null) return;
            var pulse = (Mathf.Sin(Time.time * glowPulseSpeed + _bobPhaseOffset) + 1f) * 0.5f;
            var alpha = Mathf.Lerp(minAlpha, maxAlpha, pulse);
            var glow  = Mathf.Lerp(minGlow, maxGlow, pulse);
            var c = new Color(orbColor.r, orbColor.g, orbColor.b, alpha);
            _orbMaterial.SetColor(BaseColorId, c);
            _orbMaterial.SetColor(ColorId, c);
            _orbMaterial.SetColor(EmissionColorId, orbColor * glow);
        }

        // Throttled target lookup. Re-uses last result for ~100ms; gameplay-imperceptible
        // but cuts the linecast-per-player cost from 60Hz to 10Hz.
        private NetworkFirstPersonController GetChaseTarget()
        {
            if (Time.time >= _nextTargetSearchTime
                || _cachedTarget == null
                || !_cachedTarget.IsSpawned
                || _cachedTarget.IsDead)
            {
                _nextTargetSearchTime = Time.time + TargetSearchInterval;
                _cachedTarget = FindNearestVisiblePlayer();
            }
            return _cachedTarget;
        }

        private void UpdateChaseHostile(SphereWorld world)
        {
            var target = GetChaseTarget();
            if (target == null) return;
            MoveHorizontallyToward(world, target.transform.position);
        }

        private void UpdateChaseFriendly(SphereWorld world)
        {
            var target = GetChaseTarget();
            if (target == null) return;
            var dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > contactRange + 0.3f)
                MoveHorizontallyToward(world, target.transform.position);
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

        private NetworkFirstPersonController CheckPlayerContact(SphereWorld world)
        {
            var up = world != null ? world.GetUp(transform.position) : Vector3.up;
            NetworkFirstPersonController closest = null;
            var closestDist = float.MaxValue;
            var contactSqr = contactRange * contactRange;
            foreach (var player in NetworkFirstPersonController.ActivePlayers)
            {
                if (player == null || !player.IsSpawned || player.IsDead) continue;
                var horizontalSqr = Vector3.ProjectOnPlane(player.transform.position - transform.position, up).sqrMagnitude;
                if (horizontalSqr <= contactSqr && horizontalSqr < closestDist)
                {
                    closestDist = horizontalSqr;
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
