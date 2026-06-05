// ItemDefinition.cs
// Single ScriptableObject for every item in the game.
// Set itemType in the Inspector — ItemDefinitionEditor shows only relevant fields.
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

    [Tooltip("Icon shown in inventory and equipment slots. " +
             "If null the slot tint color is used as fallback.")]
    public Sprite icon;

    [Tooltip("Prefab spawned in the world when this item is dropped or exists as a pickup. " +
             "Attach a PickupItem component to it.")]
    public GameObject worldPrefab;

    // ── Weapon fields (shown when itemType == Weapon) ─────────────────────────

    public WeaponType weaponType;
    public Handedness handedness;

    // ── Ranged-only fields (shown when weaponType == Ranged) ──────────────────

    [Tooltip("Prefab instantiated when firing. Must have a Bullet component.")]
    public GameObject bulletPrefab;

    [Tooltip("Initial speed of the bullet in units per second.")]
    public float bulletSpeed = 30f;

    [Tooltip("Gravity scale applied to the bullet. 0 = no drop, 1 = full gravity.")]
    public float bulletDrop = 0.1f;

    [Tooltip("FOV when aiming down sights. Only applied when aiming with this weapon.")]
    public float aimFOV = 45f;

    // ── Armour fields (shown when itemType == Armour) ─────────────────────────

    [Tooltip("Which body part this armour protects. " +
             "Set layerStats.category to SubPartCategory.Armour.")]
    public BodyPart targetBodyPart;
    public SubPartDefinition layerStats;

    // ── Convenience helpers ───────────────────────────────────────────────────

    public bool IsArmour => itemType == ItemType.Armour;
    public bool IsWeapon => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    public bool IsRanged => itemType == ItemType.Weapon && weaponType == WeaponType.Ranged;
    public bool IsTwoHanded => itemType == ItemType.Weapon && handedness == Handedness.TwoHanded;
}
