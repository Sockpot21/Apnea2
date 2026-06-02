// ItemDefinition.cs
// ScriptableObject blueprint for any item in the game.
// Create via: Assets > Create > Inventory > ItemDefinition

using UnityEngine;

public enum ItemType
{
    Misc,
    Weapon,
    Armour,
    Consumable,
    Quest
}

[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/ItemDefinition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    public string itemID;
    public string displayName;

    [TextArea]
    public string description;

    [Header("Classification")]
    public ItemType itemType;

    [Header("World Representation")]
    [Tooltip("The prefab spawned in the world when this item is dropped or exists as a pickup. " +
             "Must have (or will automatically receive) a PickupItem component. " +
             "Assign your pistol model, crate prefab, etc. here — " +
             "this is the mesh that appears both as a world pickup and when dropped.")]
    public GameObject worldPrefab;

    [Header("Visual")]
    [Tooltip("Tint colour shown in the inventory slot when this item is present.")]
    public Color slotColor = new Color(0.3f, 0.6f, 1f, 1f);

    [Header("Physical")]
    public float weight = 1f;
}
