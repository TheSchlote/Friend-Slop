using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Player
{
    // Health, damage, death, revive, and the post-death spectate camera.
    // Server owns the health NetworkVariable; ClientRpcs handle the
    // local-only camera detach and blood VFX. Damage flash is fired through
    // the LocalPlayerDamaged static event in the main partial so UI never
    // takes a direct dependency on this class.
    public partial class NetworkFirstPersonController
    {
        [Header("Health")]
        [SerializeField] private int maxHealth = 100;

        private readonly NetworkVariable<int> _health = new(100);
        private bool _isDead;
        private float _deathOverheadTimer;
        private bool _spectating;
        private int _spectatorIndex;
        private readonly List<NetworkFirstPersonController> _aliveSpectateTargets = new();
        private string _spectatorTargetLabel = string.Empty;
        private int _spectatorTargetLabelAliveCount = -1;
        private int _spectatorTargetLabelIndex = -1;
        private ulong _spectatorTargetLabelClientId = ulong.MaxValue;

        public bool IsDead => _health.Value <= 0;
        public float HealthPercent => maxHealth > 0 ? Mathf.Clamp01((float)_health.Value / maxHealth) : 0f;
        public int CurrentHealth => _health.Value;
        public int MaxHealth => maxHealth;
        public bool IsDeadLocally => IsOwner && _isDead;
        public bool IsSpectatingLocally => IsOwner && _isDead && _spectating;

        public string SpectatorTargetLabel => _spectating ? _spectatorTargetLabel : string.Empty;

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous && current > 0)
                LocalPlayerDamaged?.Invoke();
        }

        public void ServerHeal(int amount)
        {
            if (!IsServer || _health.Value <= 0) return;
            _health.Value = Mathf.Min(maxHealth, _health.Value + amount);
        }

        public void ServerTakeDamage(int damage)
        {
            if (!IsServer || _health.Value <= 0) return;
            _health.Value = Mathf.Max(0, _health.Value - damage);
            var isDeath = _health.Value <= 0;
            if (isDeath)
            {
                ServerForceDropHeld(Vector3.zero);
                if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
                if (IsBeingCarried.Value)
                    FindByClientId(CarriedByClientId.Value)?.ServerDropHeldPlayer(Vector3.zero);
                DieClientRpc();
            }
            if (damage > 10)
                SpawnBloodSplatterClientRpc(damage, isDeath);
        }

        [ClientRpc]
        private void SpawnBloodSplatterClientRpc(int damage, bool isDeath)
        {
            var count = Mathf.RoundToInt(Mathf.Lerp(1f, 6f, (damage - 10f) / 90f));
            if (isDeath) count += 4;
            var up = transform.up;
            var world = SphereWorld.GetClosest(transform.position);
            var groundPos = world != null ? world.GetSurfacePoint(up, 0.06f) : transform.position + up * 0.06f;
            BloodSplatter.Spawn(groundPos, up, count);
        }

        public void ServerRevive()
        {
            if (!IsServer) return;
            ServerForceDropHeld(Vector3.zero);
            if (_heldPlayer != null) ServerDropHeldPlayer(Vector3.zero);
            if (IsBeingCarried.Value)
            {
                var carrier = FindByClientId(CarriedByClientId.Value);
                carrier?.ServerDropHeldPlayer(Vector3.zero);
            }
            _health.Value = maxHealth;
            ReviveClientRpc();
        }

        [ClientRpc]
        private void DieClientRpc()
        {
            if (!IsOwner) return;
            _isDead = true;
            _spectating = false;
            _deathOverheadTimer = 5f;
            _spectatorIndex = 0;
            ResetSpectatorTargetLabel();
            if (characterController != null) characterController.enabled = false;
            if (playerCamera != null) playerCamera.transform.SetParent(null);
        }

        [ClientRpc]
        private void ReviveClientRpc()
        {
            if (!IsOwner) return;
            _isDead = false;
            _spectating = false;
            _deathOverheadTimer = 0f;
            ResetSpectatorTargetLabel();
            if (playerCamera != null && cameraRoot != null)
            {
                playerCamera.transform.SetParent(cameraRoot);
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
            if (characterController != null) characterController.enabled = true;
            knockbackVelocity = Vector3.zero;
            radialSpeed = 0f;
            _stunTimer = 0f;
            _currentTiltAngle = 0f;
        }

        private void HandleDeathCamera()
        {
            if (playerCamera == null) return;

            var world = SphereWorld.GetClosest(transform.position);
            var up = world != null ? world.GetUp(transform.position) : Vector3.up;

            if (!_spectating)
            {
                _deathOverheadTimer -= Time.deltaTime;

                var overheadPos = transform.position + up * 8f;
                playerCamera.transform.position = overheadPos;
                var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (surfaceForward.sqrMagnitude < 0.001f) surfaceForward = transform.right;
                else surfaceForward.Normalize();
                playerCamera.transform.rotation = Quaternion.LookRotation(-up, surfaceForward);

                if (_deathOverheadTimer <= 0f)
                    _spectating = true;
                return;
            }

            RefreshAliveSpectateTargets();

            if (Keyboard.current != null && !GameplayInputState.IsBlocked)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame) CycleSpectateTarget(1, _aliveSpectateTargets.Count);
                if (Keyboard.current.qKey.wasPressedThisFrame) CycleSpectateTarget(-1, _aliveSpectateTargets.Count);
            }

            if (_aliveSpectateTargets.Count > 0)
            {
                _spectatorIndex = ((_spectatorIndex % _aliveSpectateTargets.Count) + _aliveSpectateTargets.Count) % _aliveSpectateTargets.Count;
                var target = _aliveSpectateTargets[_spectatorIndex];
                UpdateSpectatorTargetLabel(_aliveSpectateTargets.Count, target);
                if (target.PlayerCamera != null)
                {
                    playerCamera.transform.position = target.PlayerCamera.transform.position;
                    playerCamera.transform.rotation = target.PlayerCamera.transform.rotation;
                }
            }
            else
            {
                UpdateSpectatorTargetLabel(0, null);
                var overheadPos = transform.position + up * 8f;
                playerCamera.transform.position = overheadPos;
                var surfaceForward = Vector3.ProjectOnPlane(transform.forward, up);
                if (surfaceForward.sqrMagnitude < 0.001f) surfaceForward = transform.right;
                else surfaceForward.Normalize();
                playerCamera.transform.rotation = Quaternion.LookRotation(-up, surfaceForward);
            }
        }

        private void CycleSpectateTarget(int direction)
        {
            CycleSpectateTarget(direction, CountAliveSpectateTargets());
        }

        private void CycleSpectateTarget(int direction, int aliveCount)
        {
            if (aliveCount == 0) return;
            _spectatorIndex = ((_spectatorIndex + direction) % aliveCount + aliveCount) % aliveCount;
        }

        private int CountAliveSpectateTargets()
        {
            var aliveCount = 0;
            foreach (var p in ActivePlayers)
            {
                if (p != null && p != this && p.IsSpawned && !p.IsDead)
                    aliveCount++;
            }
            return aliveCount;
        }

        private void RefreshAliveSpectateTargets()
        {
            _aliveSpectateTargets.Clear();
            foreach (var p in ActivePlayers)
            {
                if (p != null && p != this && p.IsSpawned && !p.IsDead)
                    _aliveSpectateTargets.Add(p);
            }
        }

        private void UpdateSpectatorTargetLabel(int aliveCount, NetworkFirstPersonController target)
        {
            var targetClientId = target != null ? target.OwnerClientId : ulong.MaxValue;
            if (_spectatorTargetLabelAliveCount == aliveCount
                && _spectatorTargetLabelIndex == _spectatorIndex
                && _spectatorTargetLabelClientId == targetClientId)
            {
                return;
            }

            _spectatorTargetLabelAliveCount = aliveCount;
            _spectatorTargetLabelIndex = _spectatorIndex;
            _spectatorTargetLabelClientId = targetClientId;

            if (target == null)
            {
                _spectatorTargetLabel = "nobody";
                return;
            }

            var serverId = NetworkManager.Singleton != null ? NetworkManager.ServerClientId : 0UL;
            _spectatorTargetLabel = target.OwnerClientId == serverId ? "Host" : $"Player {target.OwnerClientId}";
        }

        private void ResetSpectatorTargetLabel()
        {
            _spectatorTargetLabel = string.Empty;
            _spectatorTargetLabelAliveCount = -1;
            _spectatorTargetLabelIndex = -1;
            _spectatorTargetLabelClientId = ulong.MaxValue;
        }
    }
}
