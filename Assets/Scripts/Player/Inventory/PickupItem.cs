// PickupItem.cs
// Attach to any world GameObject to make it a pickup.
// Prompt label is detached from item transform — immune to Rigidbody rotation.

using UnityEngine;
using TMPro;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data")]
    public ItemInstance item;

    [Header("Prompt Style")]
    [SerializeField] private float promptVerticalOffset = 1.2f;
    [SerializeField] private Color promptTextColor = Color.white;

    // Font size is fixed across all pickups — not per-item
    private const float PromptFontSize = 2f;

    private GameObject _promptRoot;
    private TextMeshPro _promptLabel;
    private bool _promptVisible = false;
    // ── Lifecycle ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        // World-placed pickups only assign `definition` in the Inspector — fill in
        // runtime instance data (durability, stack count) the first time it's touched.
        if (item == null || item.definition == null)
        {
            Debug.LogWarning($"[PickupItem] '{gameObject.name}' has no Item Definition assigned — assign one in the Inspector.", this);
            return;
        }

        if (item.currentDurability <= 0f && item.stackCount <= 0)
        {
            item.currentDurability = item.definition.maxDurability;
            item.stackCount = 1;
        }
    }
    private void Start() => BuildPrompt();

    private void OnDestroy()
    {
        if (_promptRoot != null)
            Destroy(_promptRoot);
    }

    private void LateUpdate()
    {
        if (!_promptVisible || _promptRoot == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        // Track item position independently of its rotation
        _promptRoot.transform.position =
            transform.position + Vector3.up * promptVerticalOffset;

        // Always face camera
        _promptRoot.transform.rotation =
            Quaternion.LookRotation(_promptRoot.transform.position - cam.transform.position);
    }

    // ── Prompt control ────────────────────────────────────────────────────────

    public void ShowPrompt()
    {
        if (_promptRoot == null || _promptVisible) return;
        _promptRoot.SetActive(true);
        _promptVisible = true;
    }

    public void HidePrompt()
    {
        if (_promptRoot == null || !_promptVisible) return;
        _promptRoot.SetActive(false);
        _promptVisible = false;
    }

    // ── Collect ───────────────────────────────────────────────────────────────

    public ItemInstance Collect()
    {
        var collected = item;
        Destroy(gameObject);
        return collected;
    }

    public void RefreshPrompt() => UpdatePromptText();

    // ── Build prompt ──────────────────────────────────────────────────────────

    private void BuildPrompt()
    {
        _promptRoot = new GameObject($"PickupPrompt_{gameObject.name}");
        _promptRoot.transform.position =
            transform.position + Vector3.up * promptVerticalOffset;

        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(_promptRoot.transform, false);
        labelGO.transform.localPosition = Vector3.zero;

        _promptLabel = labelGO.AddComponent<TextMeshPro>();
        _promptLabel.fontSize = PromptFontSize;
        _promptLabel.fontStyle = FontStyles.Bold;
        _promptLabel.color = promptTextColor;
        _promptLabel.alignment = TextAlignmentOptions.Center;
        _promptLabel.textWrappingMode = TextWrappingModes.NoWrap;
        _promptLabel.overflowMode = TextOverflowModes.Ellipsis;

        UpdatePromptText();
        _promptRoot.SetActive(false);
    }

    private void UpdatePromptText()
    {
        if (_promptLabel == null) return;
        _promptLabel.text = $"[E]  Pick up  {(item != null ? item.definition.displayName : "Item")}";
    }
}
