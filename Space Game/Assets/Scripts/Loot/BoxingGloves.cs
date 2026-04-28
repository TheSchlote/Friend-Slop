using FriendSlop.Core;
using FriendSlop.Hazards;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Loot
{
    public class BoxingGloves : NetworkLootItem
    {
        [SerializeField] private float punchRange = 2.8f;
        [SerializeField] private float punchRadius = 0.45f;
        [SerializeField] private float punchImpulse = 14f;
        [SerializeField] private float punchUpwardBias = 4f;
        [SerializeField] private float punchStunDuration = 7f;
        [SerializeField] private float punchCooldown = 0.9f;
        [SerializeField] private int punchMonsterDamage = 25;
        [SerializeField] private LayerMask punchMask = ~0;

        // Per-client cooldown — not networked, applied immediately on the owner
        // when they fire a punch so the button can't be spammed before the RPC round-trips.
        private float _nextPunchTime;

        public bool CanPunchNow => Time.time >= _nextPunchTime;
        public float CooldownRemaining => Mathf.Max(0f, _nextPunchTime - Time.time);

        public override string GetPrompt(NetworkFirstPersonController player)
        {
            if (IsHeldBy(player.OwnerClientId))
                return "Boxing Gloves: Left-click punch  |  Q drop";

            if (player.InventoryCount >= NetworkFirstPersonController.InventorySize)
                return "Inventory full (Boxing Gloves)";

            return "E pick up Boxing Gloves";
        }

        // Called by PlayerInteractor on the owner before sending the RPC so the
        // button feel is instant without waiting for a server round-trip.
        public void StartLocalCooldown()
        {
            _nextPunchTime = Time.time + punchCooldown;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPunchServerRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
        {
            var attackerId = rpcParams.Receive.SenderClientId;
            var attacker = NetworkFirstPersonController.FindByClientId(attackerId);
            if (attacker == null || attacker.IsDead || !IsHeldBy(attackerId)) return;
            if (RoundManager.Instance == null || RoundManager.Instance.Phase.Value != RoundPhase.Active) return;

            // Clamp the RPC origin so a modified client can't reach across the map.
            if (Vector3.SqrMagnitude(origin - attacker.transform.position) > 9f)
                origin = attacker.transform.position + attacker.transform.forward * 1.2f;

            direction = direction.normalized;
            if (direction.sqrMagnitude < 0.5f) return;

            if (!Physics.SphereCast(origin, punchRadius, direction, out var hit,
                    punchRange, punchMask, QueryTriggerInteraction.Ignore))
                return;

            if (hit.collider == null) return;
            var target = hit.collider.GetComponentInParent<NetworkFirstPersonController>();
            if (target == null || target == attacker || target.IsDead)
            {
                var monster = hit.collider.GetComponentInParent<RoamingMonster>();
                if (monster != null) monster.ServerTakeDamage(punchMonsterDamage);
                return;
            }

            var up = FlatGravityVolume.GetGravityUp(target.transform.position);
            var horizontal = Vector3.ProjectOnPlane(direction, up);
            if (horizontal.sqrMagnitude < 0.001f) horizontal = direction;
            var impulse = horizontal.normalized * punchImpulse + up * punchUpwardBias;

            target.StunClientRpc(punchStunDuration, impulse);
        }
    }
}
