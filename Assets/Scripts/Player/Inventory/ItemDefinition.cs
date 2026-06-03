// ItemDefinition.cs
// Single ScriptableObject for every item in the game.
// Set itemType in the Inspector — the custom editor (ItemDefinitionEditor.cs)
// will show only the fields relevant to that type.
// Create via: Assets > Create > Inventory > ItemDefinition

using UnityEngine;
using System.Collections.Generic;

public enum ItemType
{
    Misc,
    Weapon,
    Armour,
    Consumable,
    Quest
}

public enum WeaponType
{
    Melee,
    Ranged
}

public enum Handedness
{
    OneHanded,
    TwoHanded
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/ItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    // ── Shared (always shown) ─────────────────────────────────────────────────

    public string itemID;
    public string displayName;
    [TextArea]
    public string description;
    public ItemType itemType;
    public float weight = 1f;
    public Color slotColor = new Color(0.3f, 0.6f, 1f, 1f);

    [Tooltip("Prefab spawned in the world when this item is dropped or exists as a pickup. " +
             "Attach a PickupItem component to it.")]
    public GameObject worldPrefab;

    // ── Weapon fields (shown when itemType == Weapon) ─────────────────────────

    public WeaponType weaponType;
    public Handedness handedness;

    // ── Armour fields (shown when itemType == Armour) ─────────────────────────

    public BodyPart targetBodyPart;

    [Tooltip("Stats for this armour layer in the damage cascade.")]
    public SubPartDefinition layerStats;

    // ── Convenience helpers ───────────────────────────────────────────────────

    public bool IsArmour => itemType == ItemType.Armour;
    public bool IsWeapon => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    public bool IsTwoHanded => itemType == ItemType.Weapon && handedness == Handedness.TwoHanded;
}
