using FriendSlop.Core;
using FriendSlop.Effects;
using FriendSlop.Player;
using FriendSlop.Round;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FriendSlop.Hazards
{
    // Network-replicated meteor that streaks from above a SphereWorld toward its surface.
    // Server owns motion + impact; clients see position via NetworkTransform plus a brief
    // local flash on impact. The meteor self-despawns when the round leaves Active so a
    // shower in flight doesn't carry into the success/fail screen.
    [RequireComponent(typeof(NetworkObject))]
    public class MeteorHazard : NetworkBehaviour
    {
        [Header("Motion")]
        [SerializeField, Min(1f)] private float fallSpeed = 16f;
        [SerializeField, Min(0f)] private float surfaceImpactOffset = 0.3f;
        // Hard timeout in case a meteor never reaches the surface (e.g. SphereWorld unloaded).
        [SerializeField, Min(1f)] private float maxLifetime = 18f;

        [Header("Impact")]
        // Damage AOE - matches telegraphRadius so the red warning circle reads as a
        // truthful "this is where the damage lands" indicator.
        [SerializeField, Min(0f)] private float blastRadius = 4.5f;
        [SerializeField, Min(0)] private int blastDamage = 70;
        // Minimum fraction of blastDamage applied to any player inside the AOE, regardless
        // of distance. Keeps glancing rim hits meaningful instead of letting players hug
        // the warning circle's edge for trivial damage.
        private const float MinDamageFloor = 0.4f;
        // Visual telegraph radius; keep equal to blastRadius unless deliberately tuning
        // damage to be wider/narrower than the warning disc.
        [SerializeField, Min(0f)] private float telegraphRadius = 4.5f;

        [Header("Visuals")]
        [SerializeField] private Color glowColor = new Color(1f, 0.55f, 0.2f, 1f);
        [SerializeField, Min(0f)] private float glowIntensity = 2.5f;
        [SerializeField, Min(0f)] private float spinSpeed = 240f;

        [Header("Impact Telegraph")]
        [SerializeField] private Color telegraphColor = new Color(1f, 0.1f, 0.1f, 1f);
        [SerializeField, Range(0f, 1f)] private float telegraphMinAlpha = 0.04f;
        [SerializeField, Range(0f, 1f)] private float telegraphMaxAlpha = 0.85f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private static Shader _cachedShader;

        private bool _initialized;
        private float _lifetime;
        private Material _runtimeMaterial;
        private Vector3 _spinAxis;
        private RoundManager _roundCache;

        private GameObject _telegraph;
        private Material _telegraphMaterial;
        private float _initialFallDistance;

        private void Awake()
        {
            _spinAxis = Random.onUnitSphere;
            if (_spinAxis.sqrMagnitude < 0.01f) _spinAxis = Vector3.up;
            _spinAxis.Normalize();
        }

        private void Start()
        {
            SetupVisuals();
        }

        public void ServerInitialize()
        {
            if (!IsServer) return;
            _initialized = true;
            _lifetime = 0f;
        }

        private void Update()
        {
            transform.Rotate(_spinAxis, spinSpeed * Time.deltaTime, Space.World);

            if (_telegraph == null) EnsureTelegraph();
            UpdateTelegraph();

            if (!IsServer || !_initialized) return;

            _lifetime += Time.deltaTime;
            if (_lifetime >= maxLifetime)
            {
                NetworkObject.Despawn();
                return;
            }

            if (_roundCache == null) _roundCache = RoundManagerRegistry.Current;
            if (_roundCache != null && _roundCache.Phase.Value != RoundPhase.Active)
            {
                NetworkObject.Despawn();
                return;
            }

            var world = SphereWorld.GetClosest(transform.position);
            if (world == null)
            {
                // No planet to fall toward - despawn so we don't drift forever.
                NetworkObject.Despawn();
                return;
            }

            var up = world.GetUp(transform.position);
            transform.position += -up * fallSpeed * Time.deltaTime;

            if (world.GetSurfaceDistance(transform.position) <= surfaceImpactOffset)
            {
                ResolveImpact(world);
            }
        }

        private void ResolveImpact(SphereWorld world)
        {
            var impactNormal = world.GetUp(transform.position);
            var impactPoint = world.GetSurfacePoint(impactNormal, surfaceImpactOffset);

            if (blastDamage > 0 && blastRadius > 0f)
            {
                var radiusSqr = blastRadius * blastRadius;
                foreach (var player in NetworkFirstPersonController.ActivePlayers)
                {
                    if (player == null || !player.IsSpawned || player.IsDead) continue;
                    var distSqr = (player.transform.position - impactPoint).sqrMagnitude;
                    if (distSqr > radiusSqr) continue;

                    // Quadratic falloff biased toward the center, but with a 40% floor so
                    // any clip on the AOE still hurts. Pure quadratic let rim hits trivialize
                    // standing in the warning - now the warning circle commits you to real damage.
                    var linear = 1f - Mathf.Sqrt(distSqr) / Mathf.Max(0.01f, blastRadius);
                    var falloff = Mathf.Lerp(MinDamageFloor, 1f, Mathf.Clamp01(linear * linear));
                    var damage = Mathf.Max(1, Mathf.RoundToInt(blastDamage * falloff));
                    player.ServerTakeDamage(damage);
                }
            }

            ImpactClientRpc(impactPoint, impactNormal);
            NetworkObject.Despawn();
        }

        [ClientRpc]
        private void ImpactClientRpc(Vector3 impactPoint, Vector3 impactNormal)
        {
            MeteorExplosionEffect.Spawn(impactPoint, impactNormal, blastRadius, glowColor);
        }

        private void SetupVisuals()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;

            var shader = GetMeteorShader();
            _runtimeMaterial = new Material(shader);
            _runtimeMaterial.EnableKeyword("_EMISSION");
            _runtimeMaterial.SetColor(BaseColorId, glowColor);
            _runtimeMaterial.SetColor(ColorId, glowColor);
            _runtimeMaterial.SetColor(EmissionColorId, glowColor * glowIntensity);
            rend.material = _runtimeMaterial;
        }

        private static Shader GetMeteorShader()
        {
            if (_cachedShader != null) return _cachedShader;
            _cachedShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return _cachedShader;
        }

        // Telegraph: a flat red disc projected onto the planet surface at the meteor's
        // future impact point. Created locally on every client (host included) - the visual
        // is purely cosmetic so it doesn't need network replication. Alpha ramps from
        // telegraphMinAlpha at spawn to telegraphMaxAlpha right before impact.
        private void EnsureTelegraph()
        {
            if (_telegraph != null) return;
            var world = SphereWorld.GetClosest(transform.position);
            if (world == null) return;

            var up = world.GetUp(transform.position);
            // Anchor the disc fractionally above the surface to avoid z-fighting with the planet mesh.
            var impactPoint = world.GetSurfacePoint(up, 0.05f);
            _initialFallDistance = Mathf.Max(0.1f,
                world.GetSurfaceDistance(transform.position) - surfaceImpactOffset);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "MeteorImpactTelegraph";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Don't parent under the SphereWorld - that GameObject is scaled (e.g. x72 on
            // Violet Giant) and would multiply our local scale, making the disc a giant
            // hovering shape in the sky. Instead, colocate the disc with the meteor's
            // scene so it tears down with the planet scene unload, then set world TRS
            // directly so scale stays exactly what we asked for.
            var meteorScene = gameObject.scene;
            if (meteorScene.IsValid() && meteorScene.isLoaded && go.scene != meteorScene)
                SceneManager.MoveGameObjectToScene(go, meteorScene);

            go.transform.position = impactPoint;
            // GetSurfaceRotation aligns local-up with the surface normal, so the cylinder's
            // round face lies flat on the surface and its height axis points outward.
            go.transform.rotation = world.GetSurfaceRotation(up, world.transform.forward);
            // Cylinder primitive: 1m diameter, 2m tall by default. Flatten Y to a thin disc.
            // Sized off telegraphRadius (visual) rather than blastRadius (damage) so the
            // disc size doesn't grow when we tune damage falloff distance.
            var diameter = telegraphRadius * 2f;
            go.transform.localScale = new Vector3(diameter, 0.01f, diameter);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                ApplyTelegraphMaterial(rend);
            }

            _telegraph = go;
        }

        private void ApplyTelegraphMaterial(Renderer rend)
        {
            var shader = GetMeteorShader();
            _telegraphMaterial = new Material(shader);
            // Mirror the URP transparent setup used by AnomalyOrb so the disc fades cleanly.
            _telegraphMaterial.SetFloat(SurfaceId, 1f);
            _telegraphMaterial.SetFloat(BlendId, 0f);
            _telegraphMaterial.SetFloat(SrcBlendId, 5f);
            _telegraphMaterial.SetFloat(DstBlendId, 10f);
            _telegraphMaterial.SetFloat(ZWriteId, 0f);
            _telegraphMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _telegraphMaterial.renderQueue = 3000;
            _telegraphMaterial.EnableKeyword("_EMISSION");
            var c = WithAlpha(telegraphColor, telegraphMinAlpha);
            _telegraphMaterial.SetColor(BaseColorId, c);
            _telegraphMaterial.SetColor(ColorId, c);
            _telegraphMaterial.SetColor(EmissionColorId, telegraphColor * 1.4f);
            rend.material = _telegraphMaterial;
        }

        private void UpdateTelegraph()
        {
            if (_telegraph == null || _telegraphMaterial == null) return;
            var world = SphereWorld.GetClosest(transform.position);
            if (world == null) return;

            var remaining = Mathf.Max(0f, world.GetSurfaceDistance(transform.position) - surfaceImpactOffset);
            var t = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.1f, _initialFallDistance));
            // Cubic ease-in: stays subtle for the first half of the fall then ramps fast,
            // matching how a real warning marker reads as the meteor closes in.
            var eased = t * t * t;
            var alpha = Mathf.Lerp(telegraphMinAlpha, telegraphMaxAlpha, eased);
            var c = WithAlpha(telegraphColor, alpha);
            _telegraphMaterial.SetColor(BaseColorId, c);
            _telegraphMaterial.SetColor(ColorId, c);
            _telegraphMaterial.SetColor(EmissionColorId, telegraphColor * Mathf.Lerp(0.4f, 2.2f, eased));
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public override void OnDestroy()
        {
            if (_telegraph != null) Destroy(_telegraph);
            base.OnDestroy();
        }
    }
}
