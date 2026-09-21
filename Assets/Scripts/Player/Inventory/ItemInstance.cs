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
    public int currentClipAmmo;
    public bool magazineInitialized;

    [System.NonSerialized] public bool isReloading;
    [System.NonSerialized] public float reloadEndsAt;
    [System.NonSerialized] public float nextAllowedFireTime;
    [System.NonSerialized] public bool isCyclingAction;
    [System.NonSerialized] public float actionCycleEndsAt;
    [System.NonSerialized] public bool reloadQueued;

    public ItemInstance(ItemDefinition definition, int stackCount = 1)
    {
        this.definition = definition;
        this.currentDurability = definition.maxDurability;
        this.stackCount = stackCount;
        InitializeMagazineIfNeeded();
    }

    public void InitializeMagazineIfNeeded()
    {
        if (magazineInitialized || definition == null || !definition.IsRanged) return;
        currentClipAmmo = Mathf.Max(1, definition.magazineCapacity);
        magazineInitialized = true;
    }

    /// <summary>Applies a source amount scaled by this item's degradation rate.</summary>
    public void ApplyDurabilityDamage(float sourceAmount)
    {
        if (definition == null || definition.degradationRate <= 0f || sourceAmount <= 0f) return;
        currentDurability = Mathf.Max(0f, currentDurability - sourceAmount * definition.degradationRate);
    }

    /// <summary>Convenience overload for a single use, such as firing one shot.</summary>
    public void Degrade()
    {
        ApplyDurabilityDamage(1f);
    }

    /// <summary>Forces an item into its broken state when its runtime protection is destroyed.</summary>
    public void Break() => currentDurability = 0f;

    public bool IsBroken => currentDurability <= 0f;
}
