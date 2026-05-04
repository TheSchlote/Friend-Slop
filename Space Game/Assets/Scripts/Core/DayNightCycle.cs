using System.Collections.Generic;
using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Core
{
    public class DayNightCycle : NetworkBehaviour
    {
        [SerializeField] private float dayLengthSeconds = 240f;
        [SerializeField] private float orbitRadius = 110f;
        [SerializeField] private float sunScale = 7f;

        // Server advances this; clients read it every Update for smooth rendering.
        private readonly NetworkVariable<float> _timeOfDay = new(
            0.22f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Light _dirLight;
        private Transform _sunVisual;
        private readonly List<Light> _fillLights = new();
        private Material _skyboxInstance;
        private Material _originalSkybox;
        private Material _sunMaterial;
        private Material _haloMaterial;

        public Vector3 SunWorldPosition { get; private set; }
        public float SunElevation { get; private set; }

        // Colour constants
        private static readonly Color DayLightColor   = new Color(1.00f, 0.98f, 0.88f);
        private static readonly Color DawnLightColor  = new Color(1.00f, 0.52f, 0.22f);
        private static readonly Color DayAmbient      = new Color(0.28f, 0.30f, 0.32f);
        private static readonly Color NightAmbient    = new Color(0.02f, 0.03f, 0.07f);
        private static readonly Color DayFog          = new Color(0.030f, 0.040f, 0.045f);
        private static readonly Color NightFog        = new Color(0.008f, 0.008f, 0.022f);
        private static readonly Color DawnFog         = new Color(0.080f, 0.038f, 0.020f);

        public override void OnNetworkSpawn()
        {
            DayNightCycleRegistry.Register(this);

            var lightObj = GameObject.Find("Tiny Planet Sun");
            if (lightObj != null) _dirLight = lightObj.GetComponent<Light>();

            for (var i = 1; i <= 3; i++)
            {
                var fillObj = GameObject.Find($"Cheap Planet Fill Light {i}");
                var l = fillObj != null ? fillObj.GetComponent<Light>() : null;
                if (l != null) _fillLights.Add(l);
            }

            // The procedural skybox renders its own sun disk based on the directional light.
            // Suppress it so only our physical sphere is visible.
            _originalSkybox = RenderSettings.skybox;
            if (_originalSkybox != null)
            {
                _skyboxInstance = new Material(_originalSkybox);
                if (_skyboxInstance.HasProperty("_SunSize"))
                    _skyboxInstance.SetFloat("_SunSize", 0f);
                RenderSettings.skybox = _skyboxInstance;
            }

            CreateSunVisual();
            ApplyTime(_timeOfDay.Value);
        }

        public override void OnNetworkDespawn()
        {
            DayNightCycleRegistry.Unregister(this);

            // Restore the original skybox material.
            if (_originalSkybox != null)
                RenderSettings.skybox = _originalSkybox;
            if (_skyboxInstance != null)
                Destroy(_skyboxInstance);
            _skyboxInstance = null;
            _originalSkybox = null;
            if (_sunMaterial != null) Destroy(_sunMaterial);
            if (_haloMaterial != null) Destroy(_haloMaterial);
            _sunMaterial = null;
            _haloMaterial = null;

            // The sun visual lives on the _dirLight object — only remove the components we added.
            if (_dirLight != null)
            {
                _dirLight.gameObject.transform.localScale = Vector3.one;
                var halo = _dirLight.transform.Find("Sun Halo");
                if (halo != null) Destroy(halo.gameObject);
                var mr = _dirLight.GetComponent<MeshRenderer>();
                if (mr != null) Destroy(mr);
                var mf = _dirLight.GetComponent<MeshFilter>();
                if (mf != null) Destroy(mf);
            }
            // Clean up any leftover standalone sphere from a previous code path.
            var stale = GameObject.Find("Sun Visual");
            if (stale != null) Destroy(stale);
            _sunVisual = null;
            _fillLights.Clear();
            _dirLight = null;
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
                _timeOfDay.Value = (_timeOfDay.Value + Time.deltaTime / dayLengthSeconds) % 1f;
            ApplyTime(_timeOfDay.Value);
        }

        // --- Sun visual ---

        private void CreateSunVisual()
        {
            // Remove any leftover standalone sphere from a previous code path.
            var stale = GameObject.Find("Sun Visual");
            if (stale != null) Destroy(stale);

            // Merge the visual directly onto the existing directional light object so there
            // is only ever one sun GameObject in the scene.
            if (_dirLight == null) return;

            var sunGo = _dirLight.gameObject;
            sunGo.transform.localScale = Vector3.one * sunScale;

            // Borrow the sphere mesh from a temporary primitive rather than keeping the whole object.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);

            if (sunGo.GetComponent<MeshFilter>() == null)
                sunGo.AddComponent<MeshFilter>().sharedMesh = sphereMesh;

            if (sunGo.GetComponent<MeshRenderer>() == null)
            {
                var mr = sunGo.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _sunMaterial = MakeSunMat();
                mr.sharedMaterial = _sunMaterial;
            }

            // Halo — larger transparent child sphere for a soft glow ring.
            if (sunGo.transform.Find("Sun Halo") == null)
            {
                var haloGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                haloGo.name = "Sun Halo";
                haloGo.transform.SetParent(sunGo.transform, false);
                haloGo.transform.localScale = Vector3.one * 1.65f;
                Destroy(haloGo.GetComponent<Collider>());
                var haloMr = haloGo.GetComponent<MeshRenderer>();
                haloMr.shadowCastingMode = ShadowCastingMode.Off;
                haloMr.receiveShadows = false;
                _haloMaterial = MakeHaloMat();
                haloMr.sharedMaterial = _haloMaterial;
            }

            _sunVisual = sunGo.transform;
        }

        private static Material MakeSunMat()
        {
            var isUrp = GraphicsSettings.currentRenderPipeline != null;
            if (isUrp)
            {
                // Unlit so the sphere is always at full brightness, ignoring scene lights.
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit");
                var mat = new Material(shader);
                // HDR colour — looks like a glowing disk even without post-processing bloom.
                mat.SetColor("_BaseColor", new Color(3.8f, 3.0f, 1.6f, 1f));
                return mat;
            }
            else
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.SetColor("_Color", Color.white);
                mat.SetColor("_EmissionColor", new Color(3.8f, 3.0f, 1.6f));
                mat.EnableKeyword("_EMISSION");
                return mat;
            }
        }

        private static Material MakeHaloMat()
        {
            var isUrp = GraphicsSettings.currentRenderPipeline != null;
            if (isUrp)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Universal Render Pipeline/Unlit");
                var mat = new Material(shader);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetColor("_BaseColor", new Color(1f, 0.62f, 0.18f, 0.24f));
                mat.renderQueue = 3000;
                return mat;
            }
            else
            {
                var mat = new Material(Shader.Find("Standard"));
                mat.SetFloat("_Mode", 3f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.color = new Color(1f, 0.62f, 0.18f, 0.24f);
                mat.renderQueue = 3000;
                return mat;
            }
        }

        // --- Time application ---

        private void ApplyTime(float t)
        {
            // t=0 → east (+X), t=0.25 → noon (+Y), t=0.5 → west (−X), t=0.75 → midnight (−Y)
            var angle  = t * Mathf.PI * 2f;
            var sunDir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

            SunWorldPosition = sunDir * orbitRadius;
            SunElevation     = sunDir.y;

            if (_sunVisual != null)
                _sunVisual.position = SunWorldPosition;

            // Directional light — points FROM sun TOWARD planet
            if (_dirLight != null)
            {
                _dirLight.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.forward);

                var elev = Mathf.Max(0f, sunDir.y);
                _dirLight.intensity = Mathf.Pow(elev, 0.4f) * 1.45f;

                // Warm orange at horizon, white-yellow at noon
                var horizonBlend = 1f - Mathf.Clamp01(sunDir.y * 5f);
                var aboveHorizon = Mathf.Clamp01(sunDir.y * 10f + 1f);
                _dirLight.color = Color.Lerp(DayLightColor, DawnLightColor, horizonBlend * aboveHorizon);
            }

            // Local sun elevation: dot of the player's surface normal against the sun direction.
            // Each client computes this independently — night-side players get a negative value.
            var localPlayer = LocalPlayerRegistry.Current;
            var playerPos   = localPlayer != null && localPlayer.IsSpawned ? localPlayer.transform.position : Vector3.zero;
            var localSunElevation  = playerPos.sqrMagnitude > 0.01f
                ? Vector3.Dot(playerPos.normalized, sunDir)
                : sunDir.y;
            var localSkyBrightness = Mathf.Clamp01(localSunElevation * 1.5f + 0.12f);

            // Drive the player camera background colour so the sky fades from blue to black
            // based on the player's actual position on the planet, not the global sun elevation.
            var localCam = localPlayer?.PlayerCamera;
            if (localCam != null)
            {
                localCam.clearFlags = CameraClearFlags.SolidColor;
                var terminator = Mathf.Clamp01((0.18f - Mathf.Abs(localSunElevation)) / 0.18f)
                               * Mathf.Clamp01(localSunElevation * 8f + 1f);
                var nightSky = new Color(0.01f, 0.01f, 0.04f);
                var daySky   = new Color(0.38f, 0.60f, 0.82f);
                var dawnSky  = new Color(0.72f, 0.42f, 0.22f);
                var skyColor = Color.Lerp(nightSky, daySky, localSkyBrightness);
                skyColor = Color.Lerp(skyColor, dawnSky, terminator * 0.55f);
                localCam.backgroundColor = skyColor;
            }

            // Move fill lights to the sun's side every frame so the opposite hemisphere stays dark.
            var fillIntensity = Mathf.Lerp(0f, 1.1f, localSkyBrightness);
            var perpFill = Mathf.Abs(Vector3.Dot(sunDir, Vector3.forward)) < 0.9f
                ? Vector3.Cross(sunDir, Vector3.forward).normalized
                : Vector3.Cross(sunDir, Vector3.right).normalized;
            var fillDist = orbitRadius * 0.4f;
            if (_fillLights.Count > 0 && _fillLights[0] != null)
            {
                _fillLights[0].transform.position = sunDir * fillDist;
                _fillLights[0].intensity = fillIntensity;
            }
            if (_fillLights.Count > 1 && _fillLights[1] != null)
            {
                _fillLights[1].transform.position = (sunDir + perpFill * 0.6f).normalized * fillDist;
                _fillLights[1].intensity = fillIntensity;
            }
            if (_fillLights.Count > 2 && _fillLights[2] != null)
            {
                _fillLights[2].transform.position = (sunDir - perpFill * 0.6f).normalized * fillDist;
                _fillLights[2].intensity = fillIntensity;
            }

            RenderSettings.ambientLight = Color.Lerp(NightAmbient, DayAmbient, localSkyBrightness);

            var dawnDusk = Mathf.Clamp01((0.18f - Mathf.Abs(localSunElevation)) / 0.18f)
                         * Mathf.Clamp01(localSunElevation * 8f + 1f);
            RenderSettings.fogColor = Color.Lerp(
                Color.Lerp(NightFog, DayFog, localSkyBrightness),
                DawnFog,
                dawnDusk * 0.65f);
        }
    }
}
