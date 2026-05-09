using FriendSlop.Effects;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Player
{
    public partial class NetworkFirstPersonController
    {
        // Ice-mine slow timer + multiplicative speed factor. Authoritative on the local
        // owner only - the server fires ApplyIceSlowClientRpc and the owner runs the
        // timer, multiplies its own speed, and shows the screen frost. Other clients
        // don't need this state because movement is owner-driven.
        private float _iceSlowTimer;
        private float _iceSlowFactor = 1f;
        private IcyScreenOverlay _iceOverlay;

        public float IceSlowSpeedMultiplier => _iceSlowTimer > 0f ? _iceSlowFactor : 1f;
        public bool IsIceSlowed => _iceSlowTimer > 0f;
        public float IceSlowSecondsRemaining => Mathf.Max(0f, _iceSlowTimer);

        [ClientRpc]
        public void ApplyIceSlowClientRpc(float duration, float factor)
        {
            // Slow is purely a local-feel effect: only the owner runs the input loop,
            // so this is the only client that needs to track it. Skipping non-owners
            // also avoids spawning a frost canvas on every spectator's screen.
            if (!IsOwner) return;
            if (duration <= 0f) return;

            // Stack rule: keep the longer remaining time AND the harsher factor (lower
            // means more slow). A weaker tag from a second mine never erases a stronger
            // one already in flight.
            var clamped = Mathf.Clamp(factor, 0.05f, 1f);
            if (_iceSlowTimer <= 0f || clamped < _iceSlowFactor)
                _iceSlowFactor = clamped;
            if (duration > _iceSlowTimer)
                _iceSlowTimer = duration;

            EnsureIceOverlay()?.Show(duration);
        }

        private void TickIceSlow(float dt)
        {
            if (_iceSlowTimer <= 0f) return;
            _iceSlowTimer = Mathf.Max(0f, _iceSlowTimer - dt);
            if (_iceSlowTimer <= 0f) _iceSlowFactor = 1f;
        }

        private IcyScreenOverlay EnsureIceOverlay()
        {
            if (_iceOverlay != null) return _iceOverlay;
            // Parent the overlay to the player so it dies with the NetworkObject. The
            // overlay itself parents a screen-space Canvas under itself; we don't need
            // to manage that Canvas's lifetime explicitly.
            var go = new GameObject("IcyScreenOverlay");
            go.transform.SetParent(transform, false);
            _iceOverlay = go.AddComponent<IcyScreenOverlay>();
            return _iceOverlay;
        }
    }
}
