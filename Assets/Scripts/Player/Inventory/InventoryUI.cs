// InventoryUI.cs
// Split layout: left = equipment panel (humanoid silhouette + hand slots),
//               right = inventory grid.
// Right-click on inventory items opens a context menu for equipping.
// Drag a slot outside the panel to drop the item into the world.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem;

public class InventoryUI : MonoBehaviour
{
    private enum EquipmentDragKind { None, Armour, LeftHand, RightHand, Bag }

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private PlayerInteraction playerInteraction;
    [SerializeField] private HealthHUD healthHUD;
    [SerializeField] private AugmentCatalogue augmentCatalogue;

    [Header("Grid Settings")]
    [SerializeField] private int columns = 5;
    [SerializeField] private float slotSize = 80f;
    [SerializeField] private float slotGap = 6f;
    [SerializeField] private float panelPadding = 14f;

    [Header("Silhouette Slot Size")]
    [SerializeField] private float armourSlotSize = 54f;
    [SerializeField] private float handSlotSize = 64f;

    [Header("Colors")]
    [SerializeField] private Color panelBgColor = new Color(0.05f, 0.05f, 0.07f, 0.95f);
    [SerializeField] private Color slotEmptyColor = new Color(0.08f, 0.08f, 0.10f, 1f);
    [SerializeField] private Color slotHoverColor = new Color(0.20f, 0.20f, 0.25f, 1f);
    [SerializeField] private Color slotBorderColor = new Color(0.25f, 0.25f, 0.30f, 1f);
    [SerializeField] private Color contextBgColor = new Color(0.06f, 0.06f, 0.09f, 0.98f);

    [Header("Drop Settings")]
    [SerializeField] private float dropDistance = 1.5f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private GameObject _root;
    private GameObject _outerPanel;   // full-screen dim
    private GameObject _leftPanel;    // equipment silhouette
    private GameObject _rightPanel;   // inventory grid

    private List<SlotWidget> _gridSlots = new();
    private ArmourSlotWidget[] _armourSlots = new ArmourSlotWidget[11];
    private HandSlotWidget _leftHandWidget;
    private HandSlotWidget _rightHandWidget;
    private BagSlotWidget _bagWidget;
    private TextMeshProUGUI _capacityLabel;
    private int _builtSlotCount;
    private RectTransform _gridContent;
    private RectTransform _healthAnchor;
    private Transform _equipmentSlotParent;

    // Shared mouse-following item details tooltip.
    private GameObject _itemTooltip;
    private TextMeshProUGUI _tooltipTitle;
    private TextMeshProUGUI _tooltipDescription;
    private ItemInstance _tooltipItem;

    // The normal health HUD is moved into the inventory while it is open.
    private Transform _healthOriginalParent;
    private int _healthOriginalSibling;
    private Vector2 _healthOriginalAnchorMin;
    private Vector2 _healthOriginalAnchorMax;
    private Vector2 _healthOriginalPivot;
    private Vector2 _healthOriginalPosition;
    private Vector2 _healthOriginalSize;
    private Vector3 _healthOriginalScale;
    private bool _healthHudWasActive;
    private AmmoHUD _ammoHUD;
    private bool _ammoHudWasActive;

    // Context menu
    private GameObject _contextMenu;
    private int _contextSlotIndex = -1;

    // Drag state
    private int _dragSourceIndex = -1;
    private EquipmentDragKind _equipmentDragKind;
    private BodyPart _dragArmourPart;
    private GameObject _dragGhost;
    private Canvas _canvas;

    private bool _built = false;

    public event System.Action OnUIOpened;
    public event System.Action OnUIClosed;
    public bool IsOpen => _root != null && _root.activeSelf;

    // ── Widget classes ────────────────────────────────────────────────────────

    private class SlotWidget
    {
        public int index;
        public GameObject root;
        public Image background;
        public Image itemColor;
        public GameObject stackBadge;
        public TextMeshProUGUI stackCount;
    }

    private class ArmourSlotWidget
    {
        public BodyPart bodyPart;
        public GameObject root;
        public Image background;
        public Image itemColor;
        public TextMeshProUGUI label;
    }

    private class HandSlotWidget
    {
        public HandSlot hand;
        public GameObject root;
        public Image background;
        public Image itemColor;
        public TextMeshProUGUI label;
    }

    private class BagSlotWidget
    {
        public GameObject root;
        public Image background;
        public Image itemColor;
        public TextMeshProUGUI label;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (transform is RectTransform host) Stretch(host);
        if (healthHUD == null) healthHUD = FindAnyObjectByType<HealthHUD>();
        if (augmentCatalogue == null)
            augmentCatalogue = FindAnyObjectByType<HealthManager>()?.augmentCatalogue;
        _ammoHUD = FindAnyObjectByType<AmmoHUD>();
        BuildUI();
    }

