// InventoryUI.cs
// Builds and manages the inventory grid UI.
// Attach to the "Inventory" child of your Canvas.
// Drag the PlayerInventory component into the Inspector field.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerInteraction playerInteraction;

    [Header("Grid Settings")]
    [SerializeField] private int columns = 5;
    [SerializeField] private float slotSize = 100f;
    [SerializeField] private float slotGap = 6f;
    [SerializeField] private float panelPadding = 16f;

    [Header("Colors")]
    [SerializeField] private Color panelBgColor = new Color(0.05f, 0.05f, 0.07f, 0.95f);
    [SerializeField] private Color slotEmptyColor = new Color(0.08f, 0.08f, 0.10f, 1f);
    [SerializeField] private Color slotHoverColor = new Color(0.20f, 0.20f, 0.25f, 1f);
    [SerializeField] private Color slotBorderColor = new Color(0.25f, 0.25f, 0.30f, 1f);

    [Header("Drop Settings")]
    [Tooltip("How far in front of the camera a dropped item spawns.")]
    [SerializeField] private float dropDistance = 1.5f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private GameObject _root;
    private GameObject _panel;
    private List<SlotWidget> _slots = new();
    private bool _built = false;

    // Drag state
    private int _dragSourceIndex = -1;
    private GameObject _dragGhost;
    private Canvas _canvas;

    // ── Events (mirrors ModificationStationUI pattern) ────────────────────────
    public event System.Action OnUIOpened;
    public event System.Action OnUIClosed;
    public bool IsOpen => _root != null && _root.activeSelf;

    // ── Slot widget ───────────────────────────────────────────────────────────

    private class SlotWidget
    {
        public int index;
        public GameObject root;
        public Image background;
        public Image itemColor;
        public TextMeshProUGUI tooltip;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        BuildUI();
    }

    private void OnEnable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged += RefreshSlots;
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= RefreshSlots;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        if (_built) return;
        _built = true;

        // Root — full screen overlay to catch drag events
        _root = new GameObject("InventoryRoot");
        _root.transform.SetParent(transform, false);
        var rootRT = _root.AddComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Invisible blocker image so UI raycasts register on drag
        var blocker = _root.AddComponent<Image>();
        blocker.color = new Color(0, 0, 0, 0.01f);

        // Panel — centered
        _panel = new GameObject("InventoryPanel");
        _panel.transform.SetParent(_root.transform, false);

        var panelRT = _panel.AddComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);

        var panelImg = _panel.AddComponent<Image>();
        panelImg.color = panelBgColor;

        // Size panel to fit grid
        int rows = Mathf.CeilToInt((float)inventory.SlotCount / columns);
        float gridW = columns * slotSize + (columns - 1) * slotGap;
        float gridH = rows * slotSize + (rows - 1) * slotGap;
        float panelW = gridW + panelPadding * 2f;
        float panelH = gridH + panelPadding * 2f + 40f; // 40 for title bar
        panelRT.sizeDelta = new Vector2(panelW, panelH);

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(_panel.transform, false);
        var titleRT = titleGO.AddComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(0, -40f);
        titleRT.offsetMax = new Vector2(0, 0f);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "INVENTORY";
        titleTMP.fontSize = 18;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;

        // Separator
        var sep = new GameObject("Sep");
        sep.transform.SetParent(_panel.transform, false);
        var sepRT = sep.AddComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0, 1);
        sepRT.anchorMax = new Vector2(1, 1);
        sepRT.pivot = new Vector2(0.5f, 1f);
        sepRT.offsetMin = new Vector2(panelPadding, -42f);
        sepRT.offsetMax = new Vector2(-panelPadding, -40f);
        var sepImg = sep.AddComponent<Image>();
        sepImg.color = new Color(0.25f, 0.55f, 1f, 0.4f);

        // Grid origin (top-left inside padding)
        var gridOriginX = -gridW * 0.5f;
        var gridOriginY = gridH * 0.5f - 40f; // offset below title

        // Build slots
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            int col = i % columns;
            int row = i / columns;

            float x = gridOriginX + col * (slotSize + slotGap) + slotSize * 0.5f;
            float y = gridOriginY - row * (slotSize + slotGap) - slotSize * 0.5f;

            var widget = BuildSlot(i, x, y);
            _slots.Add(widget);
        }

        _root.SetActive(false);
    }

    private SlotWidget BuildSlot(int index, float x, float y)
    {
        var widget = new SlotWidget { index = index };

        // Root
        var go = new GameObject($"Slot_{index}");
        go.transform.SetParent(_panel.transform, false);
        widget.root = go;

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        // Border (slightly larger background)
        var borderGO = new GameObject("Border");
        borderGO.transform.SetParent(go.transform, false);
        var borderRT = borderGO.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero;
        borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2);
        borderRT.offsetMax = new Vector2(2, 2);
        var borderImg = borderGO.AddComponent<Image>();
        borderImg.color = slotBorderColor;

        // Background
        widget.background = go.AddComponent<Image>();
        widget.background.color = slotEmptyColor;

        // Item color overlay (hidden when empty)
        var colorGO = new GameObject("ItemColor");
        colorGO.transform.SetParent(go.transform, false);
        var colorRT = colorGO.AddComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f);
        colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero;
        colorRT.offsetMax = Vector2.zero;
        widget.itemColor = colorGO.AddComponent<Image>();
        widget.itemColor.color = Color.clear;

        // Tooltip label (hidden by default, shown on hover)
        var ttGO = new GameObject("Tooltip");
        ttGO.transform.SetParent(go.transform, false);
        var ttRT = ttGO.AddComponent<RectTransform>();
        ttRT.anchorMin = new Vector2(0, 0);
        ttRT.anchorMax = new Vector2(1, 0);
        ttRT.pivot = new Vector2(0.5f, 1f);
        ttRT.offsetMin = new Vector2(0, 2f);
        ttRT.offsetMax = new Vector2(0, 26f);
        // Tooltip background
        var ttBg = ttGO.AddComponent<Image>();
        ttBg.color = new Color(0.02f, 0.02f, 0.04f, 0.9f);
        // Tooltip text
        var ttChild = new GameObject("Text");
        ttChild.transform.SetParent(ttGO.transform, false);
        var ttChildRT = ttChild.AddComponent<RectTransform>();
        ttChildRT.anchorMin = Vector2.zero;
        ttChildRT.anchorMax = Vector2.one;
        ttChildRT.offsetMin = new Vector2(4, 2);
        ttChildRT.offsetMax = new Vector2(-4, -2);
        widget.tooltip = ttChild.AddComponent<TextMeshProUGUI>();
        widget.tooltip.fontSize = 10;
        widget.tooltip.alignment = TextAlignmentOptions.Center;
        widget.tooltip.color = Color.white;
        ttGO.SetActive(false);
        widget.tooltip.transform.parent.gameObject.SetActive(false);

        // ── Event triggers ────────────────────────────────────────────────────
        var trigger = go.AddComponent<EventTrigger>();
        int captured = index;

        // Hover enter — show tooltip if occupied
        AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
        {
            if (inventory.GetSlot(captured) != null)
            {
                widget.tooltip.text = inventory.GetSlot(captured).displayName;
                widget.tooltip.transform.parent.gameObject.SetActive(true);
            }
            widget.background.color = slotHoverColor;
        });

        // Hover exit — hide tooltip
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            widget.tooltip.transform.parent.gameObject.SetActive(false);
            widget.background.color = inventory.GetSlot(captured) != null
                ? DarkenSlotColor(inventory.GetSlot(captured).slotColor)
                : slotEmptyColor;
        });

        // Drag begin — pick up item
        AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
        {
            if (inventory.GetSlot(captured) == null) return;
            _dragSourceIndex = captured;
            CreateDragGhost(inventory.GetSlot(captured), ((PointerEventData)data).position);
        });

        // Drag — move ghost
        AddTrigger(trigger, EventTriggerType.Drag, data =>
        {
            if (_dragGhost == null) return;
            var pointerData = (PointerEventData)data;
            var ghostRT = _dragGhost.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.GetComponent<RectTransform>(),
                pointerData.position,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main,
                out var localPoint
            );
            ghostRT.anchoredPosition = localPoint;
        });

        // Drop — release drag
        AddTrigger(trigger, EventTriggerType.EndDrag, data =>
        {
            if (_dragGhost == null) return;
            Destroy(_dragGhost);
            _dragGhost = null;

            var pointerData = (PointerEventData)data;
            bool droppedOnPanel = RectTransformUtility.RectangleContainsScreenPoint(
                _panel.GetComponent<RectTransform>(),
                pointerData.position,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main
            );

            if (!droppedOnPanel && _dragSourceIndex >= 0)
            {
                // Dropped outside panel — spawn in world
                DropItemToWorld(_dragSourceIndex);
            }

            _dragSourceIndex = -1;
        });

        return widget;
    }

    // ── Drag ghost ────────────────────────────────────────────────────────────

    private void CreateDragGhost(ItemDefinition item, Vector2 screenPos)
    {
        _dragGhost = new GameObject("DragGhost");
        _dragGhost.transform.SetParent(_canvas.transform, false);

        var rt = _dragGhost.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        var img = _dragGhost.AddComponent<Image>();
        img.color = new Color(item.slotColor.r, item.slotColor.g, item.slotColor.b, 0.7f);
        img.raycastTarget = false; // don't block events

        // Position ghost at cursor
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            screenPos,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main,
            out var localPoint
        );
        rt.anchoredPosition = localPoint;
    }

    // ── Drop to world ─────────────────────────────────────────────────────────

    private void DropItemToWorld(int slotIndex)
    {
        var item = inventory.RemoveAt(slotIndex);
        if (item == null) return;

        var cam = Camera.main.transform;
        var spawnPos = cam.position + cam.forward * dropDistance;

        GameObject worldObj;

        if (item.worldPrefab != null)
        {
            // Always use the item's own prefab — this preserves the correct mesh
            worldObj = Instantiate(item.worldPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // Fallback for items without a prefab assigned — warn the designer
            Debug.LogWarning($"[Inventory] '{item.displayName}' has no worldPrefab assigned " +
                             $"on its ItemDefinition. Spawning placeholder capsule. " +
                             $"Assign a worldPrefab to fix this.");
            worldObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            worldObj.transform.position = spawnPos;
            worldObj.transform.localScale = Vector3.one * 0.3f;
            var mr = worldObj.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mr.material.color = item.slotColor;
            }
        }

        // Ensure the spawned object has a PickupItem with the correct data.
        // If the prefab already has one, we just overwrite the item reference
        // so it stays in sync with whatever was in the inventory slot.
        var pickup = worldObj.GetComponent<PickupItem>();
        if (pickup == null) pickup = worldObj.AddComponent<PickupItem>();
        pickup.item = item;

        Debug.Log($"[Inventory] Dropped '{item.displayName}' at {spawnPos}.");
    }

    // ── Refresh slots ─────────────────────────────────────────────────────────

    private void RefreshSlots()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            var widget = _slots[i];
            var item = inventory.GetSlot(i);

            if (item != null)
            {
                widget.background.color = DarkenSlotColor(item.slotColor);
                widget.itemColor.color = item.slotColor;
            }
            else
            {
                widget.background.color = slotEmptyColor;
                widget.itemColor.color = Color.clear;
            }
        }
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        _root.SetActive(true);
        RefreshSlots();
        OnUIOpened?.Invoke();
    }

    public void Close()
    {
        if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
        _dragSourceIndex = -1;
        _root.SetActive(false);
        OnUIClosed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddTrigger(EventTrigger trigger,
        EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    private static Color DarkenSlotColor(Color c) =>
        new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f);
}
