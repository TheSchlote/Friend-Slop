using UnityEngine;

namespace FriendSlop.Effects
{
<<<<<<< HEAD
    // Deterministic two-color vertical gradient for a planet sphere. Authoring twin of
    // FriendSlop.Core.PlanetColorRandomizer: same texture-bake technique (a 2x256 strip
    // applied to the renderer's mainTexture so standard sphere-primitive UVs distribute
    // bottomColor at the south pole and topColor at the north pole), but the two
    // colors are SerializeFields instead of randomized hues. Used by Ice Planet to read
    // as an iceberg - light icy crown, deep blue underside - rather than a random
    // session-roll. No networking required because the colors are fixed.
=======
    // Deterministic two-color vertical gradient for a planet sphere. Bakes a 2x256
    // strip into the renderer's mainTexture so standard sphere-primitive UVs distribute
    // bottomColor at the south pole and topColor at the north pole - the two colors
    // are SerializeFields. Used by Ice Planet to read as an iceberg: light icy crown,
    // deep blue underside. No networking required because the colors are fixed.
>>>>>>> origin/interiors-changes
    [RequireComponent(typeof(Renderer))]
    public class IcebergPlanetTint : MonoBehaviour
    {
        [SerializeField] private Color topColor = new(0.78f, 0.92f, 1f, 1f);
        [SerializeField] private Color bottomColor = new(0.06f, 0.16f, 0.42f, 1f);
        // Curve exponent applied to the lerp t. 1 = linear; >1 biases toward bottomColor
        // (more dark hull); <1 biases toward topColor. The default eases the iceberg
        // crown so the bright cap reads as a thinner band over a deeper underbelly.
        [SerializeField, Range(0.25f, 4f)] private float gradientPower = 1.4f;

        private void OnEnable()
        {
            ApplyGradient();
        }

        private void ApplyGradient()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;

            var tex = BuildGradientTexture(bottomColor, topColor, gradientPower);
            // material (not sharedMaterial) so this instance gets its own copy and we
<<<<<<< HEAD
            // don't tint every sphere primitive in the scene; matches the pattern in
            // PlanetColorRandomizer.ApplyGradient.
=======
            // don't tint every sphere primitive that happens to share the asset.
>>>>>>> origin/interiors-changes
            rend.material.mainTexture = tex;
            rend.material.color = Color.white;
        }

        private static Texture2D BuildGradientTexture(Color bottom, Color top, float power)
        {
            const int Height = 256;
            var tex = new Texture2D(2, Height, TextureFormat.RGB24, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "IcebergPlanetGradient",
            };
            for (var y = 0; y < Height; y++)
            {
                var t = y / (float)(Height - 1);
                var shaped = Mathf.Pow(t, power);
                var c = Color.Lerp(bottom, top, shaped);
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply(false, true);
            return tex;
        }
    }
}
