using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public class LaserGun : NetworkLootItem
    {
        [SerializeField] private float range = 40f;
        [SerializeField] private int damage = 25;
        [SerializeField] private int monsterDamage = 40;
        [SerializeField] private float fireCooldown = 0.35f;
        [SerializeField] private int maxAmmo = 10;
        [SerializeField] private LayerMask fireMask = ~0;

        private NetworkVariable<int> _ammo = new(0);
        private float _nextFireTime;

        public bool CanFireNow => Time.time >= _nextFireTime && _ammo.Value > 0;
        public float CooldownRemaining => Mathf.Max(0f, _nextFireTime - Time.time);
        public int Ammo => _ammo.Value;
        public int MaxAmmo => maxAmmo;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
                _ammo.Value = maxAmmo;
        }

        public override string GetPrompt(NetworkFirstPersonController player)
        {
            if (IsHeldBy(player.OwnerClientId))
                return _ammo.Value > 0
                    ? $"Laser Gun [{_ammo.Value}/{maxAmmo}]: Hold Left-click fire  |  Q drop"
                    : "Laser Gun [EMPTY]  |  Q drop";

            if (player.InventoryCount >= NetworkFirstPersonController.InventorySize)
                return "Inventory full (Laser Gun)";

            return $"E pick up Laser Gun (ammo: {_ammo.Value}/{maxAmmo})";
        }

        public void StartLocalCooldown()
        {
            _nextFireTime = Time.time + fireCooldown;
        }

        public override void ServerReset()
        {
            base.ServerReset();
            if (IsServer)
                _ammo.Value = maxAmmo;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestFireServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
        {
            var shooterId = rpcParams.Receive.SenderClientId;
            var shooter = NetworkFirstPersonController.FindByClientId(shooterId);
            if (shooter == null || shooter.IsDead || !IsHeldBy(shooterId)) return;
            if (RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active) return;
            if (_ammo.Value <= 0) return;

            if (Vector3.SqrMagnitude(origin - shooter.transform.position) > 16f)
                origin = shooter.transform.position + shooter.transform.forward * 1.5f;

            direction = direction.normalized;
            if (direction.sqrMagnitude < 0.5f) return;

            _ammo.Value--;

            if (!Physics.Raycast(origin, direction, out var hit, range, fireMask, QueryTriggerInteraction.Ignore))
                return;

            if (hit.collider == null) return;

            var targetPlayer = hit.collider.GetComponentInParent<NetworkFirstPersonController>();
            if (targetPlayer != null && targetPlayer != shooter && !targetPlayer.IsDead)
            {
                targetPlayer.ServerTakeDamage(damage);
                return;
            }

            var monster = hit.collider.GetComponentInParent<RoamingMonster>();
            if (monster != null)
                monster.ServerTakeDamage(monsterDamage);
        }
    }
}
