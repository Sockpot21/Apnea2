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

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private PlayerInteraction playerInteraction;

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

    // Context menu
    private GameObject _contextMenu;
    private int _contextSlotIndex = -1;

    // Drag state
    private int _dragSourceIndex = -1;
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
        public TextMeshProUGUI tooltip;
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

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _canvas = GetComponentInParent<Canvas>();
        BuildUI();
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

        // Outer panel — centered, horizontal layout
        _outerPanel = MakeRect("OuterPanel", _root.transform);
        var outerRT = _outerPanel.GetComponent<RectTransform>();
        outerRT.anchorMin = new Vector2(0.5f, 0.5f);
        outerRT.anchorMax = new Vector2(0.5f, 0.5f);
        outerRT.pivot = new Vector2(0.5f, 0.5f);

        int rows = Mathf.CeilToInt((float)inventory.SlotCount / columns);
        float gridW = columns * slotSize + (columns - 1) * slotGap;
        float gridH = rows * slotSize + (rows - 1) * slotGap;
        float rightW = gridW + panelPadding * 2f;
        float rightH = gridH + panelPadding * 2f + 32f;
        float leftW = 220f;
        float totalW = leftW + rightW + 8f; // 8 = gap between panels
        float totalH = Mathf.Max(rightH, 440f);

        outerRT.sizeDelta = new Vector2(totalW, totalH);

        var outerHLG = _outerPanel.AddComponent<HorizontalLayoutGroup>();
        outerHLG.spacing = 8f;
        outerHLG.childForceExpandHeight = true;
        outerHLG.childForceExpandWidth = false;
        outerHLG.childControlHeight = true;
        outerHLG.childControlWidth = false;

        // ── Left panel (equipment) ────────────────────────────────────────────
        _leftPanel = MakeRect("EquipmentPanel", _outerPanel.transform);
        var leftRT = _leftPanel.GetComponent<RectTransform>();
        leftRT.sizeDelta = new Vector2(leftW, totalH);
        var leftImg = _leftPanel.AddComponent<Image>();
        leftImg.color = panelBgColor;
        var leftLE = _leftPanel.AddComponent<LayoutElement>();
        leftLE.preferredWidth = leftW;

        BuildEquipmentPanel(leftW, totalH);

        // ── Right panel (inventory grid) ──────────────────────────────────────
        _rightPanel = MakeRect("InventoryPanel", _outerPanel.transform);
        var rightRT = _rightPanel.GetComponent<RectTransform>();
        rightRT.sizeDelta = new Vector2(rightW, totalH);
        var rightImg = _rightPanel.AddComponent<Image>();
        rightImg.color = panelBgColor;
        var rightLE = _rightPanel.AddComponent<LayoutElement>();
        rightLE.preferredWidth = rightW;

        BuildInventoryGrid(rightW, gridW, gridH);

        // ── Context menu (built last so it renders on top) ────────────────────
        BuildContextMenu();

        _root.SetActive(false);
    }

    // ── Equipment panel (humanoid silhouette) ─────────────────────────────────

    private void BuildEquipmentPanel(float panelW, float panelH)
    {
        // Title
        var title = MakeRect("Title", _leftPanel.transform);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1); titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(0, -32f); titleRT.offsetMax = Vector2.zero;
        var titleTMP = title.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "EQUIPMENT"; titleTMP.fontSize = 15;
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

        // Silhouette layout — anchor slots relative to panel centre
        // Centre X of the panel
        float cx = 0f; // anchoredPosition is relative to panel centre

        // Vertical positions (Y from centre, positive = up)
        float top = panelH * 0.5f - 50f;

        // [Head]
        BuildArmourSlot(BodyPart.Head, cx, top - 0f, "Head");

        // [Chest] [Abdomen]
        BuildArmourSlot(BodyPart.Chest, cx - 34f, top - 70f, "Chest");
        BuildArmourSlot(BodyPart.Abdomen, cx + 34f, top - 70f, "Abdo");

        // [LUA] [LFA]   [RFA] [RUA]
        BuildArmourSlot(BodyPart.LeftUpperArm, cx - 80f, top - 60f, "LUA");
        BuildArmourSlot(BodyPart.LeftForearm, cx - 80f, top - 120f, "LFA");
        BuildArmourSlot(BodyPart.RightUpperArm, cx + 80f, top - 60f, "RUA");
        BuildArmourSlot(BodyPart.RightForearm, cx + 80f, top - 120f, "RFA");

        // [LThigh] [RThigh]
        BuildArmourSlot(BodyPart.LeftThigh, cx - 34f, top - 170f, "LThigh");
        BuildArmourSlot(BodyPart.RightThigh, cx + 34f, top - 170f, "RThigh");

        // [LShin] [RShin]
        BuildArmourSlot(BodyPart.LeftShin, cx - 34f, top - 240f, "LShin");
        BuildArmourSlot(BodyPart.RightShin, cx + 34f, top - 240f, "RShin");

        // Hand slots — below silhouette
        float handY = top - 310f;
        BuildHandSlot(HandSlot.Left, cx - 42f, handY, "L.Hand");
        BuildHandSlot(HandSlot.Right, cx + 42f, handY, "R.Hand");
    }

    private void BuildArmourSlot(BodyPart part, float x, float y, string shortLabel)
    {
        var go = MakeRect($"Armour_{part}", _leftPanel.transform);
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
        border.AddComponent<Image>().color = slotBorderColor;

        var bg = go.AddComponent<Image>();
        bg.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        var colorImg = colorGO.AddComponent<Image>();
        colorImg.color = Color.clear;

        var labelGO = MakeRect("Label", go.transform);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = new Vector2(1, 0);
        labelRT.pivot = new Vector2(0.5f, 1f);
        labelRT.offsetMin = new Vector2(0, -14f); labelRT.offsetMax = new Vector2(0, 0);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = shortLabel; labelTMP.fontSize = 8;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = new Color(0.7f, 0.7f, 0.7f);

        // Drag-drop target
        var trigger = go.AddComponent<EventTrigger>();
        BodyPart capturedPart = part;
        Image capturedBg = bg;

        AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            capturedBg.color = slotHoverColor);
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            capturedBg.color = equipment?.GetArmourSlot(capturedPart) != null
                ? DarkenColor(equipment.GetArmourSlot(capturedPart).slotColor)
                : slotEmptyColor);
        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_dragSourceIndex < 0) return;
            var item = inventory.GetSlot(_dragSourceIndex);
            if (item == null || !item.IsArmour) return;
            if (item.targetBodyPart != capturedPart) return;
            inventory.RemoveAt(_dragSourceIndex);
            equipment.TryEquipArmour(item);
            _dragSourceIndex = -1;
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
        var go = MakeRect($"Hand_{hand}", _leftPanel.transform);
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
        border.AddComponent<Image>().color = new Color(0.4f, 0.3f, 0.1f, 1f);

        var bg = go.AddComponent<Image>();
        bg.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        var colorImg = colorGO.AddComponent<Image>();
        colorImg.color = Color.clear;

        var labelGO = MakeRect("Label", go.transform);
        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero; labelRT.anchorMax = new Vector2(1, 0);
        labelRT.pivot = new Vector2(0.5f, 1f);
        labelRT.offsetMin = new Vector2(0, -14f); labelRT.offsetMax = new Vector2(0, 0);
        var labelTMP = labelGO.AddComponent<TextMeshProUGUI>();
        labelTMP.text = label; labelTMP.fontSize = 9;
        labelTMP.alignment = TextAlignmentOptions.Center;
        labelTMP.color = new Color(0.8f, 0.6f, 0.2f);

        var trigger = go.AddComponent<EventTrigger>();
        HandSlot capturedHand = hand;
        Image capturedBg = bg;

        AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
            capturedBg.color = slotHoverColor);
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
            capturedBg.color = equipment?.GetHandSlot(capturedHand) != null
                ? DarkenColor(GetHandItemColor(capturedHand))
                : slotEmptyColor);

        // Right-click to unequip
        AddTrigger(trigger, EventTriggerType.PointerClick, data =>
        {
            var pd = (PointerEventData)data;
            if (pd.button == PointerEventData.InputButton.Right)
                equipment?.UnequipHand(capturedHand);
        });

        // Drop target for drag-equip
        AddTrigger(trigger, EventTriggerType.Drop, _ =>
        {
            if (_dragSourceIndex < 0) return;
            var item = inventory.GetSlot(_dragSourceIndex);
            if (item == null || (!item.IsWeapon && !item.IsConsumable)) return;
            inventory.RemoveAt(_dragSourceIndex);
            equipment.TryEquipHand(item, capturedHand);
            _dragSourceIndex = -1;
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

    // ── Inventory grid ────────────────────────────────────────────────────────

    private void BuildInventoryGrid(float panelW, float gridW, float gridH)
    {
        // Title
        var title = MakeRect("Title", _rightPanel.transform);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1); titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1f);
        titleRT.offsetMin = new Vector2(0, -32f); titleRT.offsetMax = Vector2.zero;
        var titleTMP = title.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "INVENTORY"; titleTMP.fontSize = 15;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;

        float gridOriginX = -gridW * 0.5f;
        float gridOriginY = gridH * 0.5f - 40f;

        for (int i = 0; i < inventory.SlotCount; i++)
        {
            int col = i % columns;
            int row = i / columns;
            float x = gridOriginX + col * (slotSize + slotGap) + slotSize * 0.5f;
            float y = gridOriginY - row * (slotSize + slotGap) - slotSize * 0.5f;
            _gridSlots.Add(BuildGridSlot(i, x, y));
        }
    }

    private SlotWidget BuildGridSlot(int index, float x, float y)
    {
        var widget = new SlotWidget { index = index };

        var go = MakeRect($"Slot_{index}", _rightPanel.transform);
        widget.root = go;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        // Border
        var border = MakeRect("Border", go.transform);
        var borderRT = border.GetComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = new Vector2(-2, -2); borderRT.offsetMax = new Vector2(2, 2);
        border.AddComponent<Image>().color = slotBorderColor;

        widget.background = go.AddComponent<Image>();
        widget.background.color = slotEmptyColor;

        var colorGO = MakeRect("ItemColor", go.transform);
        var colorRT = colorGO.GetComponent<RectTransform>();
        colorRT.anchorMin = new Vector2(0.1f, 0.1f); colorRT.anchorMax = new Vector2(0.9f, 0.9f);
        colorRT.offsetMin = Vector2.zero; colorRT.offsetMax = Vector2.zero;
        widget.itemColor = colorGO.AddComponent<Image>();
        widget.itemColor.color = Color.clear;

        // Tooltip
        var ttGO = MakeRect("Tooltip", go.transform);
        var ttRT = ttGO.GetComponent<RectTransform>();
        ttRT.anchorMin = new Vector2(0, 1); ttRT.anchorMax = new Vector2(1, 1);
        ttRT.pivot = new Vector2(0.5f, 0f);
        ttRT.offsetMin = new Vector2(0, 2f); ttRT.offsetMax = new Vector2(0, 22f);
        ttGO.AddComponent<Image>().color = new Color(0.02f, 0.02f, 0.04f, 0.9f);
        var ttChild = MakeRect("Text", ttGO.transform);
        var ttChildRT = ttChild.GetComponent<RectTransform>();
        ttChildRT.anchorMin = Vector2.zero; ttChildRT.anchorMax = Vector2.one;
        ttChildRT.offsetMin = new Vector2(4, 2); ttChildRT.offsetMax = new Vector2(-4, -2);
        widget.tooltip = ttChild.AddComponent<TextMeshProUGUI>();
        widget.tooltip.fontSize = 9;
        widget.tooltip.alignment = TextAlignmentOptions.Center;
        widget.tooltip.color = Color.white;
        ttGO.SetActive(false);

        var trigger = go.AddComponent<EventTrigger>();
        int captured = index;

        AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
        {
            var item = inventory.GetSlot(captured);
            if (item != null)
            {
                widget.tooltip.text = item.displayName;
                widget.tooltip.transform.parent.gameObject.SetActive(true);
            }
            widget.background.color = slotHoverColor;
        });

        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            widget.tooltip.transform.parent.gameObject.SetActive(false);
            widget.background.color = inventory.GetSlot(captured) != null
                ? DarkenColor(inventory.GetSlot(captured).slotColor)
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
            _dragSourceIndex = captured;
            HideContextMenu();
            CreateDragGhost(inventory.GetSlot(captured), ((PointerEventData)data).position);
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
            Destroy(_dragGhost); _dragGhost = null;

            var pointerData = (PointerEventData)data;
            bool onPanel = RectTransformUtility.RectangleContainsScreenPoint(
                _outerPanel.GetComponent<RectTransform>(),
                pointerData.position,
                _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main);

            if (!onPanel && _dragSourceIndex >= 0)
                DropItemToWorld(_dragSourceIndex);

            _dragSourceIndex = -1;
        });

        return widget;
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
        if (item.IsArmour)
        {
            var partName = item.targetBodyPart.ToString();
            AddContextButton($"Equip to {partName}", () =>
            {
                var i = inventory.RemoveAt(_contextSlotIndex);
                if (i != null) equipment.TryEquipArmour(i);
                HideContextMenu();
            });
        }
        else if (item.IsWeapon || item.IsConsumable)
        {
            if (item.IsTwoHanded)
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

    private void CreateDragGhost(ItemDefinition item, Vector2 screenPos)
    {
        _dragGhost = MakeRect("DragGhost", _root.transform);
        var rt = _dragGhost.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(slotSize, slotSize);

        var img = _dragGhost.AddComponent<Image>();
        img.color = new Color(item.slotColor.r, item.slotColor.g, item.slotColor.b, 0.7f);
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
        if (item.worldPrefab != null)
        {
            worldObj = Instantiate(item.worldPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning($"[Inventory] '{item.displayName}' has no worldPrefab — spawning capsule.");
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

        var pickup = worldObj.GetComponent<PickupItem>();
        if (pickup == null) pickup = worldObj.AddComponent<PickupItem>();
        pickup.item = item;

        Debug.Log($"[Inventory] Dropped '{item.displayName}' at {spawnPos}.");
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshGrid()
    {
        for (int i = 0; i < _gridSlots.Count; i++)
        {
            var widget = _gridSlots[i];
            var item = inventory.GetSlot(i);
            widget.background.color = item != null ? DarkenColor(item.slotColor) : slotEmptyColor;
            widget.itemColor.color = item != null ? item.slotColor : Color.clear;
        }
    }

    private void RefreshEquipment()
    {
        // Armour slots
        foreach (var widget in _armourSlots)
        {
            if (widget == null) continue;
            var item = equipment.GetArmourSlot(widget.bodyPart);
            widget.background.color = item != null
                ? DarkenColor(item.slotColor) : slotEmptyColor;
            widget.itemColor.color = item != null
                ? item.slotColor : Color.clear;
        }

        // Hand slots
        RefreshHandWidget(_leftHandWidget, HandSlot.Left);
        RefreshHandWidget(_rightHandWidget, HandSlot.Right);
    }

    private void RefreshHandWidget(HandSlotWidget widget, HandSlot hand)
    {
        if (widget == null) return;
        var item = equipment.GetHandSlot(hand);
        widget.background.color = item != null ? DarkenColor(GetHandItemColor(hand)) : slotEmptyColor;
        widget.itemColor.color = item != null ? GetHandItemColor(hand) : Color.clear;
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        _root.SetActive(true);
        RefreshGrid();
        RefreshEquipment();
        OnUIOpened?.Invoke();
    }

    public void Close()
    {
        HideContextMenu();
        if (_dragGhost != null) { Destroy(_dragGhost); _dragGhost = null; }
        _dragSourceIndex = -1;
        _root.SetActive(false);
        OnUIClosed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Color GetHandItemColor(HandSlot hand)
    {
        var item = equipment.GetHandSlot(hand);
        return item != null ? item.slotColor : Color.clear;
    }

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
