using FriendSlop.Loot;
using UnityEngine;

namespace FriendSlop.UI
{
    // Renders a small 3D preview of each inventory slot's item to a single RenderTexture.
    // The rig sits at a remote world position with a tight orthographic camera so its render
    // doesn't conflict with the gameplay scene. Each slot has a "stage" transform; cloning
    // an item's MeshFilter/MeshRenderer into a stage produces the preview without ever
    // touching the live NetworkLootItem.
    public class InventoryPreviewRig : MonoBehaviour
    {
        private const float RemoteY = -100000f;
        private const float StageSpacing = 2.4f;
        private const float StageHalfHeight = 1.0f;
        private const float CameraDistance = 4f;
        private const int SlotPixelSize = 256;

        private int slotCount;
        private Transform[] stages;
        private GameObject[] previewInstances;
        private NetworkLootItem[] currentItems;
        private Camera previewCamera;
        public RenderTexture RenderTexture { get; private set; }

        public void Initialize(int slots)
        {
            slotCount = Mathf.Max(1, slots);
            transform.position = new Vector3(0f, RemoteY, 0f);

            stages = new Transform[slotCount];
            previewInstances = new GameObject[slotCount];
            currentItems = new NetworkLootItem[slotCount];

            for (var i = 0; i < slotCount; i++)
            {
                var stageObject = new GameObject($"Stage_{i}");
                stageObject.transform.SetParent(transform, false);
                var x = (i - (slotCount - 1) * 0.5f) * StageSpacing;
                stageObject.transform.localPosition = new Vector3(x, 0f, 0f);
                stages[i] = stageObject.transform;
            }

            BuildCamera();
            BuildLights();
            BuildRenderTexture();
        }

        private void BuildCamera()
        {
            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);
            cameraObject.transform.localRotation = Quaternion.identity;

            previewCamera = cameraObject.AddComponent<Camera>();
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = StageHalfHeight;
            previewCamera.aspect = (slotCount * StageSpacing) / (StageHalfHeight * 2f);
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 12f;
            previewCamera.depth = -50f;
            // Render last so we don't depend on the gameplay camera, but cull tightly via
            // the orthographic frustum at our remote position — nothing else is down here.
            previewCamera.useOcclusionCulling = false;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
        }

        private void BuildLights()
        {
            // Point lights with finite range so they don't bleed back up into the gameplay
            // scene 100k units above us.
            BuildPointLight("PreviewKey", new Vector3(1.2f, 1.6f, -1.5f), new Color(1f, 0.96f, 0.88f), intensity: 4.5f);
            BuildPointLight("PreviewFill", new Vector3(-1.5f, -0.8f, -1.0f), new Color(0.62f, 0.78f, 0.95f), intensity: 2.0f);
            BuildPointLight("PreviewRim", new Vector3(0f, 0.4f, 1.6f), new Color(0.95f, 0.7f, 1f), intensity: 2.4f);
        }

        private void BuildPointLight(string name, Vector3 localPos, Color color, float intensity)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = localPos;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 8f;
            light.shadows = LightShadows.None;
        }

        private void BuildRenderTexture()
        {
            var width = SlotPixelSize * slotCount;
            var height = SlotPixelSize;
            RenderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "InventoryPreviewRT",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            RenderTexture.Create();
            previewCamera.targetTexture = RenderTexture;
        }

        // 0..1 normalized rect for a slot's portion of the shared RenderTexture.
        public Rect GetSlotUvRect(int slot)
        {
            if (slotCount <= 0) return new Rect(0f, 0f, 1f, 1f);
            var width = 1f / slotCount;
            return new Rect(slot * width, 0f, width, 1f);
        }

        // Set the item displayed at a slot. Pass null to clear. No-op when the slot already
        // holds the same item.
        public void SetSlotItem(int slot, NetworkLootItem item)
        {
            if (slot < 0 || slot >= slotCount) return;
            if (currentItems[slot] == item) return;
            currentItems[slot] = item;

            if (previewInstances[slot] != null)
            {
                Destroy(previewInstances[slot]);
                previewInstances[slot] = null;
            }

            if (item == null) return;

            var sourceFilter = item.GetComponentInChildren<MeshFilter>();
            var sourceRenderer = item.GetComponentInChildren<MeshRenderer>();
            if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null) return;

            var preview = new GameObject($"Preview_{slot}");
            preview.transform.SetParent(stages[slot], false);
            preview.transform.localPosition = Vector3.zero;
            preview.transform.localRotation = Quaternion.Euler(15f, 25f, 0f);
            preview.transform.localScale = ComputeFitScale(sourceFilter);

            var meshFilter = preview.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = sourceFilter.sharedMesh;
            var meshRenderer = preview.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            previewInstances[slot] = preview;
        }

        private Vector3 ComputeFitScale(MeshFilter source)
        {
            // Preview meshes vary wildly in size (a Cube primitive is 1u, a stretched cylinder
            // wing is ~2u). Uniformly scale so the largest axis fits within the stage box.
            var sourceScale = source.transform.lossyScale;
            var bounds = source.sharedMesh.bounds.size;
            var worldSize = Vector3.Scale(bounds, sourceScale);
            var maxDim = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
            if (maxDim <= 0.0001f) return Vector3.one;
            var target = StageHalfHeight * 1.4f;
            return Vector3.one * (target / maxDim);
        }

        private void Update()
        {
            if (previewInstances == null) return;
            var step = 38f * Time.unscaledDeltaTime;
            for (var i = 0; i < previewInstances.Length; i++)
            {
                var preview = previewInstances[i];
                if (preview != null)
                    preview.transform.Rotate(Vector3.up, step, Space.Self);
            }
        }

        private void OnDestroy()
        {
            if (RenderTexture != null)
            {
                RenderTexture.Release();
                Destroy(RenderTexture);
                RenderTexture = null;
            }
        }
    }
}
