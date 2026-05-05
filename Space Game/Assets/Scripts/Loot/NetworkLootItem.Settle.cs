using UnityEngine;

namespace FriendSlop.Loot
{
    // Sphere gravity keeps applying tangential force to a rigidbody resting on a curved
    // planet, so flat or low-friction loot drifts forever and never crosses Unity's
    // auto-sleep threshold. We force a freeze on the server once the body has had time
    // to land; collisions wake it back up.
    public partial class NetworkLootItem
    {
        // Toggle to dump server-side physics state for every loot item. Verbose; expect to
        // disable after diagnosing a specific bug.
        private const bool LogSettleDiagnostics = true;

        // Velocity bands for an "early" freeze - if the body is genuinely calm we settle
        // sooner than the timeout. Generous because a flat box on a curved sphere produces
        // continuous narrowphase jitter that never crosses Unity's auto-sleep threshold.
        private const float SettleLinearSpeed = 1.0f;
        private const float SettleAngularSpeed = 2.0f;
        private const float SettleEarlyMinSeconds = 0.4f;

        // Hard timeout: regardless of velocity, freeze after this long. Catches the
        // pathological "vibrates forever" case.
        private const float SettleForceTimeoutSeconds = 1.5f;

        // Wake threshold (squared impulse). Below this we treat collision events as
        // residual contact noise and stay frozen.
        private const float SettleWakeImpulseSqr = 1.0f;

        private float _serverActiveSince = -1f;
        private float _nextSettleDiagnosticAt;

        private void FixedUpdate()
        {
            if (!IsServer) return;
            if (body == null || body.isKinematic) return;
            if (IsCarried.Value || IsDeposited.Value) return;

            if (_serverActiveSince < 0f)
            {
                _serverActiveSince = Time.time;
                _nextSettleDiagnosticAt = Time.time + 0.5f;
            }

            var elapsed = Time.time - _serverActiveSince;
            var linSpeed = body.linearVelocity.magnitude;
            var angSpeed = body.angularVelocity.magnitude;

            if (LogSettleDiagnostics && Time.time >= _nextSettleDiagnosticAt)
            {
                _nextSettleDiagnosticAt = Time.time + 0.5f;
                Debug.Log($"[Loot.Settle] {itemName} active={elapsed:F2}s lin={linSpeed:F2} ang={angSpeed:F2} pos={body.position} kinematic={body.isKinematic}");
            }

            var calmEnoughForEarlyFreeze = linSpeed < SettleLinearSpeed && angSpeed < SettleAngularSpeed;
            var hitForceTimeout = elapsed >= SettleForceTimeoutSeconds;
            var hitEarlyFreeze = elapsed >= SettleEarlyMinSeconds && calmEnoughForEarlyFreeze;

            if (hitForceTimeout || hitEarlyFreeze)
            {
                if (LogSettleDiagnostics)
                {
                    var reason = hitForceTimeout ? "timeout" : "calm";
                    Debug.Log($"[Loot.Settle] FREEZE {itemName} reason={reason} elapsed={elapsed:F2}s lin={linSpeed:F2} ang={angSpeed:F2}");
                }
                FreezeAtRest();
            }
        }

        private void FreezeAtRest()
        {
            if (body == null) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            if (sphericalGravity != null) sphericalGravity.enabled = false;
            _serverActiveSince = -1f;
        }

        private void TryWakeFromCollision(Collision collision)
        {
            if (body == null || !body.isKinematic) return;
            var impulseSqr = collision.impulse.sqrMagnitude;
            if (impulseSqr < SettleWakeImpulseSqr) return;

            if (LogSettleDiagnostics)
            {
                var otherName = collision.collider != null ? collision.collider.name : "?";
                Debug.Log($"[Loot.Settle] WAKE {itemName} by {otherName} impulse^2={impulseSqr:F3}");
            }

            body.isKinematic = false;
            if (sphericalGravity != null) sphericalGravity.enabled = true;
            _serverActiveSince = -1f;
        }
    }
}
