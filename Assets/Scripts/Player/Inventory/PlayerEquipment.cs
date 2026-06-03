// PlayerEquipment.cs
// Manages all 13 equipment slots at runtime:
//   - 11 armour slots (one per BodyPart)
//   - 2 hand slots (left hand, right hand)
// Integrates with HealthManager to insert/remove armour layers in the damage cascade.
// Attach to the same Player GameObject as PlayerInventory and HealthManager.

using System.Collections.Generic;
using UnityEngine;

public enum HandSlot { Left, Right }

public class PlayerEquipment : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private HealthManager healthManager;
    [SerializeField] private PlayerInventory inventory;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Dictionary<BodyPart, ItemDefinition> _armourSlots = new();
    private Dictionary<BodyPart, RuntimeSubPart> _armourLayers = new();

    private ItemDefinition _leftHand;
    private ItemDefinition _rightHand;

    // ── Events ────────────────────────────────────────────────────────────────

    public event System.Action OnEquipmentChanged;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            _armourSlots[part] = null;
    }

    // ── Public read access ────────────────────────────────────────────────────

    public ItemDefinition GetArmourSlot(BodyPart part)
    {
        _armourSlots.TryGetValue(part, out var item);
        return item;
    }

    public ItemDefinition GetHandSlot(HandSlot hand) =>
        hand == HandSlot.Left ? _leftHand : _rightHand;

    // ── Armour equip / unequip ────────────────────────────────────────────────

    /// <summary>
    /// Equips an armour item to its target body part slot.
    /// Inserts a RuntimeSubPart as the outermost layer in the damage cascade.
    /// If the slot is already occupied the existing piece is unequipped first.
    /// </summary>
    public bool TryEquipArmour(ItemDefinition item)
    {
        if (!item.IsArmour)
        {
            Debug.LogWarning($"[Equipment] '{item.displayName}' is not an armour item.");
            return false;
        }

        var part = item.targetBodyPart;

        if (_armourSlots[part] != null)
            UnequipArmour(part);

        var runtimeLayer = RuntimeSubPart.FromDefinition(item.layerStats);
        runtimeLayer.category = SubPartCategory.Armour; // always index 0, outermost

        var bodyPart = healthManager.GetBodyPart(part);
        if (bodyPart == null)
        {
            Debug.LogWarning($"[Equipment] Body part {part} not found in HealthManager.");
            return false;
        }

        bodyPart.layers.Insert(0, runtimeLayer);

        _armourSlots[part] = item;
        _armourLayers[part] = runtimeLayer;

        Debug.Log($"[Equipment] Armour equipped: '{item.displayName}' → {part} (layer 0).");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Unequips armour from the given body part and returns it to inventory.
    /// If the armour was destroyed (HP = 0) it is returned as a broken item.
    /// </summary>
    public void UnequipArmour(BodyPart part)
    {
        if (_armourSlots[part] == null) return;

        var item = _armourSlots[part];
        var bodyPart = healthManager.GetBodyPart(part);

        if (_armourLayers.TryGetValue(part, out var layer) && bodyPart != null)
        {
            bool destroyed = layer.IsDestroyed;
            bodyPart.layers.Remove(layer);
            _armourLayers.Remove(part);

            Debug.Log(destroyed
                ? $"[Equipment] '{item.displayName}' was destroyed — returned broken."
                : $"[Equipment] '{item.displayName}' unequipped from {part}.");
        }

        _armourSlots[part] = null;

        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.displayName}'.");

        OnEquipmentChanged?.Invoke();
    }

    // ── Hand equip / unequip ──────────────────────────────────────────────────

    /// <summary>
    /// Equips a weapon or consumable to the specified hand slot.
    /// Two-handed items occupy both slots; displaces existing items back to inventory.
    /// </summary>
    public bool TryEquipHand(ItemDefinition item, HandSlot hand)
    {
        if (!item.IsWeapon && !item.IsConsumable)
        {
            Debug.LogWarning($"[Equipment] '{item.displayName}' is not a weapon or consumable.");
            return false;
        }

        if (item.IsTwoHanded)
        {
            if (_leftHand != null) ReturnHandItemToInventory(HandSlot.Left);
            if (_rightHand != null) ReturnHandItemToInventory(HandSlot.Right);

            _leftHand = item;
            _rightHand = item;

            Debug.Log($"[Equipment] Two-handed '{item.displayName}' equipped in both hands.");
            OnEquipmentChanged?.Invoke();
            return true;
        }

        // One-handed: if other hand holds a two-handed weapon, clear both first
        var otherHand = hand == HandSlot.Left ? HandSlot.Right : HandSlot.Left;
        var otherHandItem = GetHandSlot(otherHand);
        if (otherHandItem != null && otherHandItem.IsTwoHanded)
        {
            _leftHand = null;
            _rightHand = null;
            if (!inventory.TryAdd(otherHandItem))
                Debug.LogWarning($"[Equipment] Inventory full — lost '{otherHandItem.displayName}'.");
        }

        if (GetHandSlot(hand) != null)
            ReturnHandItemToInventory(hand);

        if (hand == HandSlot.Left) _leftHand = item;
        else _rightHand = item;

        Debug.Log($"[Equipment] '{item.displayName}' equipped in {hand} hand.");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public void UnequipHand(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;

        if (item.IsTwoHanded) { _leftHand = null; _rightHand = null; }
        else if (hand == HandSlot.Left) _leftHand = null;
        else _rightHand = null;

        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.displayName}'.");

        Debug.Log($"[Equipment] '{item.displayName}' unequipped from {hand} hand.");
        OnEquipmentChanged?.Invoke();
    }

    // ── Use hand items ────────────────────────────────────────────────────────

    public void UseLeftHand()
    {
        if (_leftHand == null) return;
        Debug.Log(_leftHand.IsConsumable
            ? $"[Equipment] Used consumable: '{_leftHand.displayName}' (left hand)."
            : $"[Equipment] Attacked with: '{_leftHand.displayName}' (left hand).");
    }

    public void UseRightHand()
    {
        if (_rightHand == null) return;
        Debug.Log(_rightHand.IsConsumable
            ? $"[Equipment] Used consumable: '{_rightHand.displayName}' (right hand)."
            : $"[Equipment] Attacked with: '{_rightHand.displayName}' (right hand).");
    }

    public void Aim()
    {
        var weapon = _rightHand ?? _leftHand;
        if (weapon == null || !weapon.IsWeapon) return;
        Debug.Log(weapon.weaponType == WeaponType.Ranged
            ? $"[Equipment] Aiming '{weapon.displayName}'."
            : "[Equipment] Aim: no ranged weapon equipped.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ReturnHandItemToInventory(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;
        if (hand == HandSlot.Left) _leftHand = null;
        else _rightHand = null;
        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.displayName}'.");
    }
}
