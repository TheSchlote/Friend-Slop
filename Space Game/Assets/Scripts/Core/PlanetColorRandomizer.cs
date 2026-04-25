using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Core
{
    public class PlanetColorRandomizer : NetworkBehaviour
    {
        private readonly NetworkVariable<Color> _colorA = new(Color.green);
        private readonly NetworkVariable<Color> _colorB = new(Color.blue);

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _colorA.Value = RandomPlanetColor(Random.Range(0f, 1f));
                float h2 = (GetHue(_colorA.Value) + Random.Range(0.15f, 0.40f)) % 1f;
                _colorB.Value = RandomPlanetColor(h2);
            }

            ApplyGradient(_colorA.Value, _colorB.Value);
            _colorA.OnValueChanged += (_, v) => ApplyGradient(v, _colorB.Value);
            _colorB.OnValueChanged += (_, v) => ApplyGradient(_colorA.Value, v);
        }

        private static Color RandomPlanetColor(float hue)
        {
            float s = Random.Range(0.55f, 1.0f);
            float v = Random.Range(0.38f, 0.82f);
            return Color.HSVToRGB(hue, s, v);
        }

        private static float GetHue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h;
        }

        private void ApplyGradient(Color south, Color north)
        {
            var tex = new Texture2D(2, 256, TextureFormat.RGB24, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 256; y++)
            {
                Color c = Color.Lerp(south, north, y / 255f);
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();

            var rend = GetComponent<Renderer>();
            rend.material.mainTexture = tex;
            rend.material.color = Color.white;
        }
    }
}
