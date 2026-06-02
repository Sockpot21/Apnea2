// PlayerInventory.cs
// Manages the player's item slots at runtime.
// Attach to the player root GameObject (same as Player.cs).
// The UI lives in InventoryUI.cs on the Canvas.

using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Total number of inventory slots.")]
    [SerializeField] private int slotCount = 20;

    // Runtime slot data — null means empty
    private ItemDefinition[] _slots;

    // ── Events ────────────────────────────────────────────────────────────────

    public event System.Action OnInventoryChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _slots = new ItemDefinition[slotCount];
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public int SlotCount => _slots.Length;

    public ItemDefinition GetSlot(int index) => _slots[index];

    /// <summary>Adds item to first empty slot. Returns true on success.</summary>
    public bool TryAdd(ItemDefinition item)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] == null)
            {
                _slots[i] = item;
                OnInventoryChanged?.Invoke();
                Debug.Log($"[Inventory] Added '{item.displayName}' to slot {i}.");
                return true;
            }
        }
        Debug.Log("[Inventory] Full — could not add item.");
        return false;
    }

    /// <summary>Removes item at index and returns it. Returns null if slot empty.</summary>
    public ItemDefinition RemoveAt(int index)
    {
        if (index < 0 || index >= _slots.Length) return null;
        var item = _slots[index];
        if (item == null) return null;

        _slots[index] = null;
        OnInventoryChanged?.Invoke();
        Debug.Log($"[Inventory] Removed '{item.displayName}' from slot {index}.");
        return item;
    }

    /// <summary>Returns true if at least one slot is free.</summary>
    public bool HasSpace()
    {
        foreach (var s in _slots)
            if (s == null) return true;
        return false;
    }
}
