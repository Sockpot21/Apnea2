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
    Quest,
    Augment,
    Bag
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
    public Color slotColor = new Color(0.3f, 0.6f, 1f, 1f);

    [Tooltip("Icon shown in inventory and equipment slots. " +
             "If null the slot tint color is used as fallback.")]
    public Sprite icon;

    [Tooltip("Prefab spawned in the world when this item is dropped or exists as a pickup. " +
             "Attach a PickupItem component to it.")]
    public GameObject worldPrefab;

    [Header("Durability")]
    [Tooltip("Maximum durability as a percentage (0-100).")]
    public float maxDurability = 100f;
    [Tooltip("Durability % lost per use (e.g. per shot fired). 0 = never degrades.")]
    public float degradationRate = 0f;

    [Header("Stacking")]
    public bool isStackable = false;
    [Tooltip("Max items per stack when isStackable is true.")]
    public int maxStackSize = 1;
    // ── Weapon fields (shown when itemType == Weapon) ─────────────────────────

    public WeaponType weaponType;
    public Handedness handedness;

    // ── Ranged-only fields (shown when weaponType == Ranged) ──────────────────

    [Tooltip("Prefab instantiated when firing. Must have a Bullet component.")]
    public GameObject bulletPrefab;

    [Tooltip("Initial speed of the bullet in units per second.")]
    public float bulletSpeed = 30f;

    [Tooltip("Downward acceleration applied to the bullet in units/s². 0 = no drop.")]
    public float bulletDrop = 9.81f;

    [Tooltip("How long in seconds before the bullet despawns. 0 = never despawn.")]
    public float bulletLifetime = 5f;

    [Tooltip("FOV when aiming down sights. Only applied when aiming with this weapon.")]
    public float aimFOV = 45f;

    // ── Armour fields (shown when itemType == Armour) ─────────────────────────

    [Tooltip("Which body part this armour protects. " +
             "Set layerStats.category to SubPartCategory.Armour.")]
    public BodyPart targetBodyPart;
    public SubPartDefinition layerStats;

    // ── Augment fields (shown when itemType == Augment) ──────────────────────

    [Tooltip("ID of the AugmentEntry in the assigned AugmentCatalogue. " +
             "Use this ItemDefinition on a PickupItem world GameObject.")]
    public string augmentID;
    // ── Bag fields (shown when itemType == Bag) ───────────────────────────────
    [Tooltip("Extra inventory slots granted while this bag is equipped.")]
    public int bagSlotCapacity = 10;
    // ── Convenience helpers ───────────────────────────────────────────────────
    public bool IsArmour => itemType == ItemType.Armour;
    public bool IsWeapon => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    public bool IsAugment => itemType == ItemType.Augment;
    public bool IsBag => itemType == ItemType.Bag; public bool IsRanged => itemType == ItemType.Weapon && weaponType == WeaponType.Ranged;
    public bool IsTwoHanded => itemType == ItemType.Weapon && handedness == Handedness.TwoHanded;
}
