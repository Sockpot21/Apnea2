// ModificationStationUI.cs
// Builds its entire UI hierarchy from scratch in code.
// Only requires one empty GameObject on the Canvas named "ModificationStationUI"
// with this script attached, RectTransform stretched to fill Canvas.
//
// ── Inspector fields ──────────────────────────────────────────────────────────
//   Health Manager    → HealthManager component
//   Augment Catalogue → AugmentCatalogue ScriptableObject
//   Canvas Rect       → RectTransform on the Canvas GameObject
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModificationStationUI : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private HealthManager healthManager;
    [SerializeField] private AugmentCatalogue augmentCatalogue;
    [SerializeField] private RectTransform canvasRect;

    // ── Colours ───────────────────────────────────────────────────────────────
    private static readonly Color ColBg = new Color(0.08f, 0.08f, 0.10f, 0.97f);
    private static readonly Color ColHeader = new Color(0.05f, 0.05f, 0.07f, 1.00f);
    private static readonly Color ColTabActive = new Color(0.20f, 0.55f, 1.00f, 1.00f);
    private static readonly Color ColTabStatus = new Color(0.15f, 0.48f, 0.28f, 1.00f); // green tint for status tab
    private static readonly Color ColTabIdle = new Color(0.18f, 0.18f, 0.22f, 1.00f);
    private static readonly Color ColRowNormal = new Color(0.12f, 0.12f, 0.15f, 1.00f);
    private static readonly Color ColRowHover = new Color(0.20f, 0.22f, 0.30f, 1.00f);
    private static readonly Color ColOverlayBg = new Color(0.04f, 0.04f, 0.06f, 0.97f);
    private static readonly Color ColBtnClose = new Color(0.45f, 0.10f, 0.10f, 1.00f);
    private static readonly Color ColBtnYes = new Color(0.10f, 0.42f, 0.10f, 1.00f);
    private static readonly Color ColBtnNo = new Color(0.42f, 0.10f, 0.10f, 1.00f);
    private static readonly Color ColSeparator = new Color(0.25f, 0.55f, 1.00f, 0.40f);
    private static readonly Color ColHealthy = new Color(0.20f, 0.90f, 0.30f, 1.00f);
    private static readonly Color ColDamaged = new Color(0.90f, 0.80f, 0.10f, 1.00f);
    private static readonly Color ColCritical = new Color(0.90f, 0.20f, 0.10f, 1.00f);
    private static readonly Color ColDestroyed = new Color(0.35f, 0.35f, 0.35f, 1.00f);
    private static readonly Color ColSubBar = new Color(0.25f, 0.55f, 1.00f, 1.00f);

    // ── Sentinel value for the status tab ────────────────────────────────────
    // We use a separate bool rather than abusing the BodyPart enum
    private const string STATUS_TAB_ID = "__STATUS__";

    // ── Built references ──────────────────────────────────────────────────────
    private GameObject _root;
    private Transform _tabContent;

    // Augment list panel
    private GameObject _augmentPanel;
    private Transform _listContent;
    private ScrollRect _listScroll;

    // Status panel
    private GameObject _statusPanel;
    private Transform _statusContent;
    private ScrollRect _statusScroll;

    // Confirm overlay
    private GameObject _confirmOverlay;
    private TextMeshProUGUI _confirmText;

    // ── Runtime state ─────────────────────────────────────────────────────────
    private BodyPart _selectedPart = BodyPart.Head;
    private bool _statusTabActive = false;
    private AugmentEntry _pending;

    // Tab tracking — parallel lists, index matches
    private readonly List<Image> _tabImages = new();
    private readonly List<string> _tabIds = new(); // BodyPart.ToString() or STATUS_TAB_ID
    private readonly List<GameObject> _augRows = new();

    // Status panel live widgets — rebuilt once on status tab open, updated in Update()
    private class StatusBodyPartWidget
    {
        public BodyPart bodyPart;
        public TextMeshProUGUI headerLabel;
        public Image conditionBar;
        public GameObject subPartPanel;
        public bool expanded;
        public Button toggleBtn;
        public List<StatusSubPartWidget> subWidgets = new();
    }
    private class StatusSubPartWidget
    {
        public string subPartID;   // matched against RuntimeSubPart.subPartID
        public bool isOrgan;
        public TextMeshProUGUI hpLabel;
        public Image hpBar;
    }
    private readonly List<StatusBodyPartWidget> _statusWidgets = new();

    public event System.Action OnUIOpened;
    public event System.Action OnUIClosed;
    public bool IsOpen => _root != null && _root.activeSelf;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake() => BuildUI();

    private void Update()
    {
        if (!IsOpen || !_statusTabActive) return;
        RefreshStatusWidgets();
    }

    // ── Master build ─────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // Root
        _root = MakeRect("StationRoot", transform);
        Stretch(_root.GetComponent<RectTransform>(), 60, 60, 60, 60);
        MakeImage(_root, ColBg);
        _root.SetActive(false);

        // Header
        var header = MakeRect("Header", _root.transform);
        var headerRT = header.GetComponent<RectTransform>();
        PinTop(headerRT, 0, 56);
        MakeImage(header, ColHeader);
        var titleGO = MakeRect("Title", header.transform);
        Stretch(titleGO.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "MODIFICATION STATION";
        titleTMP.fontSize = 20;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color = Color.white;

        // Separator under header
        MakeSeparator(_root.transform, -56, -58);

        // Tab row
        BuildTabRow();

        // Separator under tabs
        MakeSeparator(_root.transform, -102, -104);

        // Augment list panel (default visible)
        BuildAugmentPanel();

        // Status panel (default hidden)
        BuildStatusPanel();

        // Close button
        var closeGO = MakeRect("CloseBtn", _root.transform);
        var closeRT = closeGO.GetComponent<RectTransform>();
        PinBottom(closeRT, 6, 46, 8, 8);
        MakeImage(closeGO, ColBtnClose);
        var closeBtn = closeGO.AddComponent<Button>();
        StyleBtn(closeBtn, ColBtnClose);
        closeBtn.onClick.AddListener(Close);
        MakeBtnLabel(closeGO.transform, "CLOSE", 14);

        // Confirm overlay (floats above everything)
        BuildConfirmOverlay();

        _root.SetActive(false);
    }

    // ── Tab row ───────────────────────────────────────────────────────────────

    private void BuildTabRow()
    {
        var tabRow = MakeRect("TabRow", _root.transform);
        var tabRowRT = tabRow.GetComponent<RectTransform>();
        tabRowRT.anchorMin = new Vector2(0, 1);
        tabRowRT.anchorMax = new Vector2(1, 1);
        tabRowRT.pivot = new Vector2(0.5f, 1f);
        tabRowRT.offsetMin = new Vector2(0, -102);
        tabRowRT.offsetMax = new Vector2(0, -58);
        MakeImage(tabRow, ColHeader);

        var tabSR = tabRow.AddComponent<ScrollRect>();
        tabSR.horizontal = true;
        tabSR.vertical = false;
        tabSR.scrollSensitivity = 20f;
        tabSR.movementType = ScrollRect.MovementType.Clamped;
        tabSR.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        var tabVP = MakeRect("Viewport", tabRow.transform);
        Stretch(tabVP.GetComponent<RectTransform>(), 4, 4, 4, 4);
        var tabMask = tabVP.AddComponent<Mask>();
        tabMask.showMaskGraphic = false;
        var tabVPImg = tabVP.AddComponent<Image>();
        tabVPImg.color = new Color(0, 0, 0, 0.01f);
        tabSR.viewport = tabVP.GetComponent<RectTransform>();

        var tabContentGO = MakeRect("TabContent", tabVP.transform);
        var tabContentRT = tabContentGO.GetComponent<RectTransform>();
        tabContentRT.anchorMin = new Vector2(0, 0);
        tabContentRT.anchorMax = new Vector2(0, 1);
        tabContentRT.pivot = new Vector2(0, 0.5f);
        tabContentRT.offsetMin = Vector2.zero;
        tabContentRT.offsetMax = Vector2.zero;
        var tabHLG = tabContentGO.AddComponent<HorizontalLayoutGroup>();
        tabHLG.spacing = 4f;
        tabHLG.padding = new RectOffset(4, 4, 4, 4);
        tabHLG.childForceExpandWidth = false;
        tabHLG.childForceExpandHeight = true;
        tabHLG.childControlWidth = false;
        tabHLG.childControlHeight = true;
        var tabCSF = tabContentGO.AddComponent<ContentSizeFitter>();
        tabCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        tabSR.content = tabContentRT;
        _tabContent = tabContentGO.transform;

        // Status tab — always first
        BuildSpecialTab(STATUS_TAB_ID, "BODY\nSTATUS", ColTabStatus);

        // One tab per body part
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            BuildBodyPartTab(part);
    }

    private void BuildSpecialTab(string id, string label, Color color)
    {
        var tabGO = MakeRect($"Tab_{id}", _tabContent);
        tabGO.GetComponent<RectTransform>().sizeDelta = new Vector2(88f, 0f);
        var img = MakeImage(tabGO, color);
        _tabImages.Add(img);
        _tabIds.Add(id);

        var btn = tabGO.AddComponent<Button>();
        var cb = btn.colors;
        cb.highlightedColor = color * 1.3f;
        btn.colors = cb;

        var labelGO = MakeRect("Label", tabGO.transform);
        Stretch(labelGO.GetComponent<RectTransform>(), 4, 4, 4, 4);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 10;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        btn.onClick.AddListener(() => SelectStatusTab());
    }

    private void BuildBodyPartTab(BodyPart part)
    {
        var id = part.ToString();
        var tabGO = MakeRect($"Tab_{id}", _tabContent);
        tabGO.GetComponent<RectTransform>().sizeDelta = new Vector2(88f, 0f);
        var img = MakeImage(tabGO, ColTabIdle);
        _tabImages.Add(img);
        _tabIds.Add(id);

        var btn = tabGO.AddComponent<Button>();
        var cb = btn.colors;
        cb.highlightedColor = new Color(0.3f, 0.5f, 0.85f, 1f);
        btn.colors = cb;

        var labelGO = MakeRect("Label", tabGO.transform);
        Stretch(labelGO.GetComponent<RectTransform>(), 4, 4, 4, 4);
        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text = SplitCamel(part.ToString());
        tmp.fontSize = 10;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.Normal;

        var capturedPart = part;
        btn.onClick.AddListener(() => SelectAugmentTab(capturedPart));
    }

    // ── Augment panel ─────────────────────────────────────────────────────────

    private void BuildAugmentPanel()
    {
        _augmentPanel = MakeRect("AugmentPanel", _root.transform);
        var rt = _augmentPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, 50);
        rt.offsetMax = new Vector2(0, -104);

        _listScroll = _augmentPanel.AddComponent<ScrollRect>();
        _listScroll.horizontal = false;
        _listScroll.vertical = true;
        _listScroll.scrollSensitivity = 40f;
        _listScroll.movementType = ScrollRect.MovementType.Clamped;

        var vp = MakeRect("Viewport", _augmentPanel.transform);
        Stretch(vp.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var mask = vp.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        vp.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        _listScroll.viewport = vp.GetComponent<RectTransform>();

        var contentGO = MakeRect("Content", vp.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5f;
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _listScroll.content = contentRT;
        _listContent = contentGO.transform;
    }

    // ── Status panel ──────────────────────────────────────────────────────────

    private void BuildStatusPanel()
    {
        _statusPanel = MakeRect("StatusPanel", _root.transform);
        var rt = _statusPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(0, 50);
        rt.offsetMax = new Vector2(0, -104);

        _statusScroll = _statusPanel.AddComponent<ScrollRect>();
        _statusScroll.horizontal = false;
        _statusScroll.vertical = true;
        _statusScroll.scrollSensitivity = 40f;
        _statusScroll.movementType = ScrollRect.MovementType.Clamped;

        var vp = MakeRect("Viewport", _statusPanel.transform);
        Stretch(vp.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var mask = vp.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        vp.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        _statusScroll.viewport = vp.GetComponent<RectTransform>();

        var contentGO = MakeRect("Content", vp.transform);
        var contentRT = contentGO.GetComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0, 1);
        contentRT.anchorMax = new Vector2(1, 1);
        contentRT.pivot = new Vector2(0.5f, 1f);
        contentRT.offsetMin = Vector2.zero;
        contentRT.offsetMax = Vector2.zero;
        var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(8, 8, 8, 8);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        var csf = contentGO.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _statusScroll.content = contentRT;
        _statusContent = contentGO.transform;

        _statusPanel.SetActive(false);
    }

    // ── Confirm overlay ───────────────────────────────────────────────────────

    private void BuildConfirmOverlay()
    {
        _confirmOverlay = MakeRect("ConfirmOverlay", _root.transform);
        var rt = _confirmOverlay.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(420f, 180f);
        MakeImage(_confirmOverlay, ColOverlayBg);

        // Border tint
        var border = MakeRect("Border", _confirmOverlay.transform);
        Stretch(border.GetComponent<RectTransform>(), 0, 0, 0, 0);
        border.AddComponent<Image>().color =
            new Color(ColTabActive.r, ColTabActive.g, ColTabActive.b, 0.25f);

        // Confirm text
        var ctGO = MakeRect("ConfirmText", _confirmOverlay.transform);
        var ctRT = ctGO.GetComponent<RectTransform>();
        ctRT.anchorMin = new Vector2(0, 1);
        ctRT.anchorMax = new Vector2(1, 1);
        ctRT.pivot = new Vector2(0.5f, 1f);
        ctRT.offsetMin = new Vector2(16, -90);
        ctRT.offsetMax = new Vector2(-16, -12);
        _confirmText = ctGO.AddComponent<TextMeshProUGUI>();
        _confirmText.fontSize = 14;
        _confirmText.alignment = TextAlignmentOptions.Center;
        _confirmText.color = Color.white;
        _confirmText.textWrappingMode = TextWrappingModes.Normal;

        // Yes
        var yesGO = MakeRect("YesBtn", _confirmOverlay.transform);
        var yesRT = yesGO.GetComponent<RectTransform>();
        yesRT.anchorMin = new Vector2(0, 0);
        yesRT.anchorMax = new Vector2(0.5f, 0);
        yesRT.pivot = new Vector2(0.5f, 0f);
        yesRT.offsetMin = new Vector2(12, 10);
        yesRT.offsetMax = new Vector2(-4, 44);
        MakeImage(yesGO, ColBtnYes);
        var yesBtn = yesGO.AddComponent<Button>();
        StyleBtn(yesBtn, ColBtnYes);
        yesBtn.onClick.AddListener(ConfirmInstall);
        MakeBtnLabel(yesGO.transform, "INSTALL", 13);

        // No
        var noGO = MakeRect("NoBtn", _confirmOverlay.transform);
        var noRT = noGO.GetComponent<RectTransform>();
        noRT.anchorMin = new Vector2(0.5f, 0);
        noRT.anchorMax = new Vector2(1, 0);
        noRT.pivot = new Vector2(0.5f, 0f);
        noRT.offsetMin = new Vector2(4, 10);
        noRT.offsetMax = new Vector2(-12, 44);
        MakeImage(noGO, ColBtnNo);
        var noBtn = noGO.AddComponent<Button>();
        StyleBtn(noBtn, ColBtnNo);
        noBtn.onClick.AddListener(CancelConfirm);
        MakeBtnLabel(noGO.transform, "CANCEL", 13);

        _confirmOverlay.SetActive(false);
    }

    // ── Open / Close ──────────────────────────────────────────────────────────

    public void Open()
    {
        _root.SetActive(true);
        // Default to status tab on open so player sees their body state immediately
        SelectStatusTab();
        OnUIOpened?.Invoke();
    }

    public void Close()
    {
        _confirmOverlay.SetActive(false);
        _pending = null;
        _root.SetActive(false);
        OnUIClosed?.Invoke();
    }

    // ── Tab selection ─────────────────────────────────────────────────────────

    private void SelectStatusTab()
    {
        _statusTabActive = true;
        _confirmOverlay.SetActive(false);
        _pending = null;

        // Colour all tabs — status tab gets its own colour when active
        for (int i = 0; i < _tabImages.Count; i++)
        {
            if (_tabIds[i] == STATUS_TAB_ID)
                _tabImages[i].color = _statusTabActive ? ColTabStatus * 1.3f : ColTabStatus;
            else
                _tabImages[i].color = ColTabIdle;
        }

        _augmentPanel.SetActive(false);
        _statusPanel.SetActive(true);

        // Build widgets if not yet built
        if (_statusWidgets.Count == 0)
            BuildStatusWidgets();

        RefreshStatusWidgets();
    }

    private void SelectAugmentTab(BodyPart part)
    {
        _selectedPart = part;
        _statusTabActive = false;
        _confirmOverlay.SetActive(false);
        _pending = null;

        for (int i = 0; i < _tabImages.Count; i++)
        {
            if (_tabIds[i] == STATUS_TAB_ID)
                _tabImages[i].color = ColTabStatus;
            else
                _tabImages[i].color = _tabIds[i] == part.ToString() ? ColTabActive : ColTabIdle;
        }

        _statusPanel.SetActive(false);
        _augmentPanel.SetActive(true);

        PopulateAugmentList(part);
        if (_listScroll != null)
            _listScroll.verticalNormalizedPosition = 1f;
    }

    // ── Status widgets ────────────────────────────────────────────────────────

    private void BuildStatusWidgets()
    {
        // Clear any old widgets
        foreach (var w in _statusWidgets)
            if (w.subPartPanel != null) Destroy(w.subPartPanel.transform.parent.gameObject);
        _statusWidgets.Clear();

        if (healthManager == null) return;
        var body = healthManager.GetFullBody();

        foreach (var kvp in body)
        {
            var runtimePart = kvp.Value;
            var widget = new StatusBodyPartWidget { bodyPart = kvp.Key };

            // ── Outer container ───────────────────────────────────────────────
            var container = MakeRect($"Status_{kvp.Key}", _statusContent);
            var le = container.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            MakeImage(container, new Color(0.10f, 0.10f, 0.13f, 1f));
            var containerVLG = container.AddComponent<VerticalLayoutGroup>();
            containerVLG.spacing = 0f;
            containerVLG.childForceExpandWidth = true;
            containerVLG.childForceExpandHeight = false;
            containerVLG.childControlWidth = true;
            containerVLG.childControlHeight = false;
            var containerCSF = container.AddComponent<ContentSizeFitter>();
            containerCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Header row ────────────────────────────────────────────────────
            var headerRow = MakeRect("Header", container.transform);
            var headerLE = headerRow.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 36f;
            headerLE.flexibleWidth = 1f;
            MakeImage(headerRow, new Color(0.14f, 0.14f, 0.18f, 1f));

            // Toggle button covering header
            widget.toggleBtn = headerRow.AddComponent<Button>();
            var btnCB = widget.toggleBtn.colors;
            btnCB.highlightedColor = new Color(0.22f, 0.22f, 0.28f, 1f);
            widget.toggleBtn.colors = btnCB;
            var capturedWidget = widget;
            widget.toggleBtn.onClick.AddListener(() => ToggleStatusExpand(capturedWidget));

            // Accent bar on left
            var accent = MakeRect("Accent", headerRow.transform);
            var accentRT = accent.GetComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0, 0);
            accentRT.anchorMax = new Vector2(0, 1);
            accentRT.offsetMin = new Vector2(0, 0);
            accentRT.offsetMax = new Vector2(4, 0);
            MakeImage(accent, ColTabStatus);

            // Part name label
            var nameGO = MakeRect("Name", headerRow.transform);
            var nameRT = nameGO.GetComponent<RectTransform>();
            nameRT.anchorMin = new Vector2(0, 0);
            nameRT.anchorMax = new Vector2(0.5f, 1);
            nameRT.offsetMin = new Vector2(12, 0);
            nameRT.offsetMax = new Vector2(0, 0);
            widget.headerLabel = nameGO.AddComponent<TextMeshProUGUI>();
            widget.headerLabel.fontSize = 13;
            widget.headerLabel.fontStyle = FontStyles.Bold;
            widget.headerLabel.alignment = TextAlignmentOptions.MidlineLeft;
            widget.headerLabel.color = Color.white;
            widget.headerLabel.textWrappingMode = TextWrappingModes.NoWrap;

            // Condition bar background
            var barBgGO = MakeRect("BarBg", headerRow.transform);
            var barBgRT = barBgGO.GetComponent<RectTransform>();
            barBgRT.anchorMin = new Vector2(0.5f, 0.5f);
            barBgRT.anchorMax = new Vector2(0.95f, 0.5f);
            barBgRT.pivot = new Vector2(0.5f, 0.5f);
            barBgRT.sizeDelta = new Vector2(0f, 12f);
            MakeImage(barBgGO, new Color(0.08f, 0.08f, 0.10f, 1f));

            // Condition bar fill
            var barFillGO = MakeRect("Fill", barBgGO.transform);
            Stretch(barFillGO.GetComponent<RectTransform>(), 0, 0, 0, 0);
            barFillGO.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
            widget.conditionBar = barFillGO.AddComponent<Image>();
            widget.conditionBar.type = Image.Type.Filled;
            widget.conditionBar.fillMethod = Image.FillMethod.Horizontal;
            widget.conditionBar.fillOrigin = 0;

            // ── Sub-part panel (collapsed by default) ─────────────────────────
            widget.subPartPanel = MakeRect("SubParts", container.transform);
            var spVLG = widget.subPartPanel.AddComponent<VerticalLayoutGroup>();
            spVLG.spacing = 2f;
            spVLG.padding = new RectOffset(16, 8, 4, 6);
            spVLG.childForceExpandWidth = true;
            spVLG.childForceExpandHeight = false;
            spVLG.childControlWidth = true;
            spVLG.childControlHeight = false;
            var spCSF = widget.subPartPanel.AddComponent<ContentSizeFitter>();
            spCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var spLE = widget.subPartPanel.AddComponent<LayoutElement>();
            spLE.flexibleWidth = 1f;

            // Build sub-part rows
            foreach (var sp in runtimePart.layers)
                widget.subWidgets.Add(BuildSubPartWidget(widget.subPartPanel.transform, sp.subPartID, sp.displayName, false));
            foreach (var organ in runtimePart.organs)
                widget.subWidgets.Add(BuildSubPartWidget(widget.subPartPanel.transform, organ.subPartID, organ.displayName, true));

            widget.subPartPanel.SetActive(false);
            _statusWidgets.Add(widget);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_statusContent.GetComponent<RectTransform>());
    }

    private StatusSubPartWidget BuildSubPartWidget(Transform parent, string id, string displayName, bool isOrgan)
    {
        var row = MakeRect($"SP_{id}", parent);
        var rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 22f;
        rowLE.flexibleWidth = 1f;

        // Name
        var nameGO = MakeRect("Name", row.transform);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0);
        nameRT.anchorMax = new Vector2(0.38f, 1);
        nameRT.offsetMin = Vector2.zero;
        nameRT.offsetMax = Vector2.zero;
        var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
        nameTMP.text = isOrgan ? $"[O] {displayName}" : displayName;
        nameTMP.fontSize = 11;
        nameTMP.alignment = TextAlignmentOptions.MidlineLeft;
        nameTMP.color = isOrgan ? new Color(0.8f, 0.6f, 1f) : new Color(0.75f, 0.75f, 0.75f);
        nameTMP.textWrappingMode = TextWrappingModes.NoWrap;
        nameTMP.overflowMode = TextOverflowModes.Ellipsis;

        // HP label
        var hpGO = MakeRect("HP", row.transform);
        var hpRT = hpGO.GetComponent<RectTransform>();
        hpRT.anchorMin = new Vector2(0.38f, 0);
        hpRT.anchorMax = new Vector2(0.58f, 1);
        hpRT.offsetMin = Vector2.zero;
        hpRT.offsetMax = Vector2.zero;
        var hpTMP = hpGO.AddComponent<TextMeshProUGUI>();
        hpTMP.fontSize = 11;
        hpTMP.alignment = TextAlignmentOptions.Center;
        hpTMP.color = Color.white;
        hpTMP.textWrappingMode = TextWrappingModes.NoWrap;

        // Bar background
        var barBgGO = MakeRect("BarBg", row.transform);
        var barBgRT = barBgGO.GetComponent<RectTransform>();
        barBgRT.anchorMin = new Vector2(0.58f, 0.2f);
        barBgRT.anchorMax = new Vector2(1f, 0.8f);
        barBgRT.offsetMin = Vector2.zero;
        barBgRT.offsetMax = Vector2.zero;
        MakeImage(barBgGO, new Color(0.08f, 0.08f, 0.10f, 1f));

        // Bar fill
        var barFillGO = MakeRect("Fill", barBgGO.transform);
        Stretch(barFillGO.GetComponent<RectTransform>(), 0, 0, 0, 0);
        barFillGO.GetComponent<RectTransform>().pivot = new Vector2(0f, 0.5f);
        var barImg = barFillGO.AddComponent<Image>();
        barImg.type = Image.Type.Filled;
        barImg.fillMethod = Image.FillMethod.Horizontal;
        barImg.fillOrigin = 0;
        barImg.color = ColSubBar;

        return new StatusSubPartWidget
        {
            subPartID = id,
            isOrgan = isOrgan,
            hpLabel = hpTMP,
            hpBar = barImg
        };
    }

    private void ToggleStatusExpand(StatusBodyPartWidget widget)
    {
        widget.expanded = !widget.expanded;
        widget.subPartPanel.SetActive(widget.expanded);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_statusContent.GetComponent<RectTransform>());
    }

    // Called every Update() while status tab is active
    private void RefreshStatusWidgets()
    {
        if (healthManager == null) return;
        var body = healthManager.GetFullBody();

        foreach (var widget in _statusWidgets)
        {
            if (!body.TryGetValue(widget.bodyPart, out var runtimePart)) continue;

            float ratio = runtimePart.ConditionRatio;

            // Header label + bar
            widget.headerLabel.text = $"{runtimePart.displayName}   {ratio * 100f:F0}%";
            widget.headerLabel.color = ConditionColor(ratio);
            widget.conditionBar.fillAmount = ratio;
            widget.conditionBar.color = ConditionColor(ratio);

            // Sub-part rows — only update if expanded (saves work when collapsed)
            if (!widget.expanded) continue;

            // Build a fast lookup for sub-parts by ID
            var allParts = new Dictionary<string, RuntimeSubPart>();
            foreach (var sp in runtimePart.layers) allParts[sp.subPartID] = sp;
            foreach (var organ in runtimePart.organs) allParts[organ.subPartID] = organ;

            foreach (var sw in widget.subWidgets)
            {
                if (!allParts.TryGetValue(sw.subPartID, out var sp)) continue;
                float spRatio = sp.maxHealth > 0f ? sp.currentHealth / sp.maxHealth : 0f;
                sw.hpLabel.text = $"{sp.currentHealth:F0}/{sp.maxHealth:F0}";
                sw.hpBar.fillAmount = spRatio;
                sw.hpBar.color = sp.IsDestroyed ? ColDestroyed : ColSubBar;
            }
        }
    }

    // ── Augment list ──────────────────────────────────────────────────────────

    private void PopulateAugmentList(BodyPart part)
    {
        foreach (var row in _augRows)
            if (row != null) Destroy(row);
        _augRows.Clear();

        var augments = augmentCatalogue != null
            ? augmentCatalogue.GetAugmentsForBodyPart(part)
            : new List<AugmentEntry>();

        if (augments.Count == 0)
        {
            _augRows.Add(MakeEmptyRow("No augments available for this body part."));
            return;
        }

        foreach (var entry in augments)
            _augRows.Add(MakeAugmentRow(entry));

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_listContent.GetComponent<RectTransform>());
    }

    private GameObject MakeAugmentRow(AugmentEntry entry)
    {
        var row = MakeRect($"Row_{entry.augmentID}", _listContent);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 84f;
        le.flexibleWidth = 1f;
        MakeImage(row, ColRowNormal);

        var btn = row.AddComponent<Button>();
        StyleBtn(btn, ColRowNormal, ColRowHover);
        var captured = entry;
        btn.onClick.AddListener(() => RequestInstall(captured));

        // Left accent bar
        var accent = MakeRect("Accent", row.transform);
        var accentRT = accent.GetComponent<RectTransform>();
        accentRT.anchorMin = new Vector2(0, 0);
        accentRT.anchorMax = new Vector2(0, 1);
        accentRT.offsetMin = new Vector2(0, 0);
        accentRT.offsetMax = new Vector2(4, 0);
        MakeImage(accent, ColTabActive);

        // Text block
        var textBlock = MakeRect("TextBlock", row.transform);
        var tbRT = textBlock.GetComponent<RectTransform>();
        tbRT.anchorMin = new Vector2(0, 0);
        tbRT.anchorMax = new Vector2(1, 1);
        tbRT.offsetMin = new Vector2(14, 8);
        tbRT.offsetMax = new Vector2(-8, -8);
        var vlg = textBlock.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        AddRowLabel(textBlock.transform, entry.displayName, 14, Color.white, 20f, true);
        string tag = entry.isSubPartAugment
            ? $"Sub-Part  ·  {entry.targetSubPartCategory}  ·  {entry.category}"
            : $"Full Replacement  ·  {entry.category}";
        AddRowLabel(textBlock.transform, tag, 11, new Color(0.5f, 0.72f, 1f), 16f, false);
        if (!string.IsNullOrEmpty(entry.description))
            AddRowLabel(textBlock.transform, entry.description, 11, new Color(0.6f, 0.78f, 0.6f), 16f, false);

        return row;
    }

    private GameObject MakeEmptyRow(string msg)
    {
        var row = MakeRect("Row_Empty", _listContent);
        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 50f;
        le.flexibleWidth = 1f;
        var tmp = row.AddComponent<TextMeshProUGUI>();
        tmp.text = msg;
        tmp.fontSize = 13;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.5f, 0.5f, 0.5f);
        tmp.margin = new Vector4(12, 0, 12, 0);
        return row;
    }

    private void AddRowLabel(Transform parent, string text, int size,
                              Color color, float height, bool bold)
    {
        var go = MakeRect("Label", parent);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    // ── Confirm ───────────────────────────────────────────────────────────────

    private void RequestInstall(AugmentEntry entry)
    {
        _pending = entry;
        string scope = entry.isSubPartAugment
            ? $"{entry.targetSubPartCategory} on {entry.targetBodyPart}"
            : $"entire {entry.targetBodyPart}";
        _confirmText.text =
            $"Install <b>{entry.displayName}</b>?\n<size=12><color=#aaaaaa>Replaces: {scope}</color></size>";
        _confirmOverlay.SetActive(true);
    }

    private void ConfirmInstall()
    {
        if (_pending == null) return;
        if (_pending.isSubPartAugment)
            healthManager.InstallSubPartAugment(_pending.augmentID);
        else
            healthManager.InstallFullAugment(_pending.augmentID);
        _pending = null;
        _confirmOverlay.SetActive(false);
        PopulateAugmentList(_selectedPart);

        // Rebuild status widgets so new sub-part names and IDs are reflected
        foreach (var w in _statusWidgets)
            if (w.subPartPanel != null)
                Destroy(w.subPartPanel.transform.parent.gameObject);
        _statusWidgets.Clear();
    }

    private void CancelConfirm()
    {
        _pending = null;
        _confirmOverlay.SetActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Color ConditionColor(float ratio)
    {
        if (ratio <= 0f) return ColDestroyed;
        if (ratio < 0.3f) return ColCritical;
        if (ratio < 0.65f) return ColDamaged;
        return ColHealthy;
    }

    private static GameObject MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static void Stretch(RectTransform rt, float l, float r, float b, float t)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
    }

    private static void PinTop(RectTransform rt, float fromTop, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0, -(fromTop + height));
        rt.offsetMax = new Vector2(0, -fromTop);
    }

    private static void PinBottom(RectTransform rt,
                                   float fromBottom, float height,
                                   float leftPad, float rightPad)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(leftPad, fromBottom);
        rt.offsetMax = new Vector2(-rightPad, fromBottom + height);
    }

    private static void MakeSeparator(Transform parent, float top, float bottom)
    {
        var sep = MakeRect("Sep", parent);
        var sepRT = sep.GetComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0, 1);
        sepRT.anchorMax = new Vector2(1, 1);
        sepRT.pivot = new Vector2(0.5f, 1f);
        sepRT.offsetMin = new Vector2(0, bottom);
        sepRT.offsetMax = new Vector2(0, top);
        MakeImage(sep, ColSeparator);
    }

    private static Image MakeImage(GameObject go, Color color)
    {
        var img = go.GetComponent<Image>() ?? go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static void StyleBtn(Button btn, Color normal, Color hover = default)
    {
        if (hover == default) hover = normal * 1.25f;
        var cb = btn.colors;
        cb.normalColor = normal;
        cb.highlightedColor = hover;
        cb.pressedColor = normal * 0.7f;
        cb.selectedColor = normal;
        btn.colors = cb;
        (btn.GetComponent<Image>() ?? btn.gameObject.AddComponent<Image>()).color = normal;
    }

    private static void MakeBtnLabel(Transform parent, string text, int size)
    {
        var go = MakeRect("Label", parent);
        Stretch(go.GetComponent<RectTransform>(), 0, 0, 0, 0);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    private static string SplitCamel(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "([A-Z])", " $1").Trim();
}
