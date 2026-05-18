using FriendSlop.Round;
using UnityEngine;

namespace FriendSlop.UI
{
    // Transient "objective met - go extract" banner. Driven per-frame from
    // Update() off server-replicated objective state; no authority or RPC
    // involvement. The text latches once when the state flips ready so it is
    // not rebuilt every frame; alpha fades and pulses for urgency.
    public partial class FriendSlopUI
    {
        private void UpdateObjectiveBanner()
        {
            if (_objectiveBannerText == null) return;

            var round = RoundManagerRegistry.Current;
            var objective = round != null ? round.ActiveObjective : null;
            var ready = round != null &&
                        round.Phase.Value == RoundPhase.Active &&
                        objective != null &&
                        objective.IsExtractionReady(round);

            if (ready)
            {
                if (!_objectiveBannerLatched)
                {
                    _objectiveBannerLatched = true;
                    _objectiveBannerText.text = objective.BuildExtractionBanner(round);
                }
            }
            else
            {
                _objectiveBannerLatched = false;
            }

            var target = ready ? 1f : 0f;
            _objectiveBannerAlpha = Mathf.MoveTowards(
                _objectiveBannerAlpha, target, ObjectiveBannerFadeSpeed * Time.deltaTime);

            var visible = _objectiveBannerAlpha > 0.001f;
            if (_objectiveBannerText.gameObject.activeSelf != visible)
                _objectiveBannerText.gameObject.SetActive(visible);
            if (!visible) return;

            var pulse = 0.82f + 0.18f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * ObjectiveBannerPulseSpeed));
            _objectiveBannerText.color = new Color(1f, 0.86f, 0.28f, _objectiveBannerAlpha * pulse);
        }
    }
}
