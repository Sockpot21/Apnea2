// HealthHUD.cs
// Collapsible body part condition readout.
// Attach to a child GameObject of your Canvas.
//
// ── Scene setup you need to do ───────────────────────────────────────────────
// 1. In your Canvas create an empty GameObject, name it "HealthHUD".
//    Attach this script to it.
// 2. Inside HealthHUD create a child: Panel → name it "HUDPanel".
//    Anchor it where you want (e.g. bottom-left). Give it a VerticalLayoutGroup
//    with spacing 4, Child Force Expand Height OFF.
// 3. Assign the HUDPanel's RectTransform to the "Hud Panel" field in the Inspector.
// 4. Assign your PlayerHealth component to the "Player Health" field.
// 5. Leave "Body Part Entry Prefab" empty for now — this script creates entries
//    in code. The prefab field is optional if you want to override visuals later.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private RectTransform hudPanel;

    [Header("Style")]
    [SerializeField] private Color healthyColor = new Color(0.2f, 0.9f, 0.3f);
    [SerializeField] private Color damagedColor = new Color(0.9f, 0.8f, 0.1f);
    [SerializeField] private Color criticalColor = new Color(0.9f, 0.2f, 0.1f);
    [SerializeField] private Color destroyedColor = new Color(0.4f, 0.4f, 0.4f);
    [SerializeField] private Color subPartBarColor = new Color(0.3f, 0.6f, 1.0f);
    [SerializeField] private int headerFontSize = 14;
    [SerializeField] private int subPartFontSize = 11;

    // Runtime entry tracking
    private class BodyPartEntry
    {
        public BodyPart bodyPart;
        public GameObject root;
        public Button headerButton;
        public TextMeshProUGUI headerLabel;
        public Image conditionBar;
        public GameObject subPartPanel;
        public bool isExpanded = false;
        public List<SubPartWidget> subPartWidgets = new();
    }

    private class SubPartWidget
    {
        public TextMeshProUGUI nameLabel;
        public TextMeshProUGUI hpLabel;
        public Image hpBar;
    }

    private readonly List<BodyPartEntry> _entries = new();
    private bool _initialised = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (playerHealth == null) return;
        playerHealth.OnBodyStateUpdated += HandleBodyStateUpdated;
        playerHealth.OnBodyPartConditionChanged += HandlePartConditionChanged;
    }

    private void OnDisable()
    {
        if (playerHealth == null) return;
        playerHealth.OnBodyStateUpdated -= HandleBodyStateUpdated;
        playerHealth.OnBodyPartConditionChanged -= HandlePartConditionChanged;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleBodyStateUpdated(Dictionary<BodyPart, BodyPartCondition> body)
    {
        // Full rebuild on init or augment swap
        ClearEntries();
        foreach (var kvp in body)
            CreateEntry(kvp.Value);
        _initialised = true;
    }

    private void HandlePartConditionChanged(BodyPartCondition condition)
    {
        if (!_initialised) return;
        foreach (var entry in _entries)
        {
            if (entry.bodyPart == condition.bodyPart)
            {
                RefreshEntry(entry, condition);
                return;
            }
        }
    }

    // ── Entry creation ────────────────────────────────────────────────────────

    private void CreateEntry(BodyPartCondition condition)
    {
        var entry = new BodyPartEntry { bodyPart = condition.bodyPart };

        // ── Root container ────────────────────────────────────────────────────
        entry.root = new GameObject($"Entry_{condition.bodyPart}");
        entry.root.transform.SetParent(hudPanel, false);

        var rootLayout = entry.root.AddComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 2f;
        rootLayout.childForceExpandHeight = false;
        rootLayout.childForceExpandWidth = true;

        var rootFitter = entry.root.AddComponent<ContentSizeFitter>();
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Header row ────────────────────────────────────────────────────────
        var headerRow = MakeRow($"Header_{condition.bodyPart}", entry.root.transform, 28f);

        entry.headerButton = headerRow.AddComponent<Button>();
        entry.headerButton.onClick.AddListener(() => ToggleExpand(entry));

        // Part name + condition %
        entry.headerLabel = MakeLabel(headerRow.transform, "", headerFontSize, TextAlignmentOptions.MidlineLeft);

        // Condition bar (sits to the right of label)
        var barBg = MakeBarBackground(headerRow.transform, 80f, 14f);
        entry.conditionBar = MakeBarFill(barBg.transform);

        // ── Sub-part panel (hidden by default) ───────────────────────────────
        entry.subPartPanel = new GameObject($"SubParts_{condition.bodyPart}");
        entry.subPartPanel.transform.SetParent(entry.root.transform, false);

        var subLayout = entry.subPartPanel.AddComponent<VerticalLayoutGroup>();
        subLayout.spacing = 1f;
        subLayout.childForceExpandHeight = false;
        subLayout.childForceExpandWidth = true;
        subLayout.padding = new RectOffset(12, 0, 0, 0);

        var subFitter = entry.subPartPanel.AddComponent<ContentSizeFitter>();
        subFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        entry.subPartPanel.SetActive(false);

        // ── Sub-part widgets ──────────────────────────────────────────────────
        foreach (var sp in condition.subPartConditions)
        {
            var row = MakeRow($"SP_{sp.displayName}", entry.subPartPanel.transform, 22f);

            var widget = new SubPartWidget();

            // Name (organ tagged differently)
            string label = sp.isOrgan ? $"[Organ] {sp.displayName}" : sp.displayName;
            widget.nameLabel = MakeLabel(row.transform, label, subPartFontSize,
                                         TextAlignmentOptions.MidlineLeft);

            // HP text
            widget.hpLabel = MakeLabel(row.transform, "", subPartFontSize,
                                        TextAlignmentOptions.MidlineRight);
            ((RectTransform)widget.hpLabel.transform).sizeDelta = new Vector2(80f, 20f);

            // HP bar
            var subBarBg = MakeBarBackground(row.transform, 70f, 10f);
            widget.hpBar = MakeBarFill(subBarBg.transform);
            widget.hpBar.color = subPartBarColor;

            entry.subPartWidgets.Add(widget);
        }

        _entries.Add(entry);
        RefreshEntry(entry, condition);
    }

    private void ToggleExpand(BodyPartEntry entry)
    {
        entry.isExpanded = !entry.isExpanded;
        entry.subPartPanel.SetActive(entry.isExpanded);
    }

    // ── Refresh (called on every damage event) ────────────────────────────────

    private void RefreshEntry(BodyPartEntry entry, BodyPartCondition condition)
    {
        float ratio = condition.conditionRatio;

        // Header label
        entry.headerLabel.text = $"{condition.displayName}  {ratio * 100f:F0}%";
        entry.headerLabel.color = ConditionColor(ratio);

        // Condition bar
        entry.conditionBar.fillAmount = ratio;
        entry.conditionBar.color = ConditionColor(ratio);

        // Sub-parts
        int widgetCount = Mathf.Min(entry.subPartWidgets.Count, condition.subPartConditions.Count);
        for (int i = 0; i < widgetCount; i++)
        {
            var widget = entry.subPartWidgets[i];
            var sp = condition.subPartConditions[i];
            float spRatio = sp.HealthRatio;

            widget.nameLabel.text = sp.isOrgan ? $"[Organ] {sp.displayName}" : sp.displayName;
            widget.hpLabel.text = $"{sp.currentHealth:F0} / {sp.maxHealth:F0}";
            widget.hpBar.fillAmount = spRatio;
            widget.hpBar.color = sp.IsDestroyed ? destroyedColor : subPartBarColor;
        }
    }

    private void ClearEntries()
    {
        foreach (var entry in _entries)
            if (entry.root != null) Destroy(entry.root);
        _entries.Clear();
    }

    // ── Color helper ──────────────────────────────────────────────────────────

    private Color ConditionColor(float ratio)
    {
        if (ratio <= 0f) return destroyedColor;
        if (ratio < 0.3f) return criticalColor;
        if (ratio < 0.65f) return damagedColor;
        return healthyColor;
    }

    // ── UI factory helpers ────────────────────────────────────────────────────

    private static GameObject MakeRow(string name, Transform parent, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.spacing = 6f;

        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0f, height);

        // Needs an Image for the Button component to receive clicks
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);

        return go;
    }

    private static TextMeshProUGUI MakeLabel(Transform parent, string text,
                                              int fontSize, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(130f, 20f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        return tmp;
    }

    private static GameObject MakeBarBackground(Transform parent, float width, float height)
    {
        var go = new GameObject("BarBg");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.flexibleWidth = 0f;

        return go;
    }

    private static Image MakeBarFill(Transform parent)
    {
        var go = new GameObject("Fill");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.pivot = new Vector2(0f, 0.5f);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = 0;
        img.fillAmount = 1f;
        img.color = new Color(0.2f, 0.9f, 0.3f);

        return img;
    }
}