    private void Update()
    {
        if (!IsOpen || _tooltipItem == null || _itemTooltip == null) return;
        Vector2 pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        PositionTooltip(pointer);
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.OnInventoryChanged += RefreshGrid;
        if (equipment != null) equipment.OnEquipmentChanged += RefreshEquipment;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.OnInventoryChanged -= RefreshGrid;
        if (equipment != null) equipment.OnEquipmentChanged -= RefreshEquipment;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        if (_built) return;
        _built = true;

        // Full-screen root
        _root = MakeRect("InventoryRoot", transform);
        Stretch(_root.GetComponent<RectTransform>());
        var rootImg = _root.AddComponent<Image>();
        rootImg.color = new Color(0, 0, 0, 0.5f);
        _root.AddComponent<EventTrigger>(); // absorb clicks

        // Outer panel — use almost the entire screen so growing inventories do
        // not force the equipment area off-screen.
        _outerPanel = MakeRect("OuterPanel", _root.transform);
        var outerRT = _outerPanel.GetComponent<RectTransform>();
        outerRT.anchorMin = new Vector2(.025f, .035f);
        outerRT.anchorMax = new Vector2(.975f, .965f);
        outerRT.offsetMin = outerRT.offsetMax = Vector2.zero;
        _outerPanel.AddComponent<Image>().color = new Color(.025f, .028f, .04f, .99f);

        // ── Left panel (equipment) ────────────────────────────────────────────
        _leftPanel = MakeRect("EquipmentPanel", _outerPanel.transform);
        var leftRT = _leftPanel.GetComponent<RectTransform>();
        leftRT.anchorMin = new Vector2(0f, 0f);
        leftRT.anchorMax = new Vector2(.43f, 1f);
        leftRT.offsetMin = new Vector2(0f, 0f);
        leftRT.offsetMax = new Vector2(-6f, 0f);
        var leftImg = _leftPanel.AddComponent<Image>();
        leftImg.color = panelBgColor;

        BuildEquipmentPanel();

        // ── Right panel (inventory grid) ──────────────────────────────────────
        _rightPanel = MakeRect("InventoryPanel", _outerPanel.transform);
        var rightRT = _rightPanel.GetComponent<RectTransform>();
        rightRT.anchorMin = new Vector2(.43f, 0f);
        rightRT.anchorMax = Vector2.one;
        rightRT.offsetMin = new Vector2(6f, 0f);
        rightRT.offsetMax = Vector2.zero;
        var rightImg = _rightPanel.AddComponent<Image>();
        rightImg.color = panelBgColor;

        BuildInventoryGrid();

        // ── Context menu (built last so it renders on top) ────────────────────
        BuildContextMenu();
        BuildItemTooltip();

        _root.SetActive(false);
    }

    // ── Equipment panel (humanoid silhouette) ─────────────────────────────────

    private void BuildEquipmentPanel()
    {
        // Title
        var title = MakeRect("Title", _leftPanel.transform);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1); titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(0, -32f); titleRT.offsetMax = Vector2.zero;
        var titleTMP = title.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "EQUIPPED LOADOUT"; titleTMP.fontSize = 20;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;

