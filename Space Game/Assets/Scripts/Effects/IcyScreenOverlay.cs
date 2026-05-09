using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.Effects
{
    // Local-only HUD effect: a translucent ice-blue vignette around the screen edges
    // that fades in as the slow takes hold and fades back out as it expires. Built
    // procedurally so authors don't have to wire a Canvas or sprite asset; the player
    // controller spawns one of these on the local owner the first time an ice-mine
    // slow lands. Lifetime tracks the slow timer the controller passes in.
    public class IcyScreenOverlay : MonoBehaviour
    {
        // Hand-authored procedural sprite. Cached statically so multiple slow ticks
        // don't allocate fresh textures - one per process is enough.
        private static Sprite _cachedFrostSprite;

        // Fade-in is faster than fade-out so the hit reads instantly but the recovery
        // tail lingers; both are forgiving enough that re-applying a slow mid-effect
        // doesn't pop the alpha around.
        private const float FadeOutWindowSeconds = 0.6f;
        private const float FadeInSpeedPerSecond = 5f;
        private const float FadeOutSpeedPerSecond = 1.6f;
        private const float MaxAlpha = 0.85f;

        private Canvas _canvas;
        private Image _frost;
        private float _remaining;
        private float _currentAlpha;

        private void Awake()
        {
            BuildCanvas();
            _frost.color = new Color(_frost.color.r, _frost.color.g, _frost.color.b, 0f);
            _canvas.enabled = false;
        }

        public void Show(float duration)
        {
            if (duration <= 0f) return;
            // Refresh-only: if the controller re-applies a slow on top of an active one,
            // adopt the longer remaining time without resetting the fade-in mid-flight.
            if (duration > _remaining) _remaining = duration;
            if (_canvas != null) _canvas.enabled = true;
        }

        private void Update()
        {
            // Hold full alpha while comfortably inside the slow window, then ramp the
            // target alpha back down as we cross into the final FadeOutWindowSeconds so
            // the frost fades out in lockstep with the slow expiring.
            float targetAlpha;
            if (_remaining > 0f)
            {
                _remaining = Mathf.Max(0f, _remaining - Time.deltaTime);
                targetAlpha = _remaining <= FadeOutWindowSeconds
                    ? Mathf.Lerp(0f, MaxAlpha, _remaining / FadeOutWindowSeconds)
                    : MaxAlpha;
            }
            else
            {
                targetAlpha = 0f;
            }

            var rate = targetAlpha > _currentAlpha ? FadeInSpeedPerSecond : FadeOutSpeedPerSecond;
            _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, rate * Time.deltaTime);

            var c = _frost.color;
            c.a = _currentAlpha;
            _frost.color = c;

            if (_remaining <= 0f && _currentAlpha <= 0.01f && _canvas != null && _canvas.enabled)
                _canvas.enabled = false;
        }

        private void OnDestroy()
        {
            if (_canvas != null && _canvas.gameObject != null)
                Destroy(_canvas.gameObject);
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("IcyScreenOverlayCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Sit above the regular HUD - the slow is a global "you got hit" cue and
            // shouldn't be hidden by chat or compass widgets.
            _canvas.sortingOrder = 1000;
            canvasGo.AddComponent<CanvasScaler>();

            var imageGo = new GameObject("Frost");
            imageGo.transform.SetParent(canvasGo.transform, false);
            _frost = imageGo.AddComponent<Image>();
            _frost.sprite = GetOrCreateFrostSprite();
            _frost.type = Image.Type.Simple;
            _frost.preserveAspect = false;
            _frost.raycastTarget = false;
            _frost.color = new Color(0.78f, 0.92f, 1f, 0f);

            var rect = _frost.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Sprite GetOrCreateFrostSprite()
        {
            if (_cachedFrostSprite != null) return _cachedFrostSprite;

            // 256-pixel radial vignette: transparent in the middle, ice-blue at the
            // outer edge. Stretching this to fill an arbitrary aspect-ratio screen
            // produces an elliptical fringe, which matches the "frost on the edges"
            // brief - a perfectly circular fringe would clip on widescreen.
            const int Size = 256;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "IcyScreenFrost",
            };

            var center = (Size - 1) * 0.5f;
            var maxDist = center;
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var dx = (x - center) / maxDist;
                    var dy = (y - center) / maxDist;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Inner ~55% radius is fully transparent so gameplay readability
                    // isn't destroyed; alpha ramps up to opaque toward the corners.
                    var t = Mathf.Clamp01((d - 0.55f) / 0.5f);
                    var alpha = Mathf.Pow(t, 1.6f);
                    var r = (byte)(0.78f * 255f);
                    var g = (byte)(0.92f * 255f);
                    var b = (byte)(1.0f * 255f);
                    pixels[y * Size + x] = new Color32(r, g, b, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            _cachedFrostSprite = Sprite.Create(
                tex,
                new Rect(0, 0, Size, Size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            _cachedFrostSprite.name = "IcyScreenFrost";
            return _cachedFrostSprite;
        }
    }
}
