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

    /// <summary>Adds item to a matching stack if possible, else the first empty slot. Returns true on success.</summary>
    public bool TryAdd(ItemInstance item)
    {
        if (item.definition.isStackable)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].definition == item.definition
                    && _slots[i].stackCount < item.definition.maxStackSize)
                {
                    _slots[i].stackCount++;
                    OnInventoryChanged?.Invoke();
                    Debug.Log($"[Inventory] Stacked '{item.definition.displayName}' in slot {i} ({_slots[i].stackCount}/{item.definition.maxStackSize}).");
                    return true;
                }
            }
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