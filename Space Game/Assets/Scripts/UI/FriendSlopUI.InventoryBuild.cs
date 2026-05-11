using FriendSlop.Player;
using UnityEngine;
using UnityEngine.UI;

namespace FriendSlop.UI
{
    public partial class FriendSlopUI
    {
        private void BuildInventoryPanel(GameObject canvasObject)
        {
            const int slotCount = NetworkFirstPersonController.InventorySize;
            const float slotWidth = 120f;
            const float slotHeight = 120f;
            const float slotGap = 10f;
            var totalWidth = slotCount * slotWidth + (slotCount - 1) * slotGap;

            // Anchor at bottom-center, sitting above the prompt line.
            var panel = CreatePanel("InventoryPanel", canvasObject.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 140f), new Vector2(totalWidth, slotHeight),
                new Color(0f, 0f, 0f, 0f));
            inventoryPanelRect = panel.GetComponent<RectTransform>();

            inventorySlotBackgrounds = new Image[slotCount];
            inventorySlotBorders = new Image[slotCount];
            inventorySlotItemTexts = new Text[slotCount];
            inventorySlotValueTexts = new Text[slotCount];
            inventorySlotPreviews = new RawImage[slotCount];

            for (var i = 0; i < slotCount; i++)
            {
                var slotX = -totalWidth * 0.5f + slotWidth * 0.5f + i * (slotWidth + slotGap);

                // Border (sits behind the slot fill so it shows as a thin frame).
                var border = CreatePanel($"Slot{i + 1}Border", panel.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(slotX, 0f), new Vector2(slotWidth, slotHeight),
                    InventorySlotBorderIdleColor);
                inventorySlotBorders[i] = border.GetComponent<Image>();

                var slotBg = CreatePanel($"Slot{i + 1}", border.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(slotWidth - 4f, slotHeight - 4f),
                    InventorySlotEmptyColor);
                inventorySlotBackgrounds[i] = slotBg.GetComponent<Image>();

                // 3D preview RawImage. Added first so it sits behind the text overlays.
                var previewObject = new GameObject($"Slot{i + 1}Preview");
                previewObject.transform.SetParent(slotBg.transform, false);
                var previewRect = previewObject.AddComponent<RectTransform>();
                previewRect.anchorMin = new Vector2(0.5f, 0.5f);
                previewRect.anchorMax = new Vector2(0.5f, 0.5f);
                previewRect.pivot = new Vector2(0.5f, 0.5f);
                previewRect.anchoredPosition = new Vector2(0f, 14f);
                previewRect.sizeDelta = new Vector2(72f, 72f);
                var preview = previewObject.AddComponent<RawImage>();
                preview.raycastTarget = false;
                if (inventoryPreviewRig != null)
                {
                    preview.texture = inventoryPreviewRig.RenderTexture;
                    preview.uvRect = inventoryPreviewRig.GetSlotUvRect(i);
                }
                inventorySlotPreviews[i] = preview;

                var numberLabel = CreateText($"Slot{i + 1}Number", slotBg.transform,
                    (i + 1).ToString(), 14, TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(8f, -4f), new Vector2(20f, 18f));
                numberLabel.color = new Color(1f, 1f, 1f, 0.85f);
                var numberOutline = numberLabel.gameObject.AddComponent<Outline>();
                numberOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                numberOutline.effectDistance = new Vector2(1f, -1f);

                var itemText = CreateText($"Slot{i + 1}Item", slotBg.transform,
                    string.Empty, 13, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 22f), new Vector2(slotWidth - 14f, 14f));
                itemText.horizontalOverflow = HorizontalWrapMode.Overflow;
                var itemOutline = itemText.gameObject.AddComponent<Outline>();
                itemOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                itemOutline.effectDistance = new Vector2(1f, -1f);
                inventorySlotItemTexts[i] = itemText;

                var valueText = CreateText($"Slot{i + 1}Value", slotBg.transform,
                    string.Empty, 12, TextAnchor.LowerCenter,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 4f), new Vector2(slotWidth - 14f, 14f));
                valueText.color = new Color(0.92f, 1f, 0.52f, 1f);
                var valueOutline = valueText.gameObject.AddComponent<Outline>();
                valueOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                valueOutline.effectDistance = new Vector2(1f, -1f);
                inventorySlotValueTexts[i] = valueText;
            }

            // Hidden until the round is active and the local player exists.
            inventoryPanelRect.gameObject.SetActive(false);
        }
    }
}
