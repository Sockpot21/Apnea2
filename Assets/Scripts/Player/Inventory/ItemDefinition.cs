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

public enum ConsumableKind { General, Healing, Biofluid, Ammo, Edible }

public enum FireMode { SemiAutomatic, BurstFire, Automatic }

public enum ConsumableEffectTiming
{
    AfterUse,
    AfterTimedEffectExpires
}

public enum ConsumableLifecycleChange
{
    PlayerStat,
    Saturation,
    Stamina,
    Biofluid,
    Health,
    MaximumHealth
}

public enum ConsumableHealthTarget
{
    SelectedLimb,
    FixedBodyPart,
    SpecificOrgan,
    AllSkin,
    AllMuscle,
    AllBone,
    AllOrgans,
    EntireBody
}

[System.Serializable]
public class ConsumableLifecycleEffect
{
    [Tooltip("When this one-shot effect is applied. AfterUse occurs only after the item is used successfully.")]
    public ConsumableEffectTiming timing;

    [Tooltip("What this lifecycle entry changes. The Inspector only shows fields relevant to this selection.")]
    public ConsumableLifecycleChange changes;

    [Tooltip("Player stat and modifier mode used when Changes is Player Stat.")]
    public AugmentStatOverride playerStatModifier = new AugmentStatOverride();

    [Tooltip("Signed amount applied to the selected vital or health target.")]
    public float amount;

    [Tooltip("Where Health or Maximum Health changes are applied.")]
    public ConsumableHealthTarget healthTarget = ConsumableHealthTarget.SelectedLimb;

    [Tooltip("Body part used by Fixed Body Part and Specific Organ targeting.")]
    public BodyPart targetBodyPart;

    [Tooltip("Organ category used by Specific Organ targeting.")]
    public SubPartCategory targetOrgan = SubPartCategory.Heart;

    [Tooltip("Damage type used when a Health change is negative.")]
    public DamageType negativeHealthDamageType = DamageType.Blunt;

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
    [Min(0f), Tooltip("Damage dealt to combat damage receivers such as enemies.")]
    public float weaponDamage = 20f;
    public DamageType weaponDamageType = DamageType.Slash;

    // ── Ranged-only fields (shown when weaponType == Ranged) ──────────────────

    [Tooltip("Ammunition this weapon accepts. The ammo type supplies the projectile prefab.")]
    public AmmoTypeDefinition ammoType;

    [Min(1)] public int clipSize = 1;
    public FireMode fireMode = FireMode.SemiAutomatic;
    [Min(1)] public int burstCount = 3;
    [Min(1f), Tooltip("Maximum firing cadence in rounds per minute.")]
    public float roundsPerMinute = 60f;
    [Min(0f)] public float reloadTime = 1f;

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

    // ── Bag fields (shown when itemType == Bag) ───────────────────────────────
    [Tooltip("Extra inventory slots granted while this bag is equipped.")]
    public int bagSlotCapacity = 10;

    [Header("Consumable Treatment")]
    public ConsumableKind consumableKind;
    [Min(0f)] public float healthRestore;
    [Min(0f)] public float biofluidRestore;
    public bool restoresMaxHealth;
    public List<DamageType> treatableDamageTypes = new List<DamageType>();
    public List<HealthConditionType> removableConditions = new List<HealthConditionType>();

    [Header("Edible")]
    [Min(0f), Tooltip("Saturation restored when this edible is consumed.")]
    public float saturationRestore;
    [Tooltip("Marks this edible as a future cooking ingredient.")]
    public bool isIngredient;

    [Header("Timed Consumable Effects")]
    [Min(0f), Tooltip("Shared duration for timed player-stat modifiers. After Timed Effect Expires fires after the last active modifier ends.")]
    public float effectDuration;
    [Tooltip("When enabled, every timed stat modifier uses Effect Duration. When disabled, each entry exposes its own duration.")]
    public bool timedEffectsShareTimer = true;
    [Tooltip("Temporary player-controller modifiers. Using the same item again refreshes its timer.")]
    public List<AugmentStatOverride> timedStatEffects = new List<AugmentStatOverride>();

    [Header("Consumable Lifecycle Effects")]
    [Min(0f), Tooltip("Shared duration for lifecycle Player Stat changes.")]
    public float lifecycleEffectDuration = 1f;
    [Tooltip("When enabled, lifecycle Player Stat changes share Lifecycle Effect Duration. When disabled, each entry exposes its own duration.")]
    public bool lifecycleEffectsShareTimer = true;
    [Tooltip("Optional one-shot changes fired after a successful use or when this item's timed effect expires. Limb changes require a healing-item target.")]
    public List<ConsumableLifecycleEffect> lifecycleEffects = new List<ConsumableLifecycleEffect>();
    // ── Convenience helpers ───────────────────────────────────────────────────
    public bool IsArmour => itemType == ItemType.Armour;
    public bool IsWeapon => itemType == ItemType.Weapon;
    public bool IsConsumable => itemType == ItemType.Consumable;
    public bool IsAmmo => IsConsumable && consumableKind == ConsumableKind.Ammo;
    public bool IsEdible => IsConsumable && consumableKind == ConsumableKind.Edible;
    public bool IsAugment => itemType == ItemType.Augment;
    public bool IsBag => itemType == ItemType.Bag; public bool IsRanged => itemType == ItemType.Weapon && weaponType == WeaponType.Ranged;
    public bool IsTwoHanded => itemType == ItemType.Weapon && handedness == Handedness.TwoHanded;
}
