using System.Collections;
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
        private float _nextLocalFireTime;
        private float _nextServerFireTime;
        private LineRenderer _laserLine;
        private Coroutine _hideLaserCoroutine;

        // Shader.Find is a registry string lookup — cache once across all guns.
        private static Shader _cachedLaserShader;

        public bool CanFireNow => Time.time >= _nextLocalFireTime && _ammo.Value > 0;
        public float CooldownRemaining => Mathf.Max(0f, _nextLocalFireTime - Time.time);
        public int Ammo => _ammo.Value;
        public int MaxAmmo => maxAmmo;

        private void Start()
        {
            _laserLine = gameObject.AddComponent<LineRenderer>();
            _laserLine.positionCount = 2;
            _laserLine.startWidth = 0.04f;
            _laserLine.endWidth = 0.01f;
            _laserLine.useWorldSpace = true;
            _laserLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laserLine.receiveShadows = false;
            _laserLine.startColor = Color.red;
            _laserLine.endColor = new Color(1f, 0.2f, 0.2f, 0f);
            if (_cachedLaserShader == null) _cachedLaserShader = Shader.Find("Sprites/Default");
            _laserLine.material = new Material(_cachedLaserShader);
            _laserLine.enabled = false;
        }

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
            _nextLocalFireTime = Time.time + fireCooldown;
        }

        public override void ServerReset()
        {
            base.ServerReset();
            if (IsServer)
            {
                _ammo.Value = maxAmmo;
                _nextServerFireTime = 0f;
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestFireServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
        {
            var shooterId = rpcParams.Receive.SenderClientId;
            var shooter = NetworkFirstPersonController.FindByClientId(shooterId);
            if (shooter == null || shooter.IsDead || !IsHeldBy(shooterId)) return;
            if (RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active) return;
            if (_ammo.Value <= 0) return;
            if (Time.time < _nextServerFireTime) return;

            if (Vector3.SqrMagnitude(origin - shooter.transform.position) > 16f)
                origin = shooter.transform.position + shooter.transform.forward * 1.5f;

            direction = direction.normalized;
            if (direction.sqrMagnitude < 0.5f) return;

            _nextServerFireTime = Time.time + fireCooldown;
            _ammo.Value--;

            var endpoint = origin + direction * range;
            if (Physics.Raycast(origin, direction, out var hit, range, fireMask, QueryTriggerInteraction.Ignore) && hit.collider != null)
            {
                endpoint = hit.point;

                var targetPlayer = hit.collider.GetComponentInParent<NetworkFirstPersonController>();
                if (targetPlayer != null && targetPlayer != shooter && !targetPlayer.IsDead)
                {
                    targetPlayer.ServerTakeDamage(damage);
                }
                else
                {
                    var monster = hit.collider.GetComponentInParent<RoamingMonster>();
                    if (monster != null)
                        monster.ServerTakeDamage(monsterDamage);
                }
            }

            ShowLaserClientRpc(origin, endpoint);
        }

        [Rpc(SendTo.Everyone)]
        private void ShowLaserClientRpc(Vector3 from, Vector3 to)
        {
            if (_laserLine == null) return;
            _laserLine.SetPosition(0, from);
            _laserLine.SetPosition(1, to);
            _laserLine.enabled = true;
            if (_hideLaserCoroutine != null) StopCoroutine(_hideLaserCoroutine);
            _hideLaserCoroutine = StartCoroutine(HideLaserAfterDelay());
        }

        private IEnumerator HideLaserAfterDelay()
        {
            yield return new WaitForSeconds(0.12f);
            if (_laserLine != null) _laserLine.enabled = false;
        }
    }
}
