using System.Collections;
using UnityEngine;

namespace FriendSlop.Interiors
{
    // Add to a Canvas with a CanvasGroup. Listens for InteriorEvents and fades in/out.
    [RequireComponent(typeof(CanvasGroup))]
    public class InteriorLoadingScreen : MonoBehaviour
    {
        [SerializeField] private float fadeDuration = 0.3f;

        private CanvasGroup _group;
        private Coroutine _fade;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            SetVisible(false, instant: true);
        }

        private void OnEnable()  => InteriorEvents.LoadingStateChanged += OnLoadingStateChanged;
        private void OnDisable() => InteriorEvents.LoadingStateChanged -= OnLoadingStateChanged;

        private void OnLoadingStateChanged(bool visible)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeTo(visible ? 1f : 0f));
        }

        private IEnumerator FadeTo(float target)
        {
            _group.blocksRaycasts = true;
            float start = _group.alpha;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(start, target, t / fadeDuration);
                yield return null;
            }
            _group.alpha = target;
            _group.blocksRaycasts = target > 0.5f;
            _group.interactable    = target > 0.5f;
        }

        private void SetVisible(bool visible, bool instant)
        {
            _group.alpha          = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable   = visible;
        }
    }
}
