// PlayerInventory.cs
// Manages the player's item slots at runtime.
// Attach to the player root GameObject (same as Player.cs).
// The UI lives in InventoryUI.cs on the Canvas.

using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Base number of inventory slots, before any bag bonus.")]
    [SerializeField] private int baseSlotCount = 10;

    // Runtime slot data — null means empty. Array length = baseSlotCount + bonus slots from an equipped bag.
    private ItemInstance[] _slots;
    private int _bonusSlots;

    // ── Events ────────────────────────────────────────────────────────────────

    public event System.Action OnInventoryChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _slots = new ItemInstance[baseSlotCount];
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public int SlotCount => _slots.Length;

    /// <summary>Enumerates the occupied inventory slots without exposing the slot array itself.</summary>
    public IEnumerable<ItemInstance> Items
    {
        get
        {
            foreach (var item in _slots)
                if (item != null) yield return item;
        }
    }

    public int UsedSlots
    {
        get
        {
            int used = 0;
            foreach (var s in _slots) if (s != null) used++;
            return used;
        }
    }

    public ItemInstance GetSlot(int index) => _slots[index];

    public bool CanAdd(ItemInstance item)
    {
        if (item?.definition == null) return false;
        if (!item.definition.isStackable) return HasSpace();

        int needed = Mathf.Max(1, item.stackCount);
        int maximum = Mathf.Max(1, item.definition.maxStackSize);
        foreach (ItemInstance slot in _slots)
        {
            if (slot == null) needed -= maximum;
            else if (slot.definition == item.definition)
                needed -= Mathf.Max(0, maximum - slot.stackCount);
            if (needed <= 0) return true;
        }
        return false;
    }

    public int CountAmmo(AmmoTypeDefinition ammoType)
    {
        if (ammoType == null) return 0;
        int total = 0;
        foreach (ItemInstance item in _slots)
            if (item?.definition != null && item.definition.IsAmmo && item.definition.ammoType == ammoType)
                total += Mathf.Max(0, item.stackCount);
        return total;
    }

    /// <summary>Consumes up to amount rounds and returns the quantity actually removed.</summary>
    public int ConsumeAmmo(AmmoTypeDefinition ammoType, int amount)
    {
        if (ammoType == null || amount <= 0) return 0;
        int remaining = amount;
        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            ItemInstance item = _slots[i];
            if (item?.definition == null || !item.definition.IsAmmo || item.definition.ammoType != ammoType)
                continue;
            int consumed = Mathf.Min(remaining, Mathf.Max(0, item.stackCount));
            item.stackCount -= consumed;
            remaining -= consumed;
            if (item.stackCount <= 0) _slots[i] = null;
        }
        int totalConsumed = amount - remaining;
        if (totalConsumed > 0) OnInventoryChanged?.Invoke();
        return totalConsumed;
    }

    /// <summary>Adds item to a matching stack if possible, else the first empty slot. Returns true on success.</summary>
    public bool TryAdd(ItemInstance item)
    {
        if (!CanAdd(item))
        {
            Debug.Log("[Inventory] Full — could not add item or complete stack.");
            return false;
        }
        if (item.definition.isStackable)
        {
            int quantity = Mathf.Max(1, item.stackCount);
            int maximum = Mathf.Max(1, item.definition.maxStackSize);
            int remaining = quantity;
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i] == null || _slots[i].definition != item.definition) continue;
                int added = Mathf.Min(remaining, maximum - _slots[i].stackCount);
                _slots[i].stackCount += added;
                remaining -= added;
            }
            bool usedOriginal = false;
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i] != null) continue;
                int added = Mathf.Min(remaining, maximum);
                ItemInstance stack = usedOriginal ? new ItemInstance(item.definition, added) : item;
                stack.stackCount = added;
                stack.currentDurability = item.currentDurability;
                _slots[i] = stack;
                usedOriginal = true;
                remaining -= added;
            }
            OnInventoryChanged?.Invoke();
            Debug.Log($"[Inventory] Added {quantity} × '{item.definition.displayName}'.");
            return true;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null)
            {
                _slots[i] = item;
                OnInventoryChanged?.Invoke();
                Debug.Log($"[Inventory] Added '{item.definition.displayName}' to slot {i}.");
                return true;
            }
        }
        Debug.Log("[Inventory] Full — could not add item.");
        return false;
    }

    /// <summary>Removes item at index and returns it. Returns null if slot empty.</summary>
    public ItemInstance RemoveAt(int index)
    {
        if (index < 0 || index >= _slots.Length) return null;
        var item = _slots[index];
        if (item == null) return null;

        _slots[index] = null;
        OnInventoryChanged?.Invoke();
        Debug.Log($"[Inventory] Removed '{item.definition.displayName}' from slot {index}.");
        return item;
    }

    /// <summary>Removes this exact runtime item instance, wherever it currently sits.</summary>
    public bool TryRemove(ItemInstance item)
    {
        if (item == null) return false;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!ReferenceEquals(_slots[i], item)) continue;
            RemoveAt(i);
            return true;
        }
        return false;
    }

    /// <summary>Moves the item at fromIndex into toIndex, swapping with whatever is already there (if anything).</summary>
    public bool MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _slots.Length) return false;
        if (toIndex < 0 || toIndex >= _slots.Length) return false;
        if (fromIndex == toIndex) return false;

        (_slots[toIndex], _slots[fromIndex]) = (_slots[fromIndex], _slots[toIndex]);
        OnInventoryChanged?.Invoke();
        return true;
    }

    /// <summary>Returns true if at least one slot is free.</summary>
    public bool HasSpace()
    {
        foreach (var s in _slots)
            if (s == null) return true;
        return false;
    }

    // ── Bag capacity ──────────────────────────────────────────────────────────

    /// <summary>Grows the slot array by amount, preserving existing items. Call when a bag is equipped.</summary>
    public void AddBonusSlots(int amount)
    {
        if (amount <= 0) return;
        System.Array.Resize(ref _slots, _slots.Length + amount);
        _bonusSlots += amount;
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Shrinks the slot array by amount, removing trailing slots. Call when a bag is unequipped.
    /// Fails (returns false) if any of those trailing slots are occupied.
    /// </summary>
    public bool RemoveBonusSlots(int amount)
    {
        if (amount <= 0) return true;
        int newLength = _slots.Length - amount;
        for (int i = newLength; i < _slots.Length; i++)
            if (_slots[i] != null) return false;

        System.Array.Resize(ref _slots, newLength);
        _bonusSlots -= amount;
        OnInventoryChanged?.Invoke();
        return true;
    }
}
