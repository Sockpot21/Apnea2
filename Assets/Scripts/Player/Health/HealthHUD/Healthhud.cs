// HealthHUD.cs
// One row per body part: [Name %] [LayerName] [====bar====]
// Opaque panel background. Bar shrinks left-to-right as health depletes.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private RectTransform hudPanel;

    [Header("Bar Style")]
    [SerializeField] private float barWidth = 100f;
    [SerializeField] private float barHeight = 12f;
    [SerializeField] private float entrySpacing = 3f;
    [SerializeField] private float labelWidth = 100f;  // "Head 100%"
    [SerializeField] private float layerWidth = 50f;   // "Skin"

    [Header("Colors")]
    [SerializeField] private Color panelBgColor = new Color(0.05f, 0.05f, 0.07f, 0.92f);
    [SerializeField] private Color healthyColor = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color damagedColor = new Color(0.9f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color criticalColor = new Color(0.9f, 0.2f, 0.1f, 1f);
    [SerializeField] private Color destroyedColor = new Color(0.4f, 0.4f, 0.4f, 1f);
    [SerializeField] private Color barFillColor = new Color(0.3f, 0.6f, 1.0f, 1f);
    [SerializeField] private Color barBgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f);
    [SerializeField] private int labelFontSize = 12;
    [SerializeField] private int layerFontSize = 10;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private class Entry
    {
        public BodyPart bodyPart;
        public GameObject root;
        public TextMeshProUGUI partLabel;
        public TextMeshProUGUI layerLabel;
        public Image barFill;
        public Image barBg;
        public RectTransform fillRT;
    }

    private readonly List<Entry> _entries = new();
    private bool _initialised = false;
    private bool _panelReady = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
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

    // ── Events ────────────────────────────────────────────────────────────────

    private void OnBodyStateUpdated(Dictionary<BodyPart, BodyPartCondition> body)
    {
        ClearEntries();
        SetupPanel();

        foreach (var kvp in body)
            BuildEntry(kvp.Value);

        _initialised = true;
    }

    private void OnPartChanged(BodyPartCondition condition)
    {
        if (!_initialised) return;
        foreach (var e in _entries)
            if (e.bodyPart == condition.bodyPart) { Refresh(e, condition); return; }
    }

    // ── Panel setup (run once) ────────────────────────────────────────────────

    private void SetupPanel()
    {
        if (_panelReady) return;
        _panelReady = true;

        // Opaque background
        var bg = hudPanel.GetComponent<Image>();
        if (bg == null) bg = hudPanel.gameObject.AddComponent<Image>();
        bg.color = panelBgColor;

        // Vertical stack of rows
        var vlg = hudPanel.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = hudPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = entrySpacing;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = false;
        vlg.childControlHeight = false;
        vlg.childControlWidth = false;
        vlg.padding = new RectOffset(8, 8, 6, 6);

        // Shrink panel to fit content
        var csf = hudPanel.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = hudPanel.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    // ── Build one row ─────────────────────────────────────────────────────────

    private void BuildEntry(BodyPartCondition condition)
    {
        var entry = new Entry { bodyPart = condition.bodyPart };

        float rowWidth = labelWidth + layerWidth + barWidth + 12f; // 12 = two 6px gaps

        // Root row
        entry.root = new GameObject($"HUD_{condition.bodyPart}");
        entry.root.transform.SetParent(hudPanel, false);

        var rootRT = entry.root.AddComponent<RectTransform>();
        rootRT.sizeDelta = new Vector2(rowWidth, barHeight + 4f);

        var hlg = entry.root.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childControlHeight = false;
        hlg.childControlWidth = false;
        hlg.spacing = 6f;

        // ── Part label ────────────────────────────────────────────────────────
        var pGO = new GameObject("PartLabel");
        pGO.transform.SetParent(entry.root.transform, false);
        var pRT = pGO.AddComponent<RectTransform>();
        pRT.sizeDelta = new Vector2(labelWidth, barHeight + 4f);
        entry.partLabel = pGO.AddComponent<TextMeshProUGUI>();
        entry.partLabel.fontSize = labelFontSize;
        entry.partLabel.alignment = TextAlignmentOptions.MidlineLeft;
        entry.partLabel.color = Color.white;
        entry.partLabel.textWrappingMode = TextWrappingModes.NoWrap;
        entry.partLabel.overflowMode = TextOverflowModes.Ellipsis;

        // ── Layer label ───────────────────────────────────────────────────────
        var lGO = new GameObject("LayerLabel");
        lGO.transform.SetParent(entry.root.transform, false);
        var lRT = lGO.AddComponent<RectTransform>();
        lRT.sizeDelta = new Vector2(layerWidth, barHeight + 4f);
        entry.layerLabel = lGO.AddComponent<TextMeshProUGUI>();
        entry.layerLabel.fontSize = layerFontSize;
        entry.layerLabel.alignment = TextAlignmentOptions.MidlineLeft;
        entry.layerLabel.color = Color.gray;
        entry.layerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        entry.layerLabel.overflowMode = TextOverflowModes.Ellipsis;

        // ── Bar background ────────────────────────────────────────────────────
        var bgGO = new GameObject("BarBg");
        bgGO.transform.SetParent(entry.root.transform, false);
        var bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.sizeDelta = new Vector2(barWidth, barHeight);
        entry.barBg = bgGO.AddComponent<Image>();
        entry.barBg.color = barBgColor;

        // ── Bar fill — anchored left, width set manually ───────────────────
        var fGO = new GameObject("Fill");
        fGO.transform.SetParent(bgGO.transform, false);
        entry.fillRT = fGO.AddComponent<RectTransform>();
        entry.fillRT.anchorMin = new Vector2(0f, 0f);
        entry.fillRT.anchorMax = new Vector2(0f, 1f);
        entry.fillRT.pivot = new Vector2(0f, 0.5f);
        entry.fillRT.offsetMin = Vector2.zero;
        entry.fillRT.offsetMax = new Vector2(barWidth, 0f); // full at start
        entry.barFill = fGO.AddComponent<Image>();
        entry.barFill.color = barFillColor;

        _entries.Add(entry);
        Refresh(entry, condition);
    }

    // ── Refresh one row ───────────────────────────────────────────────────────

    private void Refresh(Entry entry, BodyPartCondition condition)
    {
        float ratio = condition.conditionRatio;
        entry.partLabel.text = $"{condition.displayName} {ratio * 100f:F0}%";
        entry.partLabel.color = ConditionColor(ratio);

        // Outermost non-destroyed sub-part
        SubPartCondition visible = null;
        foreach (var sp in condition.subPartConditions)
            if (!sp.IsDestroyed) { visible = sp; break; }

        if (visible == null && condition.subPartConditions.Count > 0)
            visible = condition.subPartConditions[condition.subPartConditions.Count - 1];

        if (visible == null) return;

        entry.layerLabel.text = visible.isOrgan ? $"[O] {visible.displayName}" : visible.displayName;
        entry.layerLabel.color = visible.IsDestroyed ? destroyedColor : Color.gray;

        // Shrink fill width
        entry.fillRT.offsetMax = new Vector2(visible.HealthRatio * barWidth, 0f);
        entry.barFill.color = visible.IsDestroyed ? destroyedColor : barFillColor;
        entry.barBg.color = visible.IsDestroyed
            ? new Color(0.08f, 0.08f, 0.08f, 0.85f)
            : barBgColor;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearEntries()
    {
        foreach (var e in _entries)
            if (e.root != null) Destroy(e.root);
        _entries.Clear();
    }

    private Color ConditionColor(float ratio)
    {
        if (ratio <= 0f) return destroyedColor;
        if (ratio < 0.3f) return criticalColor;
        if (ratio < 0.65f) return damagedColor;
        return healthyColor;
    }
}
