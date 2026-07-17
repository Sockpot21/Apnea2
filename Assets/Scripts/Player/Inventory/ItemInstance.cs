// ItemInstance.cs
// Runtime, per-instance wrapper around an ItemDefinition asset.
// Inventory/equipment slots and PickupItem store these instead of a raw
// ItemDefinition reference, so durability and stack count are per-item
// rather than shared across every copy of that item in the game.
using UnityEngine;

[System.Serializable]
public class ItemInstance
{
    public ItemDefinition definition;
    public float currentDurability;
    public int stackCount;

    public ItemInstance(ItemDefinition definition, int stackCount = 1)
    {
        this.definition = definition;
        this.currentDurability = definition.maxDurability;
        this.stackCount = stackCount;
    }

    /// <summary>Reduces durability by the item's degradationRate. Call on use (e.g. each shot).</summary>
    public void Degrade()
    {
        if (definition.degradationRate <= 0f) return;
        currentDurability = Mathf.Max(0f, currentDurability - definition.degradationRate);
    }

    public bool IsBroken => definition.degradationRate > 0f && currentDurability <= 0f;
}