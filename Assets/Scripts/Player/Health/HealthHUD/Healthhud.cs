using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Live limb HUD with structural layers literally overlaid in UI depth.</summary>
public class HealthHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private HealthManager healthManager;
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private RectTransform hudPanel;

    [Header("Bar Style")]
    [SerializeField] private float barWidth = 120f;
    [SerializeField] private float barHeight = 11f;
    [SerializeField] private float entrySpacing = 5f;
    [SerializeField] private float labelWidth = 130f;
    [SerializeField] private float valueWidth = 76f;
    [SerializeField] private int labelFontSize = 12;
    [SerializeField] private int layerFontSize = 10;

    [Header("Colors")]
    [SerializeField] private Color panelBgColor = new(0.05f, 0.05f, 0.07f, 0.92f);
    [SerializeField] private Color healthyColor = new(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color damagedColor = new(0.9f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color criticalColor = new(0.9f, 0.2f, 0.1f, 1f);
    [SerializeField] private Color destroyedColor = new(0.4f, 0.4f, 0.4f, 1f);
    [SerializeField] private Color armourColor = new(0.55f, 0.58f, 0.62f, 1f);
    [SerializeField] private Color skinColor = new(0.88f, 0.24f, 0.22f, 1f);
    [SerializeField] private Color muscleColor = new(0.52f, 0.08f, 0.1f, 1f);
    [SerializeField] private Color boneColor = new(0.92f, 0.88f, 0.72f, 1f);
    [SerializeField] private Color barBgColor = new(0.08f, 0.08f, 0.09f, 0.95f);
    [SerializeField] private Color saturationColor = new(0.95f, 0.62f, 0.18f, 1f);
    [SerializeField] private Color staminaColor = new(0.16f, 0.75f, 0.92f, 1f);
    [SerializeField] private Color effectColor = new(0.55f, 0.88f, 1f, 1f);

    private sealed class ResourceBar
    {
        public Image fill;
        public TextMeshProUGUI label;
    }

    private sealed class LayerBar
    {
        public Image fill;
        public RectTransform fillRect;
        public float width;
    }

    private sealed class Entry
    {
        public BodyPart bodyPart;
        public GameObject root;
        public TextMeshProUGUI partLabel;
        public TextMeshProUGUI layerLabel;
        public TextMeshProUGUI value;
        public readonly List<LayerBar> layers = new();
    }

    private readonly List<Entry> _entries = new();
    private bool _initialised;
    private bool _panelReady;
    private bool _compactInventoryMode;
    private float _compactRowWidth;
    private GameObject _compactEntriesRoot;
    private GridLayoutGroup _compactEntriesGrid;
    private LayoutElement _compactEntriesLayout;
    private TextMeshProUGUI _biofluidLabel;
    private TextMeshProUGUI _conditionLabel;
    private TextMeshProUGUI _effectLabel;
    private ResourceBar _saturationBar;
    private ResourceBar _staminaBar;
    private float RowWidth => _compactInventoryMode && _compactRowWidth > 0f
        ? _compactRowWidth
        : labelWidth + barWidth + valueWidth + 12f;

    public void SetCompactInventoryMode(bool compact, float availableWidth = 0f)
    {
        float nextWidth = compact ? Mathf.Max(0f, availableWidth) : 0f;
        if (_compactInventoryMode == compact && Mathf.Approximately(_compactRowWidth, nextWidth)) return;
        _compactInventoryMode = compact;
        _compactRowWidth = nextWidth;
        if (_panelReady)
        {
            VerticalLayoutGroup layout = hudPanel.GetComponent<VerticalLayoutGroup>();
            if (layout != null) layout.spacing = compact ? 2f : entrySpacing;
            ResizeHudWidth(RowWidth);
        }
        if (_initialised) RebuildAll();
    }

    private void ResizeHudWidth(float width)
    {
        if (hudPanel == null) return;
        foreach (Transform child in hudPanel)
        {
            if (child is not RectTransform rect) continue;
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            LayoutElement layout = child.GetComponent<LayoutElement>();
            if (layout != null) layout.preferredWidth = width;
        }
    }

    private void OnEnable()
    {
        if (healthManager == null)
            healthManager = GetComponent<HealthManager>() ?? GetComponentInParent<HealthManager>();
        if (healthManager == null && playerHealth != null)
            healthManager = playerHealth.GetComponent<HealthManager>();
        if (playerCharacter == null && healthManager != null)
            playerCharacter = healthManager.GetComponent<PlayerCharacter>()
                ?? healthManager.GetComponentInParent<PlayerCharacter>()
                ?? FindAnyObjectByType<PlayerCharacter>();
        if (playerHealth == null) return;
        playerHealth.OnBodyStateUpdated += OnBodyStateUpdated;
        playerHealth.OnBodyPartConditionChanged += OnPartChanged;
    }

    private void OnDisable()
    {
        if (playerHealth == null) return;
        playerHealth.OnBodyStateUpdated -= OnBodyStateUpdated;
        playerHealth.OnBodyPartConditionChanged -= OnPartChanged;
    }

    private void Start()
    {
        if (!_initialised && healthManager != null && playerHealth != null) RebuildAll();
    }

    private void Update()
    {
        if (healthManager == null || playerHealth == null) return;
        if (_biofluidLabel != null)
            _biofluidLabel.text = $"BIOFLUID  {healthManager.CurrentBiofluid:F0} / {healthManager.TotalBiofluidRequired:F0}";
        if (playerCharacter != null)
        {
            RefreshResourceBar(_saturationBar, "SATURATION", playerCharacter.CurrentSaturation,
                playerCharacter.MaxSaturation);
            RefreshResourceBar(_staminaBar, "STAMINA", playerCharacter.CurrentStamina,
                playerCharacter.MaxStamina);
        }

        foreach (Entry entry in _entries)
        {
            RuntimeBodyPart runtimePart = healthManager.GetBodyPart(entry.bodyPart);
            if (runtimePart == null) continue;
            BodyPartCondition snapshot = playerHealth.BuildLiveSnapshot(runtimePart);
            if (StructuralCount(snapshot) != entry.layers.Count)
            {
                RebuildAll();
                return;
            }
            Refresh(entry, snapshot);
        }
        RefreshConditionReadout();
        RefreshEffectReadout();
    }

    private void OnBodyStateUpdated(Dictionary<BodyPart, BodyPartCondition> body) => RebuildAll(body);

    private void OnPartChanged(BodyPartCondition condition)
    {
        if (!_initialised) return;
        foreach (Entry entry in _entries)
            if (entry.bodyPart == condition.bodyPart)
            {
                if (entry.layers.Count != StructuralCount(condition)) RebuildAll();
                else Refresh(entry, condition);
                return;
            }
    }

    private void RebuildAll(Dictionary<BodyPart, BodyPartCondition> snapshots = null)
    {
        SetupPanel();
        ClearEntries();
        PrepareEntryContainer();
        if (snapshots != null)
            foreach (var pair in snapshots) BuildEntry(pair.Value);
        else if (healthManager != null && playerHealth != null)
            foreach (var pair in healthManager.GetFullBody())
                BuildEntry(playerHealth.BuildLiveSnapshot(pair.Value));
        UpdateCompactEntryContainer();
        BuildConditionReadout();
        _initialised = true;
    }

    private void PrepareEntryContainer()
    {
        if (!_compactInventoryMode)
        {
            if (_compactEntriesRoot != null) _compactEntriesRoot.SetActive(false);
            return;
        }

        if (_compactEntriesRoot == null)
        {
            _compactEntriesRoot = new GameObject("CompactBodyGrid", typeof(RectTransform),
                typeof(LayoutElement), typeof(GridLayoutGroup));
            _compactEntriesRoot.transform.SetParent(hudPanel, false);
            _compactEntriesLayout = _compactEntriesRoot.GetComponent<LayoutElement>();
            _compactEntriesGrid = _compactEntriesRoot.GetComponent<GridLayoutGroup>();
            _compactEntriesGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _compactEntriesGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
            _compactEntriesGrid.childAlignment = TextAnchor.UpperLeft;
            _compactEntriesGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _compactEntriesGrid.constraintCount = 2;
        }

        _compactEntriesRoot.SetActive(true);
        float gap = 8f;
        float cellHeight = Mathf.Max(18f, barHeight + 5f);
        float cellWidth = Mathf.Max(180f, (RowWidth - gap) * .5f);
        _compactEntriesGrid.cellSize = new Vector2(cellWidth, cellHeight);
        _compactEntriesGrid.spacing = new Vector2(gap, 2f);
        RectTransform rect = _compactEntriesRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(RowWidth, cellHeight);
        _compactEntriesLayout.preferredWidth = RowWidth;
    }

    private void UpdateCompactEntryContainer()
    {
        if (!_compactInventoryMode || _compactEntriesRoot == null) return;
        int rows = Mathf.CeilToInt(_entries.Count / 2f);
        float height = rows <= 0
            ? 0f
            : rows * _compactEntriesGrid.cellSize.y + (rows - 1) * _compactEntriesGrid.spacing.y;
        _compactEntriesLayout.preferredHeight = height;
        RectTransform rect = _compactEntriesRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(RowWidth, height);
        _conditionLabel?.transform.SetAsLastSibling();
        _effectLabel?.transform.SetAsLastSibling();
    }

    private void SetupPanel()
    {
        if (_panelReady || hudPanel == null) return;
        _panelReady = true;

        // The original scene scaled this hierarchy by 2 × 0.8, which made TMP
        // text and thin bars render between pixels. Keep transforms at 1:1 and
        // make the actual layout larger instead.
        if (transform is RectTransform rootRect)
        {
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(24f, -24f);
            rootRect.localScale = Vector3.one;
        }
        hudPanel.anchorMin = hudPanel.anchorMax = new Vector2(0f, 1f);
        hudPanel.pivot = new Vector2(0f, 1f);
        hudPanel.anchoredPosition = Vector2.zero;
        hudPanel.localScale = Vector3.one;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.pixelPerfect = true;

        barWidth = Mathf.Max(barWidth, 180f);
        barHeight = Mathf.Max(barHeight, 14f);
        labelWidth = Mathf.Max(labelWidth, 150f);
        valueWidth = Mathf.Max(valueWidth, 78f);
        labelFontSize = Mathf.Max(labelFontSize, 14);
        layerFontSize = Mathf.Max(layerFontSize, 11);
        Image bg = hudPanel.GetComponent<Image>() ?? hudPanel.gameObject.AddComponent<Image>();
        bg.color = panelBgColor;
        VerticalLayoutGroup layout = hudPanel.GetComponent<VerticalLayoutGroup>() ?? hudPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = entrySpacing;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.childControlHeight = true;
        layout.childControlWidth = false;
        layout.padding = new RectOffset(8, 8, 6, 6);
        ContentSizeFitter fitter = hudPanel.GetComponent<ContentSizeFitter>() ?? hudPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject title = new("BodyStatusTitle", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        title.transform.SetParent(hudPanel, false);
        title.GetComponent<LayoutElement>().preferredHeight = 26f;
        TextMeshProUGUI titleLabel = title.GetComponent<TextMeshProUGUI>();
        titleLabel.text = "BODY STATUS";
        titleLabel.fontSize = labelFontSize + 3;
        titleLabel.fontStyle = FontStyles.Bold;
        titleLabel.color = Color.white;
        titleLabel.alignment = TextAlignmentOptions.MidlineLeft;

        _saturationBar = BuildResourceBar("Saturation", saturationColor);
        _staminaBar = BuildResourceBar("Stamina", staminaColor);

        GameObject biofluid = new("BiofluidLabel", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        biofluid.transform.SetParent(hudPanel, false);
        biofluid.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, barHeight + 8f);
        biofluid.GetComponent<LayoutElement>().preferredHeight = barHeight + 8f;
        _biofluidLabel = biofluid.GetComponent<TextMeshProUGUI>();
        _biofluidLabel.fontSize = labelFontSize;
        _biofluidLabel.fontStyle = FontStyles.Bold;
        _biofluidLabel.color = skinColor;
        _biofluidLabel.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private void BuildEntry(BodyPartCondition condition)
    {
        Entry entry = new() { bodyPart = condition.bodyPart, root = new GameObject($"HUD_{condition.bodyPart}") };
        Transform entryParent = _compactInventoryMode && _compactEntriesRoot != null
            ? _compactEntriesRoot.transform
            : hudPanel;
        entry.root.transform.SetParent(entryParent, false);
        RectTransform rootRect = entry.root.AddComponent<RectTransform>();

        if (_compactInventoryMode)
        {
            float compactHeight = Mathf.Max(18f, barHeight + 5f);
            float compactEntryWidth = _compactEntriesGrid != null
                ? _compactEntriesGrid.cellSize.x
                : RowWidth;
            float compactLabelWidth = Mathf.Clamp(compactEntryWidth * .31f, 98f, 145f);
            float compactValueWidth = Mathf.Clamp(compactEntryWidth * .19f, 62f, 82f);
            float compactBarWidth = Mathf.Max(76f,
                compactEntryWidth - compactLabelWidth - compactValueWidth - 12f);
            rootRect.sizeDelta = new Vector2(compactEntryWidth, compactHeight);
            entry.root.AddComponent<LayoutElement>().preferredHeight = compactHeight;
            HorizontalLayoutGroup compactRow = entry.root.AddComponent<HorizontalLayoutGroup>();
            compactRow.spacing = 6f;
            compactRow.childAlignment = TextAnchor.MiddleLeft;
            compactRow.childControlHeight = false;
            compactRow.childControlWidth = false;
            compactRow.childForceExpandHeight = false;
            compactRow.childForceExpandWidth = false;

            GameObject part = new("PartName", typeof(RectTransform), typeof(TextMeshProUGUI));
            part.transform.SetParent(entry.root.transform, false);
            part.GetComponent<RectTransform>().sizeDelta = new Vector2(compactLabelWidth, compactHeight);
            entry.partLabel = part.GetComponent<TextMeshProUGUI>();
            entry.partLabel.fontSize = layerFontSize;
            entry.partLabel.fontStyle = FontStyles.Bold;
            entry.partLabel.alignment = TextAlignmentOptions.MidlineLeft;
            entry.partLabel.textWrappingMode = TextWrappingModes.NoWrap;
            entry.partLabel.overflowMode = TextOverflowModes.Ellipsis;

            GameObject compactBackground = new("CompositeBar", typeof(RectTransform), typeof(Image));
            compactBackground.transform.SetParent(entry.root.transform, false);
            compactBackground.GetComponent<RectTransform>().sizeDelta = new Vector2(compactBarWidth, barHeight);
            compactBackground.GetComponent<Image>().color = barBgColor;
            for (int i = condition.subPartConditions.Count - 1; i >= 0; i--)
            {
                SubPartCondition subPart = condition.subPartConditions[i];
                if (subPart.isOrgan) continue;
                entry.layers.Insert(0, BuildLayerBar(compactBackground.transform, compactBarWidth));
            }

            GameObject value = new("PartValue", typeof(RectTransform), typeof(TextMeshProUGUI));
            value.transform.SetParent(entry.root.transform, false);
            value.GetComponent<RectTransform>().sizeDelta = new Vector2(compactValueWidth, compactHeight);
            entry.value = value.GetComponent<TextMeshProUGUI>();
            entry.value.fontSize = layerFontSize;
            entry.value.alignment = TextAlignmentOptions.MidlineRight;
            _entries.Add(entry);
            Refresh(entry, condition);
            _conditionLabel?.transform.SetAsLastSibling();
            _effectLabel?.transform.SetAsLastSibling();
            return;
        }

        float height = labelFontSize + barHeight + 10f;
        rootRect.sizeDelta = new Vector2(RowWidth, height);
        entry.root.AddComponent<LayoutElement>().preferredHeight = height;
        VerticalLayoutGroup vertical = entry.root.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 2f;
        vertical.childControlHeight = false;
        vertical.childControlWidth = false;
        vertical.childForceExpandHeight = false;
        vertical.childForceExpandWidth = false;

        GameObject header = new("PartHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        header.transform.SetParent(entry.root.transform, false);
        header.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, labelFontSize + 5f);
        entry.partLabel = header.GetComponent<TextMeshProUGUI>();
        entry.partLabel.fontSize = labelFontSize;
        entry.partLabel.alignment = TextAlignmentOptions.MidlineLeft;
        entry.partLabel.fontStyle = FontStyles.Bold;

        GameObject row = new("LayerStack", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(entry.root.transform, false);
        row.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, barHeight + 1f);
        HorizontalLayoutGroup horizontal = row.GetComponent<HorizontalLayoutGroup>();
        horizontal.spacing = 6f;
        horizontal.childAlignment = TextAnchor.MiddleLeft;
        horizontal.childControlHeight = false;
        horizontal.childControlWidth = false;
        horizontal.childForceExpandHeight = false;
        horizontal.childForceExpandWidth = false;

        GameObject labelObject = new("VisibleLayer", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(row.transform, false);
        labelObject.GetComponent<RectTransform>().sizeDelta = new Vector2(labelWidth, barHeight + 1f);
        entry.layerLabel = labelObject.GetComponent<TextMeshProUGUI>();
        entry.layerLabel.fontSize = layerFontSize;
        entry.layerLabel.alignment = TextAlignmentOptions.MidlineLeft;
        entry.layerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        entry.layerLabel.overflowMode = TextOverflowModes.Ellipsis;

        GameObject background = new("CompositeBar", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(row.transform, false);
        background.GetComponent<RectTransform>().sizeDelta = new Vector2(barWidth, barHeight);
        background.GetComponent<Image>().color = barBgColor;

        // Inner layers are created first. Outer layers are later siblings and
        // therefore render on top in exactly the same RectTransform space.
        for (int i = condition.subPartConditions.Count - 1; i >= 0; i--)
        {
            SubPartCondition subPart = condition.subPartConditions[i];
            if (subPart.isOrgan) continue;
            entry.layers.Insert(0, BuildLayerBar(background.transform, barWidth));
        }

        GameObject valueObject = new("VisibleValue", typeof(RectTransform), typeof(TextMeshProUGUI));
        valueObject.transform.SetParent(row.transform, false);
        valueObject.GetComponent<RectTransform>().sizeDelta = new Vector2(valueWidth, barHeight + 1f);
        entry.value = valueObject.GetComponent<TextMeshProUGUI>();
        entry.value.fontSize = layerFontSize;
        entry.value.alignment = TextAlignmentOptions.MidlineRight;

        _entries.Add(entry);
        Refresh(entry, condition);
        _conditionLabel?.transform.SetAsLastSibling();
        _effectLabel?.transform.SetAsLastSibling();
    }

    private ResourceBar BuildResourceBar(string name, Color color)
    {
        float height = barHeight + 8f;
        GameObject background = new($"{name}Bar", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        background.transform.SetParent(hudPanel, false);
        background.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, height);
        background.GetComponent<LayoutElement>().preferredHeight = height;
        background.GetComponent<Image>().color = barBgColor;

        GameObject fillObject = new("Fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(background.transform, false);
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fill = fillObject.GetComponent<Image>();
        fill.color = color;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.raycastTarget = false;

        GameObject labelObject = new("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(background.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(7f, 0f);
        labelRect.offsetMax = new Vector2(-7f, 0f);
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = layerFontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        return new ResourceBar { fill = fill, label = label };
    }

    private static void RefreshResourceBar(ResourceBar bar, string title, float current, float maximum)
    {
        if (bar == null) return;
        bar.fill.fillAmount = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
        bar.label.text = $"{title}    {current:F0} / {maximum:F0}";
    }

    private static LayerBar BuildLayerBar(Transform parent, float width)
    {
        LayerBar bar = new() { width = width };
        bar.fill = CreateBarImage("LayerHealth", parent, out bar.fillRect);
        return bar;
    }

    private static Image CreateBarImage(string name, Transform parent, out RectTransform rect)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, .5f);
        rect.offsetMin = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private void Refresh(Entry entry, BodyPartCondition condition)
    {
        string state = condition.isDestroyed ? "DESTROYED" : condition.isDisabled ? "DISABLED" : string.Empty;
        entry.partLabel.text = _compactInventoryMode
            ? condition.displayName + (state.Length > 0 ? $"  {state}" : string.Empty)
            : $"{condition.displayName}  {condition.currentHealth:F0}/{condition.maxHealth:F0}" +
              (condition.maxHealth < condition.baseMaxHealth ? $" (BASE {condition.baseMaxHealth:F0})" : string.Empty) +
              (state.Length > 0 ? $"  {state}" : string.Empty);
        entry.partLabel.color = ConditionColor(condition.conditionRatio);

        var structural = new List<SubPartCondition>();
        foreach (SubPartCondition subPart in condition.subPartConditions)
            if (!subPart.isOrgan) structural.Add(subPart);

        SubPartCondition visible = null;
        for (int i = 0; i < entry.layers.Count; i++)
        {
            LayerBar bar = entry.layers[i];
            SubPartCondition subPart = structural[i];
            Color color = LayerColor(subPart);
            float baseMaximum = Mathf.Max(Mathf.Epsilon, subPart.baseMaxHealth);
            SetBarWidth(bar, Mathf.Clamp01(subPart.currentHealth / baseMaximum));
            bar.fill.color = subPart.IsDestroyed ? destroyedColor : color;
            if (visible == null && !subPart.IsDepleted && !subPart.IsDestroyed) visible = subPart;
        }

        if (visible == null && structural.Count > 0) visible = structural[structural.Count - 1];
        if (_compactInventoryMode)
        {
            entry.value.text = $"{condition.currentHealth:F0}/{condition.maxHealth:F0}";
            entry.value.color = ConditionColor(condition.conditionRatio);
        }
        else if (visible != null)
        {
            Color visibleColor = visible.IsDepleted || visible.IsDestroyed ? destroyedColor : LayerColor(visible);
            entry.layerLabel.text = visible.displayName;
            entry.layerLabel.color = visibleColor;
            entry.value.text = $"{visible.currentHealth:F0}/{visible.maxHealth:F0}";
            entry.value.color = visibleColor;
        }
    }

    private static void SetBarWidth(LayerBar bar, float ratio) =>
        bar.fillRect.offsetMax = new Vector2(bar.width * ratio, 0f);

    private Color LayerColor(SubPartCondition subPart)
    {
        return subPart.category switch
        {
            SubPartCategory.Armour => armourColor,
            SubPartCategory.Skin => skinColor,
            SubPartCategory.Muscle => muscleColor,
            SubPartCategory.Bone => boneColor,
            _ => destroyedColor
        };
    }

    private void BuildConditionReadout()
    {
        if (_conditionLabel != null) return;
        GameObject go = new("ActiveConditions", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        go.transform.SetParent(hudPanel, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, 0f);
        go.GetComponent<LayoutElement>().preferredWidth = RowWidth;
        _conditionLabel = go.GetComponent<TextMeshProUGUI>();
        _conditionLabel.fontSize = layerFontSize;
        _conditionLabel.color = damagedColor;
        _conditionLabel.alignment = TextAlignmentOptions.TopLeft;
        _conditionLabel.textWrappingMode = TextWrappingModes.Normal;
        _conditionLabel.overflowMode = TextOverflowModes.Overflow;

        GameObject effects = new("ActivePlayerEffects", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
        effects.transform.SetParent(hudPanel, false);
        effects.GetComponent<RectTransform>().sizeDelta = new Vector2(RowWidth, 0f);
        effects.GetComponent<LayoutElement>().preferredWidth = RowWidth;
        _effectLabel = effects.GetComponent<TextMeshProUGUI>();
        _effectLabel.fontSize = layerFontSize;
        _effectLabel.color = effectColor;
        _effectLabel.alignment = TextAlignmentOptions.TopLeft;
        _effectLabel.textWrappingMode = TextWrappingModes.Normal;
        _effectLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private void RefreshConditionReadout()
    {
        if (_conditionLabel == null) return;
        List<string> lines = new();
        foreach (Entry entry in _entries)
        {
            RuntimeBodyPart runtimePart = healthManager.GetBodyPart(entry.bodyPart);
            if (runtimePart == null) continue;
            BodyPartCondition condition = playerHealth.BuildLiveSnapshot(runtimePart);
            foreach (SubPartCondition subPart in condition.subPartConditions)
            {
                if (subPart.isOrgan) continue;
                foreach (HealthConditionSnapshot active in subPart.activeConditions)
                {
                    string time = active.IsPermanent ? "UNTIL TREATED" : $"{Mathf.Max(0f, active.remainingSeconds):F1}s";
                    lines.Add($"{condition.displayName} / {subPart.displayName}: {SplitCamel(active.type.ToString())}  {time}");
                }
            }
        }
        _conditionLabel.text = lines.Count == 0 ? string.Empty : $"<b>CONDITIONS</b>\n{string.Join("\n", lines)}";
        int lineCount = lines.Count == 0 ? 0 : lines.Count + 1;
        float height = lineCount * (layerFontSize + 5f);
        _conditionLabel.GetComponent<LayoutElement>().preferredHeight = height;
        _conditionLabel.rectTransform.sizeDelta = new Vector2(RowWidth, height);
        _conditionLabel.transform.SetAsLastSibling();
    }

    private void RefreshEffectReadout()
    {
        if (_effectLabel == null) return;
        List<string> lines = new();
        if (playerCharacter != null)
            foreach (TimedPlayerStatEffect effect in playerCharacter.ActiveTimedEffects)
                lines.Add($"{effect.displayName}  {Mathf.Max(0f, effect.remainingSeconds):F1}s");
        _effectLabel.text = lines.Count == 0 ? string.Empty : $"<b>ACTIVE EFFECTS</b>\n{string.Join("\n", lines)}";
        int lineCount = lines.Count == 0 ? 0 : lines.Count + 1;
        float height = lineCount * (layerFontSize + 5f);
        _effectLabel.GetComponent<LayoutElement>().preferredHeight = height;
        _effectLabel.rectTransform.sizeDelta = new Vector2(RowWidth, height);
        _effectLabel.transform.SetAsLastSibling();
    }

    private void ClearEntries()
    {
        foreach (Entry entry in _entries)
            if (entry.root != null) Destroy(entry.root);
        _entries.Clear();
    }

    private Color ConditionColor(float ratio)
    {
        if (ratio <= 0f) return destroyedColor;
        if (ratio < .3f) return criticalColor;
        if (ratio < .65f) return damagedColor;
        return healthyColor;
    }

    private static int StructuralCount(BodyPartCondition condition)
    {
        int count = 0;
        foreach (SubPartCondition subPart in condition.subPartConditions)
            if (!subPart.isOrgan) count++;
        return count;
    }

    private static string SplitCamel(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "([A-Z])", " $1").Trim();
}
