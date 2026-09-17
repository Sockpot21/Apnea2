using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Bottom-right readout for every uniquely equipped ranged weapon.</summary>
public class AmmoHUD : MonoBehaviour
{
    private PlayerEquipment _equipment;
    private TextMeshProUGUI _label;

    public static AmmoHUD GetOrCreate(PlayerEquipment equipment, PlayerInventory inventory)
    {
        AmmoHUD hud = FindAnyObjectByType<AmmoHUD>();
        if (hud == null)
        {
            GameObject root = new("AmmoHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;
            hud = root.AddComponent<AmmoHUD>();
            hud.Build();
        }
        else if (hud._label == null)
            hud.Build();
        hud._equipment = equipment;
        return hud;
    }

    private void Build()
    {
        GameObject textObject = new("AmmoReadout", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(transform, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(420f, 90f);
        _label = textObject.GetComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.BottomRight;
        _label.fontSize = 22f;
        _label.fontStyle = FontStyles.Bold;
        _label.color = Color.white;
        _label.enableVertexGradient = true;
        _label.colorGradient = new VertexGradient(Color.white, Color.white,
            new Color(.65f, .72f, .8f), new Color(.65f, .72f, .8f));
        _label.raycastTarget = false;
    }

    private void Update()
    {
        if (_equipment == null || _label == null) return;
        ItemInstance left = _equipment.GetHandSlot(HandSlot.Left);
        ItemInstance right = _equipment.GetHandSlot(HandSlot.Right);
        var lines = new List<string>();
        if (left?.definition != null && left.definition.IsRanged)
            lines.Add(Format(left, ReferenceEquals(left, right) ? "" : "L · "));
        if (right?.definition != null && right.definition.IsRanged && !ReferenceEquals(left, right))
            lines.Add(Format(right, "R · "));
        _label.text = string.Join("\n", lines);
        _label.gameObject.SetActive(lines.Count > 0);
    }

    private string Format(ItemInstance weapon, string handPrefix)
    {
        weapon.InitializeMagazineIfNeeded();
        int reserve = _equipment.GetAmmoReserve(weapon);
        string state = weapon.isReloading
            ? $"  RELOADING {Mathf.Max(0f, weapon.reloadEndsAt - Time.time):F1}s"
            : weapon.currentClipAmmo <= 0 ? "  EMPTY" : string.Empty;
        return $"{handPrefix}{weapon.definition.displayName}  {weapon.currentClipAmmo} / {reserve}{state}";
    }
}
