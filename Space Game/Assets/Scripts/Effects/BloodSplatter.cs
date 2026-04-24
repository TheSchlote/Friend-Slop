using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Effects
{
    public class BloodSplatter : MonoBehaviour
    {
        private const float Lifetime = 10f;
        private const float FadeDelay = 5f;

        private float _elapsed;

        private readonly struct SplatterMat
        {
            public readonly Material Material;
            public readonly float StartAlpha;
            public SplatterMat(Material m, float a) { Material = m; StartAlpha = a; }
        }

        private readonly List<SplatterMat> _mats = new();

        public static void Spawn(Vector3 position, Vector3 surfaceNormal, int count)
        {
            var go = new GameObject("BloodSplatter");
            go.AddComponent<BloodSplatter>().Initialize(position, surfaceNormal, count);
            Destroy(go, Lifetime + 0.5f);
        }

        private void Initialize(Vector3 position, Vector3 surfaceNormal, int count)
        {
            var tangent = Vector3.Cross(surfaceNormal, Vector3.right);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(surfaceNormal, Vector3.forward);
            tangent.Normalize();
            var bitangent = Vector3.Cross(surfaceNormal, tangent).normalized;

            bool isUrp = GraphicsSettings.currentRenderPipeline != null;

            for (var i = 0; i < count; i++)
            {
                var offset = tangent * Random.Range(-0.45f, 0.45f)
                           + bitangent * Random.Range(-0.45f, 0.45f);
                var spotPos = position + surfaceNormal * 0.04f + offset;
                var rot = Quaternion.LookRotation(surfaceNormal, tangent)
                        * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                var scaleW = Random.Range(0.18f, 0.58f);
                var scaleH = Random.Range(0.08f, 0.32f);

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<MeshCollider>());
                quad.transform.SetParent(transform, false);
                quad.transform.SetPositionAndRotation(spotPos, rot);
                quad.transform.localScale = new Vector3(scaleW, scaleH, 1f);

                var mr = quad.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;

                var alpha = Random.Range(0.55f, 0.80f);
                var r = Random.Range(0.38f, 0.55f);
                var mat = isUrp ? CreateUrpMat(r, alpha) : CreateBuiltInMat(r, alpha);
                if (mat != null)
                    mr.material = mat;
                else
                    mr.material.color = new Color(r, 0f, 0f, 1f);

                _mats.Add(new SplatterMat(mr.material, alpha));
            }
        }

        private static Material CreateUrpMat(float r, float alpha)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(r, 0f, 0f, alpha));
            return mat;
        }

        private static Material CreateBuiltInMat(float r, float alpha)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.SetFloat("_Glossiness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetInt("_Cull", (int)CullMode.Off);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.color = new Color(r, 0f, 0f, alpha);
            return mat;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed < FadeDelay) return;

            var fade = 1f - Mathf.Clamp01((_elapsed - FadeDelay) / (Lifetime - FadeDelay));
            foreach (var item in _mats)
            {
                var c = item.Material.color;
                item.Material.color = new Color(c.r, c.g, c.b, item.StartAlpha * fade);
            }
        }
    }
}