        // Separator
        var sep = MakeRect("Sep", _leftPanel.transform);
        var sepRT = sep.GetComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0, 1); sepRT.anchorMax = new Vector2(1, 1);
        sepRT.pivot = new Vector2(0.5f, 1f);
        sepRT.offsetMin = new Vector2(8, -34f); sepRT.offsetMax = new Vector2(-8, -32f);
        sep.AddComponent<Image>().color = new Color(0.25f, 0.55f, 1f, 0.4f);

        var equipmentArea = MakeRect("EquipmentSlots", _leftPanel.transform);
        var equipmentRT = equipmentArea.GetComponent<RectTransform>();
        equipmentRT.anchorMin = new Vector2(.02f, .39f);
        equipmentRT.anchorMax = new Vector2(.98f, .95f);
        equipmentRT.offsetMin = equipmentRT.offsetMax = Vector2.zero;

        // A wider, symmetrical body layout with clear room for hands and bag.
        float cx = 0f;
        float top = 190f;

        Transform oldParent = _leftPanel.transform;
        _equipmentSlotParent = equipmentArea.transform;

        // [Head]
        BuildArmourSlot(BodyPart.Head, cx, top - 0f, "Head");

        // [Chest] [Abdomen]
        BuildArmourSlot(BodyPart.Chest, cx, top - 90f, "Chest");
        BuildArmourSlot(BodyPart.Abdomen, cx, top - 180f, "Abdomen");

        // [LUA] [LFA]   [RFA] [RUA]
        BuildArmourSlot(BodyPart.LeftUpperArm, cx - 115f, top - 90f, "L ARM");
        BuildArmourSlot(BodyPart.LeftForearm, cx - 175f, top - 180f, "L FOREARM");
        BuildArmourSlot(BodyPart.RightUpperArm, cx + 115f, top - 90f, "R ARM");
        BuildArmourSlot(BodyPart.RightForearm, cx + 175f, top - 180f, "R FOREARM");

        // [LThigh] [RThigh]
        BuildArmourSlot(BodyPart.LeftThigh, cx - 42f, top - 270f, "L THIGH");
        BuildArmourSlot(BodyPart.RightThigh, cx + 42f, top - 270f, "R THIGH");

        // [LShin] [RShin]
        BuildArmourSlot(BodyPart.LeftShin, cx - 42f, top - 360f, "L SHIN");
        BuildArmourSlot(BodyPart.RightShin, cx + 42f, top - 360f, "R SHIN");

        // Hand slots — below silhouette
        float handY = top - 290f;
        BuildHandSlot(HandSlot.Left, cx - 190f, handY, "LEFT HAND");
        BuildHandSlot(HandSlot.Right, cx + 190f, handY, "RIGHT HAND");

        // Bag slot — bottom-right corner of the panel, clear of the hand slots
        BuildBagSlot(cx + 285f, top - 360f, "BAG");

        _equipmentSlotParent = oldParent;

        GameObject healthSection = MakeRect("HealthSection", _leftPanel.transform);
        _healthAnchor = healthSection.GetComponent<RectTransform>();
        _healthAnchor.anchorMin = new Vector2(.02f, .02f);
        _healthAnchor.anchorMax = new Vector2(.98f, .36f);
        _healthAnchor.offsetMin = _healthAnchor.offsetMax = Vector2.zero;
        healthSection.AddComponent<Image>().color = new Color(.025f, .03f, .045f, .8f);
    }

    private void BuildArmourSlot(BodyPart part, float x, float y, string shortLabel)
    {
        var go = MakeRect($"Armour_{part}", _equipmentSlotParent ?? _leftPanel.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(armourSlotSize, armourSlotSize);

        // Border
        var border = MakeRect("Border", go.transform);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2); borderRT.offsetMax = new Vector2(2, 2);
        var borderImage = border.AddComponent<Image>();
        borderImage.color = slotBorderColor;
        borderImage.raycastTarget = false;

        var bg = go.AddComponent<Image>();
        bg.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        var colorImg = colorGO.AddComponent<Image>();
        colorImg.color = Color.clear;
        colorImg.raycastTarget = false;

        var labelGO = MakeRect("Label", go.transform);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = new Vector2(1, 0);
        labelRT.pivot = new Vector2(0.5f, 0f);
        labelRT.offsetMin = new Vector2(2f, 1f); labelRT.offsetMax = new Vector2(-2f, 15f);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = shortLabel; labelTMP.fontSize = 7;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = Color.white;
        labelTMP.outlineColor = Color.black;
        labelTMP.outlineWidth = .18f;
        labelTMP.textWrappingMode = TextWrappingModes.NoWrap;
        labelTMP.overflowMode = TextOverflowModes.Ellipsis;
        labelTMP.raycastTarget = false;

        // Drag-drop target
        var trigger = go.AddComponent<EventTrigger>();
        BodyPart capturedPart = part;
        Image capturedBg = bg;

        AddTrigger(trigger, EventTriggerType.PointerEnter, data =>
        {
            capturedBg.color = slotHoverColor;
            ShowItemTooltip(equipment?.GetArmourSlot(capturedPart), ((PointerEventData)data).position);
        });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            HideItemTooltip();
            capturedBg.color = equipment?.GetArmourSlot(capturedPart) != null
                ? DarkenColor(equipment.GetArmourSlot(capturedPart).definition.slotColor)
                : slotEmptyColor;
        });
        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_dragSourceIndex < 0) return;
            int sourceIndex = _dragSourceIndex;
            var item = inventory.GetSlot(sourceIndex);
            if (item == null || !item.definition.IsArmour) return;
            if (item.definition.targetBodyPart != capturedPart) return;
            FinishDragVisual();
            inventory.RemoveAt(sourceIndex);
            equipment.TryEquipArmour(item);
        });
        // Right-click to unequip
        AddTrigger(trigger, EventTriggerType.PointerClick, data =>
        {
            var pd = (PointerEventData)data;
            if (pd.button == PointerEventData.InputButton.Right)
            {
                if (equipment?.GetArmourSlot(capturedPart) != null)
                    equipment.UnequipArmour(capturedPart);
            }
        });
        AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
            BeginEquipmentDrag(EquipmentDragKind.Armour, capturedPart,
                equipment?.GetArmourSlot(capturedPart), ((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.Drag, data =>
            MoveGhost(((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.EndDrag, _ => FinishDragVisual());

        int idx = (int)part;
        _armourSlots[idx] = new ArmourSlotWidget
        {
            bodyPart = part,
            root = go,
            background = bg,
            itemColor = colorImg,
            label = labelTMP
        };
    }

    private void BuildHandSlot(HandSlot hand, float x, float y, string label)
    {
        var go = MakeRect($"Hand_{hand}", _equipmentSlotParent ?? _leftPanel.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(handSlotSize, handSlotSize);

        var border = MakeRect("Border", go.transform);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2); borderRT.offsetMax = new Vector2(2, 2);
        var borderImage = border.AddComponent<Image>();
        borderImage.color = new Color(0.4f, 0.3f, 0.1f, 1f);
        borderImage.raycastTarget = false;

        var bg = go.AddComponent<Image>();
        bg.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        var colorImg = colorGO.AddComponent<Image>();
        colorImg.color = Color.clear;
        colorImg.raycastTarget = false;

        var labelGO = MakeRect("Label", go.transform);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = new Vector2(1, 0);
        labelRT.pivot = new Vector2(0.5f, 0f);
        labelRT.offsetMin = new Vector2(2f, 1f); labelRT.offsetMax = new Vector2(-2f, 16f);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label; labelTMP.fontSize = 8;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = new Color(1f, .82f, .3f);
        labelTMP.outlineColor = Color.black;
        labelTMP.outlineWidth = .18f;
        labelTMP.textWrappingMode = TextWrappingModes.NoWrap;
        labelTMP.overflowMode = TextOverflowModes.Ellipsis;
        labelTMP.raycastTarget = false;

        var trigger = go.AddComponent<EventTrigger>();
        HandSlot capturedHand = hand;
        Image capturedBg = bg;

        AddTrigger(trigger, EventTriggerType.PointerEnter, data =>
        {
            capturedBg.color = slotHoverColor;
            ShowItemTooltip(equipment?.GetHandSlot(capturedHand), ((PointerEventData)data).position);
        });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            HideItemTooltip();
            capturedBg.color = equipment?.GetHandSlot(capturedHand) != null
                ? DarkenColor(equipment.GetHandSlot(capturedHand).definition.slotColor)
                : slotEmptyColor;
        });

        // Right-click to unequip
        AddTrigger(trigger, EventTriggerType.PointerClick, data =>
        {
            var pd = (PointerEventData)data;
            if (pd.button == PointerEventData.InputButton.Right)
                equipment?.UnequipHand(capturedHand);
        });
        AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
            BeginEquipmentDrag(capturedHand == HandSlot.Left
                    ? EquipmentDragKind.LeftHand : EquipmentDragKind.RightHand,
                default, equipment?.GetHandSlot(capturedHand), ((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.Drag, data =>
            MoveGhost(((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.EndDrag, _ => FinishDragVisual());

        // Drop target for drag-equip
        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_dragSourceIndex < 0) return;
            int sourceIndex = _dragSourceIndex;
            var item = inventory.GetSlot(sourceIndex);
            if (item == null || item.definition.IsAmmo
                || (!item.definition.IsWeapon && !item.definition.IsConsumable)) return;
            FinishDragVisual();
            inventory.RemoveAt(sourceIndex);
            equipment.TryEquipHand(item, capturedHand);
        });

        var widget = new HandSlotWidget
        {
            hand = hand,
            root = go,
            background = bg,
            itemColor = colorImg,
            label = labelTMP
        };

        if (hand == HandSlot.Left) _leftHandWidget = widget;
        else _rightHandWidget = widget;
    }

    private void BuildBagSlot(float x, float y, string label)
    {
        var go = MakeRect("Bag", _equipmentSlotParent ?? _leftPanel.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(handSlotSize, handSlotSize);

        var border = MakeRect("Border", go.transform);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2); borderRT.offsetMax = new Vector2(2, 2);
        var borderImage = border.AddComponent<Image>();
        borderImage.color = new Color(0.3f, 0.2f, 0.4f, 1f);
        borderImage.raycastTarget = false;

        var bg = go.AddComponent<Image>();
        bg.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        var colorImg = colorGO.AddComponent<Image>();
        colorImg.color = Color.clear;
        colorImg.raycastTarget = false;

        var labelGO = MakeRect("Label", go.transform);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = new Vector2(1, 0);
        labelRT.pivot = new Vector2(0.5f, 0f);
        labelRT.offsetMin = new Vector2(2f, 1f); labelRT.offsetMax = new Vector2(-2f, 16f);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label; labelTMP.fontSize = 8;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = new Color(.82f, .62f, 1f);
        labelTMP.outlineColor = Color.black;
        labelTMP.outlineWidth = .18f;
        labelTMP.textWrappingMode = TextWrappingModes.NoWrap;
        labelTMP.overflowMode = TextOverflowModes.Ellipsis;
        labelTMP.raycastTarget = false;

        var trigger = go.AddComponent<EventTrigger>();
        Image capturedBg = bg;

        AddTrigger(trigger, EventTriggerType.PointerEnter, data =>
        {
            capturedBg.color = slotHoverColor;
            ShowItemTooltip(equipment?.GetBagSlot(), ((PointerEventData)data).position);
        });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            HideItemTooltip();
            capturedBg.color = equipment?.GetBagSlot() != null
                ? DarkenColor(equipment.GetBagSlot().definition.slotColor)
                : slotEmptyColor;
        });

        AddTrigger(trigger, EventTriggerType.PointerClick, data =>
        {
            var pd = (PointerEventData)data;
            if (pd.button == PointerEventData.InputButton.Right)
                equipment?.UnequipBag();
        });
        AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
            BeginEquipmentDrag(EquipmentDragKind.Bag, default,
                equipment?.GetBagSlot(), ((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.Drag, data =>
            MoveGhost(((PointerEventData)data).position));
        AddTrigger(trigger, EventTriggerType.EndDrag, _ => FinishDragVisual());

        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_dragSourceIndex < 0) return;
            int sourceIndex = _dragSourceIndex;
            var item = inventory.GetSlot(sourceIndex);
            if (item == null || !item.definition.IsBag) return;
            FinishDragVisual();
            inventory.RemoveAt(sourceIndex);
            equipment.TryEquipBag(item);
        });

        _bagWidget = new BagSlotWidget { root = go, background = bg, itemColor = colorImg, label = labelTMP };
    }

    // ── Inventory grid ────────────────────────────────────────────────────────

    private void BuildInventoryGrid()
    {
        EventTrigger panelDropTarget = _rightPanel.GetComponent<EventTrigger>()
            ?? _rightPanel.AddComponent<EventTrigger>();
        AddTrigger(panelDropTarget, EventTriggerType.Drop, _ =>
        {
            if (_equipmentDragKind == EquipmentDragKind.None) return;
            UnequipDraggedEquipment();
            FinishDragVisual();
        });

        // Title
        var title = MakeRect("Title", _rightPanel.transform);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1); titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(0, -32f); titleRT.offsetMax = Vector2.zero;
        var titleTMP = title.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "INVENTORY"; titleTMP.fontSize = 20;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;

        // Capacity label (e.g. "4 / 10")
        var capGO = MakeRect("Capacity", _rightPanel.transform);
        var capRT = capGO.GetComponent<RectTransform>();
        capRT.anchorMin = new Vector2(.72f, 1); capRT.anchorMax = new Vector2(1, 1);
        capRT.pivot = new Vector2(1f, 1f);
        capRT.offsetMin = new Vector2(0, -40f); capRT.offsetMax = new Vector2(-18f, 0);
        _capacityLabel = capGO.AddComponent<TextMeshProUGUI>();
        _capacityLabel.fontSize = 14;
        _capacityLabel.fontStyle = FontStyles.Bold;
        _capacityLabel.alignment = TextAlignmentOptions.MidlineRight;
        _capacityLabel.color = new Color(0.7f, 0.7f, 0.7f);

        CanvasScaler scaler = _canvas != null ? _canvas.GetComponent<CanvasScaler>() : null;
        float referenceWidth = scaler != null ? scaler.referenceResolution.x : 1920f;
        float availableWidth = referenceWidth * .95f * .57f - panelPadding * 2f;
        columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + slotGap) / (slotSize + slotGap)));

        GameObject viewport = MakeRect("GridViewport", _rightPanel.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, panelPadding, panelPadding, panelPadding, 54f);
        viewport.AddComponent<Image>().color = new Color(.018f, .02f, .03f, .65f);
        viewport.AddComponent<RectMask2D>();

        GameObject content = MakeRect("GridContent", viewport.transform);
        _gridContent = content.GetComponent<RectTransform>();
        _gridContent.anchorMin = new Vector2(0f, 1f);
        _gridContent.anchorMax = new Vector2(1f, 1f);
        _gridContent.pivot = new Vector2(.5f, 1f);
        _gridContent.anchoredPosition = Vector2.zero;
        _gridContent.sizeDelta = Vector2.zero;
        GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(slotSize, slotSize);
        grid.spacing = new Vector2(slotGap, slotGap);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = _gridContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 28f;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        RebuildGridSlots();
    }

    private void RebuildGridSlots()
    {
        foreach (var w in _gridSlots) Destroy(w.root);
        _gridSlots.Clear();

        for (int i = 0; i < inventory.SlotCount; i++)
            _gridSlots.Add(BuildGridSlot(i));
        _builtSlotCount = inventory.SlotCount;
    }

    private SlotWidget BuildGridSlot(int index)
    {
        var widget = new SlotWidget { index = index };

        var go = MakeRect($"Slot_{index}", _gridContent);
        widget.root = go;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        // Border
        var border = MakeRect("Border", go.transform);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2); borderRT.offsetMax = new Vector2(2, 2);
        var borderImage = border.AddComponent<Image>();
        borderImage.color = slotBorderColor;
        borderImage.raycastTarget = false;

        widget.background = go.AddComponent<Image>();
        widget.background.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        widget.itemColor = colorGO.AddComponent<Image>();
        widget.itemColor.color = Color.clear;
        widget.itemColor.raycastTarget = false;

        BuildStackBadge(go.transform, widget);

        var trigger = go.AddComponent<EventTrigger>();
        int captured = index;

        AddTrigger(trigger, EventTriggerType.PointerEnter, data =>
        {
            var item = inventory.GetSlot(captured);
            ShowItemTooltip(item, ((PointerEventData)data).position);
            widget.background.color = slotHoverColor;
        });

        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            HideItemTooltip();
            widget.background.color = inventory.GetSlot(captured) != null
                ? DarkenColor(inventory.GetSlot(captured).definition.slotColor)
                : slotEmptyColor;
        });

        // Right-click → context menu
        AddTrigger(trigger, EventTriggerType.PointerClick, data =>
        {
            var pd = (PointerEventData)data;
            if (pd.button == PointerEventData.InputButton.Right)
                ShowContextMenu(captured, pd.position);
        });

        // Drag begin
        AddTrigger(trigger, EventTriggerType.BeginDrag, data =>
        {
            if (inventory.GetSlot(captured) == null) return;
            _equipmentDragKind = EquipmentDragKind.None;
            _dragSourceIndex = captured;
            HideContextMenu();
            CreateDragGhost(inventory.GetSlot(captured), ((PointerEventData)data).position);
        });

        // Drop onto another inventory slot — moves/swaps regardless of how many slots are open
        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_equipmentDragKind != EquipmentDragKind.None)
            {
                UnequipDraggedEquipment();
                FinishDragVisual();
                return;
            }
            if (_dragSourceIndex < 0) return;
            if (captured != _dragSourceIndex)
                inventory.MoveItem(_dragSourceIndex, captured);
        });

        // Drag move
        AddTrigger(trigger, EventTriggerType.Drag, data =>
        {
            if (_dragGhost == null) return;
            MoveGhost(((PointerEventData)data).position);
        });

        // Drag end — drop outside panel = drop to world
        AddTrigger(trigger, EventTriggerType.EndDrag, data =>
        {
            if (_dragGhost == null) return;

            var pointerData = (PointerEventData)data;
            bool onPanel = RectTransformUtility.RectangleContainsScreenPoint(
                _outerPanel.GetComponent<RectTransform>(),
                pointerData.position,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main);

            if (!onPanel && _dragSourceIndex >= 0)
                DropItemToWorld(_dragSourceIndex);

            FinishDragVisual();
        });

        return widget;
    }

    private void BuildStackBadge(Transform parent, SlotWidget widget)
    {
        widget.stackBadge = MakeRect("StackBadge", parent);
        RectTransform badgeRect = widget.stackBadge.GetComponent<RectTransform>();
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(.5f, .5f);
        badgeRect.anchoredPosition = new Vector2(-8f, -8f);
        badgeRect.sizeDelta = new Vector2(27f, 27f);
        Image circle = widget.stackBadge.AddComponent<Image>();
        circle.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        circle.color = Color.white;
        circle.raycastTarget = false;

        GameObject count = MakeRect("Count", widget.stackBadge.transform);
        Stretch(count.GetComponent<RectTransform>(), 2f, 2f, 2f, 2f);
        widget.stackCount = count.AddComponent<TextMeshProUGUI>();
        widget.stackCount.fontSize = 13f;
        widget.stackCount.fontStyle = FontStyles.Bold;
        widget.stackCount.color = Color.black;
        widget.stackCount.alignment = TextAlignmentOptions.Center;
        widget.stackCount.raycastTarget = false;
        widget.stackBadge.SetActive(false);
    }

    private void BuildItemTooltip()
    {
        _itemTooltip = MakeRect("ItemDetailsTooltip", _root.transform);
        RectTransform rect = _itemTooltip.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(340f, 0f);
        Image background = _itemTooltip.AddComponent<Image>();
        background.color = new Color(.025f, .03f, .045f, .985f);
        background.raycastTarget = false;
        Outline outline = _itemTooltip.AddComponent<Outline>();
        outline.effectColor = new Color(.32f, .55f, .9f, .8f);
        outline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup layout = _itemTooltip.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 11, 13);
        layout.spacing = 7f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = _itemTooltip.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject title = MakeRect("Title", _itemTooltip.transform);
        _tooltipTitle = title.AddComponent<TextMeshProUGUI>();
        _tooltipTitle.fontSize = 18f;
        _tooltipTitle.fontStyle = FontStyles.Bold;
        _tooltipTitle.color = Color.white;
        _tooltipTitle.textWrappingMode = TextWrappingModes.Normal;
        _tooltipTitle.raycastTarget = false;

        GameObject separator = MakeRect("Separator", _itemTooltip.transform);
        separator.AddComponent<LayoutElement>().preferredHeight = 2f;
        Image separatorImage = separator.AddComponent<Image>();
        separatorImage.color = new Color(.32f, .55f, .9f, .65f);
        separatorImage.raycastTarget = false;

        GameObject description = MakeRect("Description", _itemTooltip.transform);
        _tooltipDescription = description.AddComponent<TextMeshProUGUI>();
        _tooltipDescription.fontSize = 13f;
        _tooltipDescription.color = new Color(.84f, .87f, .93f, 1f);
        _tooltipDescription.textWrappingMode = TextWrappingModes.Normal;
        _tooltipDescription.overflowMode = TextOverflowModes.Overflow;
        _tooltipDescription.raycastTarget = false;

        _itemTooltip.SetActive(false);
    }

    private void ShowItemTooltip(ItemInstance item, Vector2 pointerPosition)
    {
        if (item?.definition == null || _itemTooltip == null)
        {
            HideItemTooltip();
            return;
        }
        _tooltipItem = item;
        _tooltipTitle.text = ResolveItemName(item.definition);
        string description = ResolveItemDescription(item.definition);
        _tooltipDescription.text = string.IsNullOrWhiteSpace(description)
            ? "No description available."
            : description;
        _itemTooltip.SetActive(true);
        _itemTooltip.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        PositionTooltip(pointerPosition);
    }

    private void HideItemTooltip()
    {
        _tooltipItem = null;
        if (_itemTooltip != null) _itemTooltip.SetActive(false);
    }

    private void PositionTooltip(Vector2 screenPosition)
    {
        if (_itemTooltip == null || !_itemTooltip.activeSelf) return;
        RectTransform rootRect = _root.GetComponent<RectTransform>();
        RectTransform tooltipRect = _itemTooltip.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRect, screenPosition,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main, out Vector2 local);
        float width = tooltipRect.rect.width;
        float height = tooltipRect.rect.height;
        float x = Mathf.Clamp(local.x + 18f, rootRect.rect.xMin, rootRect.rect.xMax - width);
        float y = Mathf.Clamp(local.y - 18f, rootRect.rect.yMin + height, rootRect.rect.yMax);
        tooltipRect.anchoredPosition = new Vector2(x, y);
    }

    private string ResolveItemName(ItemDefinition definition)
    {
        if (definition.IsAugment && augmentCatalogue != null
            && augmentCatalogue.TryGetAugment(definition.itemID, out AugmentEntry augment))
            return augment.displayName;
        return string.IsNullOrWhiteSpace(definition.displayName) ? definition.name : definition.displayName;
    }

    private string ResolveItemDescription(ItemDefinition definition)
    {
        if (definition.IsAugment && augmentCatalogue != null
            && augmentCatalogue.TryGetAugment(definition.itemID, out AugmentEntry augment))
            return augment.description;
        return definition.description;
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        _contextMenu = MakeRect("ContextMenu", _root.transform);
        var rt = _contextMenu.GetComponent<RectTransform>();
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(160f, 0f); // height set dynamically

        _contextMenu.AddComponent<Image>().color = contextBgColor;

        var vlg = _contextMenu.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        var csf = _contextMenu.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _contextMenu.SetActive(false);
    }

    private void ShowContextMenu(int slotIndex, Vector2 screenPos)
    {
        var item = inventory.GetSlot(slotIndex);
        if (item == null) { HideContextMenu(); return; }

        _contextSlotIndex = slotIndex;

        // Clear old buttons
        foreach (Transform child in _contextMenu.transform)
            Destroy(child.gameObject);

        // ── Equip options ─────────────────────────────────────────────────────
        if (item.definition.IsArmour)
        {
            var partName = item.definition.targetBodyPart.ToString();
            AddContextButton($"Equip to {partName}", () =>
            {
                var i = inventory.RemoveAt(_contextSlotIndex);
                if (i != null) equipment.TryEquipArmour(i);
                HideContextMenu();
            });
        }
        else if (item.definition.IsWeapon || (item.definition.IsConsumable && !item.definition.IsAmmo))
        {
            if (item.definition.IsTwoHanded)
            {
                AddContextButton("Equip (Two-Handed)", () =>
                {
                    var i = inventory.RemoveAt(_contextSlotIndex);
                    if (i != null) equipment.TryEquipHand(i, HandSlot.Left);
                    HideContextMenu();
                });
            }
            else
            {
                AddContextButton("Equip Left Hand", () =>
                {
                    var i = inventory.RemoveAt(_contextSlotIndex);
                    if (i != null) equipment.TryEquipHand(i, HandSlot.Left);
                    HideContextMenu();
                });
                AddContextButton("Equip Right Hand", () =>
                {
                    var i = inventory.RemoveAt(_contextSlotIndex);
                    if (i != null) equipment.TryEquipHand(i, HandSlot.Right);
                    HideContextMenu();
                });
            }
        }
        else if (item.definition.IsBag)
        {
            AddContextButton("Equip Bag", () =>
            {
                var i = inventory.RemoveAt(_contextSlotIndex);
                if (i != null) equipment.TryEquipBag(i);
                HideContextMenu();
            });
        }

        // Drop option always available
        AddContextButton("Drop", () =>
        {
            DropItemToWorld(_contextSlotIndex);
            HideContextMenu();
        });

        // ── Position menu at cursor ───────────────────────────────────────────
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _root.GetComponent<RectTransform>(),
            screenPos,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main,
            out var localPos);

        var menuRT = _contextMenu.GetComponent<RectTransform>();
        menuRT.anchoredPosition = localPos;
        _contextMenu.SetActive(true);
        _contextMenu.transform.SetAsLastSibling(); // render on top
    }

    private void AddContextButton(string label, System.Action onClick)
    {
        var go = MakeRect($"Btn_{label}", _contextMenu.transform);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 28f;

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.13f, 1f);

        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = new Color(0.1f, 0.1f, 0.13f, 1f);
        cb.highlightedColor = new Color(0.2f, 0.35f, 0.6f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(() => onClick());

        var textGO = MakeRect("Text", go.transform);
        Stretch(textGO.GetComponent<RectTransform>(), 8, 8, 2, 2);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 11;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white;
    }

    private void HideContextMenu()
    {
        if (_contextMenu != null) _contextMenu.SetActive(false);
        _contextSlotIndex = -1;
    }

    // ── Drag ghost ────────────────────────────────────────────────────────────

    private void BeginEquipmentDrag(EquipmentDragKind kind, BodyPart armourPart,
        ItemInstance item, Vector2 screenPosition)
    {
        if (item == null) return;
        _dragSourceIndex = -1;
        _equipmentDragKind = kind;
        _dragArmourPart = armourPart;
        HideContextMenu();
        HideItemTooltip();
        CreateDragGhost(item, screenPosition);
    }

    private void UnequipDraggedEquipment()
    {
        switch (_equipmentDragKind)
        {
            case EquipmentDragKind.Armour:
                equipment?.UnequipArmour(_dragArmourPart);
                break;
            case EquipmentDragKind.LeftHand:
                equipment?.UnequipHand(HandSlot.Left);
                break;
            case EquipmentDragKind.RightHand:
                equipment?.UnequipHand(HandSlot.Right);
                break;
            case EquipmentDragKind.Bag:
                equipment?.UnequipBag();
                break;
        }
    }

    private void FinishDragVisual()
    {
        if (_dragGhost != null) Destroy(_dragGhost);
        _dragGhost = null;
        _dragSourceIndex = -1;
        _equipmentDragKind = EquipmentDragKind.None;
    }

    private void CreateDragGhost(ItemInstance item, Vector2 screenPos)
    {
        if (_dragGhost != null) Destroy(_dragGhost);
        _dragGhost = MakeRect("DragGhost", _root.transform);
        var rt = _dragGhost.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        var img = _dragGhost.AddComponent<Image>();
        var c = item.definition.slotColor;
        img.color = new Color(c.r, c.g, c.b, 0.7f);
        img.raycastTarget = false;

        MoveGhost(screenPos);
    }

    private void MoveGhost(Vector2 screenPos)
    {
        if (_dragGhost == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.GetComponent<RectTransform>(),
            screenPos,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main,
            out var localPoint);
        _dragGhost.GetComponent<RectTransform>().anchoredPosition = localPoint;
    }

    // ── Drop to world ─────────────────────────────────────────────────────────

    private void DropItemToWorld(int slotIndex)
    {
        var item = inventory.RemoveAt(slotIndex);
        if (item == null) return;

        var cam = Camera.main.transform;
        var spawnPos = cam.position + cam.forward * dropDistance;

        GameObject worldObj;
        if (item.definition.worldPrefab != null)
        {
            worldObj = Instantiate(item.definition.worldPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning($"[Inventory] '{item.definition.displayName}' has no worldPrefab — spawning capsule.");
            worldObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            worldObj.transform.position = spawnPos;
            worldObj.transform.localScale = Vector3.one * 0.3f;
            var mr = worldObj.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mr.material.color = item.definition.slotColor;
            }
        }

        var pickup = worldObj.GetComponent<PickupItem>();
        if (pickup == null) pickup = worldObj.AddComponent<PickupItem>();
        pickup.item = item;

        Debug.Log($"[Inventory] Dropped '{item.definition.displayName}' at {spawnPos}.");
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshGrid()
    {
        if (inventory.SlotCount != _builtSlotCount)
            RebuildGridSlots();

        for (int i = 0; i < _gridSlots.Count; i++)
        {
            var widget = _gridSlots[i];
            var item = inventory.GetSlot(i);
            ApplyItemVisual(widget.background, widget.itemColor, item);
            bool showStack = item?.definition != null && item.definition.isStackable && item.stackCount > 1;
            widget.stackBadge.SetActive(showStack);
            if (showStack) widget.stackCount.text = item.stackCount.ToString();
        }

        if (_capacityLabel != null)
            _capacityLabel.text = $"{inventory.UsedSlots} / {inventory.SlotCount}";
    }

    private void RefreshEquipment()
    {
        foreach (var widget in _armourSlots)
        {
            if (widget == null) continue;
            var item = equipment.GetArmourSlot(widget.bodyPart);
            ApplyItemVisual(widget.background, widget.itemColor, item);
        }

        RefreshHandWidget(_leftHandWidget, HandSlot.Left);
        RefreshHandWidget(_rightHandWidget, HandSlot.Right);

        if (_bagWidget != null)
            ApplyItemVisual(_bagWidget.background, _bagWidget.itemColor, equipment.GetBagSlot());
    }

    private void RefreshHandWidget(HandSlotWidget widget, HandSlot hand)
    {
        if (widget == null) return;
        var item = equipment.GetHandSlot(hand);
        ApplyItemVisual(widget.background, widget.itemColor, item);
    }

    /// <summary>
    /// Applies icon sprite if available, falls back to slot color tint.
    /// </summary>
    private void ApplyItemVisual(Image background, Image itemColor, ItemInstance item)
    {
        if (item == null)
        {
            background.color = slotEmptyColor;
            itemColor.sprite = null;
            itemColor.color = Color.clear;
            return;
        }

        var def = item.definition;
        if (def.icon != null)
        {
            // Icon mode — neutral background, full-color icon sprite
            background.color = DarkenColor(def.slotColor);
            itemColor.sprite = def.icon;
            itemColor.color = Color.white; // don't tint the sprite
            itemColor.type = Image.Type.Simple;
            itemColor.preserveAspect = true;
        }
        else
        {
            // Color fallback — no icon assigned
            background.color = DarkenColor(def.slotColor);
            itemColor.sprite = null;
            itemColor.color = def.slotColor;
        }
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        if (IsOpen) return;
        _root.SetActive(true);
        AttachHealthHUD();
        if (_ammoHUD == null) _ammoHUD = FindAnyObjectByType<AmmoHUD>();
        if (_ammoHUD != null)
        {
            _ammoHudWasActive = _ammoHUD.gameObject.activeSelf;
            _ammoHUD.gameObject.SetActive(false);
        }
        RefreshGrid();
        RefreshEquipment();
        OnUIOpened?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        HideContextMenu();
        HideItemTooltip();
        FinishDragVisual();
        RestoreHealthHUD();
        if (_ammoHUD != null && _ammoHudWasActive) _ammoHUD.gameObject.SetActive(true);
        _root.SetActive(false);
        OnUIClosed?.Invoke();
    }

    private void AttachHealthHUD()
    {
        if (healthHUD == null || _healthAnchor == null || _healthOriginalParent != null) return;
        RectTransform rect = healthHUD.transform as RectTransform;
        if (rect == null) return;
        _healthOriginalParent = rect.parent;
        _healthOriginalSibling = rect.GetSiblingIndex();
        _healthOriginalAnchorMin = rect.anchorMin;
        _healthOriginalAnchorMax = rect.anchorMax;
        _healthOriginalPivot = rect.pivot;
        _healthOriginalPosition = rect.anchoredPosition;
        _healthOriginalSize = rect.sizeDelta;
        _healthOriginalScale = rect.localScale;
        _healthHudWasActive = healthHUD.gameObject.activeSelf;

        Canvas.ForceUpdateCanvases();
        float availableWidth = Mathf.Max(420f, _healthAnchor.rect.width - 20f);
        healthHUD.SetCompactInventoryMode(true, availableWidth);
        rect.SetParent(_healthAnchor, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(10f, -10f);
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        healthHUD.gameObject.SetActive(true);
    }

    private void RestoreHealthHUD()
    {
        if (healthHUD == null || _healthOriginalParent == null) return;
        RectTransform rect = healthHUD.transform as RectTransform;
        rect.SetParent(_healthOriginalParent, false);
        rect.SetSiblingIndex(Mathf.Min(_healthOriginalSibling, _healthOriginalParent.childCount - 1));
        rect.anchorMin = _healthOriginalAnchorMin;
        rect.anchorMax = _healthOriginalAnchorMax;
        rect.pivot = _healthOriginalPivot;
        rect.anchoredPosition = _healthOriginalPosition;
        rect.sizeDelta = _healthOriginalSize;
        rect.localScale = _healthOriginalScale;
        healthHUD.SetCompactInventoryMode(false);
        healthHUD.gameObject.SetActive(_healthHudWasActive);
        _healthOriginalParent = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color DarkenColor(Color c) =>
        new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 1f);

    private static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static void Stretch(RectTransform rt,
        float l = 0, float r = 0, float b = 0, float t = 0)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
    }

    private static void AddTrigger(EventTrigger trigger,
        EventTriggerType type,
        UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }
}
