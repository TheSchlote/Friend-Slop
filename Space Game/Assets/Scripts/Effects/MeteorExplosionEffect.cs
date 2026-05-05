using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Effects
{
    // Local-only meteor impact VFX. Spawned from MeteorHazard's ClientRpc on every
    // client (host included). Drives an expanding fireball + ground shockwave + flash
    // light, then destroys itself. No networking; the AOE damage is already authoritative.
    public class MeteorExplosionEffect : MonoBehaviour
    {
        private const float Lifetime = 0.85f;

        private float _elapsed;
        private float _radius;
        private Color _color;
        private Vector3 _surfaceNormal;

        private Transform _core;
        private Material _coreMat;
        private Transform _ring;
        private Material _ringMat;
        private Light _flash;

        public static void Spawn(Vector3 position, Vector3 surfaceNormal, float radius, Color color)
        {
            var go = new GameObject("MeteorExplosion");
            go.transform.position = position;
            var fx = go.AddComponent<MeteorExplosionEffect>();
            fx._radius = Mathf.Max(0.5f, radius);
            fx._color = color;
            fx._surfaceNormal = surfaceNormal.sqrMagnitude > 0.001f
                ? surfaceNormal.normalized
                : Vector3.up;
            Destroy(go, Lifetime + 0.1f);
        }

        private void Start()
        {
            BuildCore();
            BuildRing();
            BuildFlash();
        }

        private void BuildCore()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.name = "Core";
            sphere.transform.SetParent(transform, false);
            sphere.transform.localPosition = _surfaceNormal * (_radius * 0.4f);
            sphere.transform.localScale = Vector3.one * 0.1f;

            var rend = sphere.GetComponent<MeshRenderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _coreMat = CreateGlowMat(_color, 0.95f);
            if (_coreMat != null) rend.material = _coreMat;
            _core = sphere.transform;
        }

        private void BuildRing()
        {
            // Flat shockwave that expands outward on the surface plane. Cylinder primitive
            // flattened to a thin disc and scaled wider over the lifetime.
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.name = "Shockwave";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = _surfaceNormal * 0.05f;
            // Align cylinder height axis to surface normal so the round face lies flat.
            var fwd = Vector3.Cross(_surfaceNormal, Vector3.right);
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.Cross(_surfaceNormal, Vector3.forward);
            ring.transform.rotation = Quaternion.LookRotation(fwd.normalized, _surfaceNormal);
            ring.transform.localScale = new Vector3(0.5f, 0.005f, 0.5f);

            var rend = ring.GetComponent<MeshRenderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _ringMat = CreateGlowMat(_color, 0.7f);
            if (_ringMat != null) rend.material = _ringMat;
            _ring = ring.transform;
        }

        private void BuildFlash()
        {
            var lightObj = new GameObject("Flash");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = _surfaceNormal * (_radius * 0.5f);
            _flash = lightObj.AddComponent<Light>();
            _flash.color = _color;
            _flash.intensity = 12f;
            _flash.range = _radius * 4f;
            _flash.shadows = LightShadows.None;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_elapsed / Lifetime);

            // Core: rapid expansion in the first 30%, then fade out.
            if (_core != null)
            {
                var growT = Mathf.Clamp01(_elapsed / (Lifetime * 0.3f));
                var grow = Mathf.SmoothStep(0f, 1f, growT);
                var scale = Mathf.Lerp(0.1f, _radius * 1.4f, grow);
                _core.localScale = Vector3.one * scale;
                if (_coreMat != null)
                {
                    var alpha = Mathf.Lerp(0.95f, 0f, t * t);
                    var emission = Mathf.Lerp(3.5f, 0.2f, t);
                    SetMatColor(_coreMat, _color, alpha, emission);
                }
            }

            // Shockwave: keeps expanding to ~1.5x radius, fades out earlier.
            if (_ring != null)
            {
                var ringSize = Mathf.Lerp(0.5f, _radius * 3f, Mathf.SmoothStep(0f, 1f, t));
                _ring.localScale = new Vector3(ringSize, 0.005f, ringSize);
                if (_ringMat != null)
                {
                    var alpha = Mathf.Lerp(0.7f, 0f, t);
                    SetMatColor(_ringMat, _color, alpha, Mathf.Lerp(2.5f, 0f, t));
                }
            }

            if (_flash != null)
            {
                _flash.intensity = Mathf.Lerp(12f, 0f, t);
                _flash.range = Mathf.Lerp(_radius * 4f, _radius * 1f, t);
            }
        }

        private void OnDestroy()
        {
            if (_coreMat != null) Destroy(_coreMat);
            if (_ringMat != null) Destroy(_ringMat);
        }

        private static void SetMatColor(Material mat, Color color, float alpha, float emission)
        {
            var c = new Color(color.r, color.g, color.b, alpha);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * emission);
        }

        private static Material CreateGlowMat(Color color, float alpha)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader);
            // URP transparent setup mirrored from BloodSplatter so this works under URP.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_EMISSION");
            mat.renderQueue = (int)RenderQueue.Transparent;
            SetMatColor(mat, color, alpha, 3f);
            return mat;
        }
    }
}
