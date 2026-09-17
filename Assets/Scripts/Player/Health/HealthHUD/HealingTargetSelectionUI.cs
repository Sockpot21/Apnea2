using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runtime radial selector for targeted healing consumables.</summary>
public class HealingTargetSelectionUI : MonoBehaviour
{
    private GameObject root;
    private HealthManager health;
    private PlayerEquipment equipment;
    private HandSlot hand;
    private ItemInstance item;

    public static HealingTargetSelectionUI GetOrCreate()
    {
        var existing = FindAnyObjectByType<HealingTargetSelectionUI>();
        if (existing != null) return existing;
        var canvas = FindAnyObjectByType<Canvas>();
        var go = new GameObject("HealingTargetSelectionUI", typeof(RectTransform), typeof(HealingTargetSelectionUI));
        go.transform.SetParent(canvas != null ? canvas.transform : null, false);
        return go.GetComponent<HealingTargetSelectionUI>();
    }

    public void Open(HealthManager manager, PlayerEquipment sourceEquipment, HandSlot sourceHand, ItemInstance sourceItem)
    {
        health = manager; equipment = sourceEquipment; hand = sourceHand; item = sourceItem;
        if (root != null) Destroy(root);
        root = new GameObject("HealingTargetRoot", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(transform, false);
        var rt = root.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0, 0, 0, .6f);

        var title = MakeText(root.transform, "SELECT TREATMENT TARGET", 18);
        var titleRt = title.GetComponent<RectTransform>(); titleRt.anchorMin = new(.5f, .5f); titleRt.anchorMax = new(.5f, .5f); titleRt.anchoredPosition = new(0, 190); titleRt.sizeDelta = new(440, 40);
        var valid = new HashSet<BodyPart>(health.GetApplicableHealingTargets(item.definition));
        var parts = (BodyPart[])System.Enum.GetValues(typeof(BodyPart));
        for (int i = 0; i < parts.Length; i++)
        {
            float angle = i * Mathf.PI * 2f / parts.Length + Mathf.PI / 2f;
            var buttonGo = new GameObject(parts[i].ToString(), typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(root.transform, false);
            var brt = buttonGo.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new(.5f, .5f); brt.sizeDelta = new(110, 42); brt.anchoredPosition = new(Mathf.Cos(angle) * 145f, Mathf.Sin(angle) * 145f);
            bool applicable = valid.Contains(parts[i]);
            buttonGo.GetComponent<Image>().color = applicable ? new Color(.12f, .55f, .22f) : new Color(.2f, .2f, .2f);
            var button = buttonGo.GetComponent<Button>(); button.interactable = applicable;
            var captured = parts[i]; button.onClick.AddListener(() => Select(captured));
            MakeText(buttonGo.transform, Split(parts[i].ToString()), 11);
        }
        var cancel = new GameObject("Cancel", typeof(RectTransform), typeof(Image), typeof(Button)); cancel.transform.SetParent(root.transform, false);
        var crt = cancel.GetComponent<RectTransform>(); crt.anchorMin = crt.anchorMax = new(.5f, .5f); crt.sizeDelta = new(100, 38); crt.anchoredPosition = new(0, -230);
        cancel.GetComponent<Image>().color = new Color(.5f, .1f, .1f); cancel.GetComponent<Button>().onClick.AddListener(Close); MakeText(cancel.transform, "CANCEL", 12);
    }

    private void Select(BodyPart part) { if (equipment.ApplyConsumableToPart(hand, part)) Close(); }
    private void Close() { if (root != null) Destroy(root); root = null; }
    private static GameObject MakeText(Transform parent, string text, int size)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var label = go.GetComponent<TextMeshProUGUI>(); label.text = text; label.fontSize = size; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        return go;
    }
    private static string Split(string value) => System.Text.RegularExpressions.Regex.Replace(value, "([A-Z])", " $1").Trim();
}
