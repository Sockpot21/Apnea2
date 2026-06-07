// PlayerEquipment.cs
// Manages all 13 equipment slots (11 armour + 2 hands).
// Handles shooting for ranged weapons and aim state.
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

    // Muzzle points — found on instantiated weapon prefabs
    // TODO: when real weapon models are ready, instantiate worldPrefab and
    // parent it to the appropriate hand bone. Then GetComponentInChildren<WeaponMuzzle>()
    // will find the muzzle automatically from wherever it was set on the prefab.
    private WeaponMuzzle _leftMuzzle;
    private WeaponMuzzle _rightMuzzle;

    // Aim state
    private bool _isAiming = false;

    // ── Events ────────────────────────────────────────────────────────────────

    public event System.Action OnEquipmentChanged;

    // Properties read by CameraFOV
    public bool IsAiming => _isAiming;
    public float AimFOV => GetAimingWeapon()?.aimFOV ?? 45f;
    public bool HasRangedWeapon =>
        (_rightHand != null && _rightHand.IsRanged) ||
        (_leftHand != null && _leftHand.IsRanged);

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
        runtimeLayer.category = SubPartCategory.Armour;

        var bodyPart = healthManager.GetBodyPart(part);
        if (bodyPart == null)
        {
            Debug.LogWarning($"[Equipment] Body part {part} not found in HealthManager.");
            return false;
        }

        bodyPart.layers.Insert(0, runtimeLayer);
        _armourSlots[part] = item;
        _armourLayers[part] = runtimeLayer;

        Debug.Log($"[Equipment] Armour equipped: '{item.displayName}' → {part}.");
        OnEquipmentChanged?.Invoke();
        return true;
    }

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
                ? $"[Equipment] '{item.displayName}' destroyed — returned broken."
                : $"[Equipment] '{item.displayName}' unequipped from {part}.");
        }

        _armourSlots[part] = null;

        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.displayName}'.");

        OnEquipmentChanged?.Invoke();
    }

    // ── Hand equip / unequip ──────────────────────────────────────────────────

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
            RefreshMuzzle(HandSlot.Left);
            RefreshMuzzle(HandSlot.Right);
            Debug.Log($"[Equipment] Two-handed '{item.displayName}' equipped.");
            OnEquipmentChanged?.Invoke();
            return true;
        }

        // If other hand holds a two-handed weapon, clear both first
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

        RefreshMuzzle(hand);

        Debug.Log($"[Equipment] '{item.displayName}' equipped in {hand} hand.");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public void UnequipHand(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;

        if (item.IsTwoHanded)
        {
            _leftHand = null;
            _rightHand = null;
            _leftMuzzle = null;
            _rightMuzzle = null;
        }
        else
        {
            if (hand == HandSlot.Left) { _leftHand = null; _leftMuzzle = null; }
            else { _rightHand = null; _rightMuzzle = null; }
        }

        // Cancel aim if no ranged weapon remains
        if (!HasRangedWeapon) _isAiming = false;

        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.displayName}'.");

        Debug.Log($"[Equipment] '{item.displayName}' unequipped from {hand} hand.");
        OnEquipmentChanged?.Invoke();
    }

    // ── Use hand items ────────────────────────────────────────────────────────

    public void UseLeftHand()
    {
        if (_leftHand == null) return;
        if (_leftHand.IsRanged) Fire(HandSlot.Left);
        else if (_leftHand.IsWeapon)
            Debug.Log($"[Equipment] Melee attack: '{_leftHand.displayName}' (left). TODO: melee system.");
        else if (_leftHand.IsConsumable)
            Debug.Log($"[Equipment] Used consumable: '{_leftHand.displayName}' (left).");
    }

    public void UseRightHand()
    {
        if (_rightHand == null) return;
        if (_rightHand.IsRanged) Fire(HandSlot.Right);
        else if (_rightHand.IsWeapon)
            Debug.Log($"[Equipment] Melee attack: '{_rightHand.displayName}' (right). TODO: melee system.");
        else if (_rightHand.IsConsumable)
            Debug.Log($"[Equipment] Used consumable: '{_rightHand.displayName}' (right).");
    }

    // ── Aim toggle ────────────────────────────────────────────────────────────

    public void ToggleAim()
    {
        if (!HasRangedWeapon)
        {
            _isAiming = false;
            Debug.Log("[Equipment] ToggleAim: no ranged weapon equipped.");
            return;
        }
        _isAiming = !_isAiming;
        Debug.Log($"[Equipment] Aim: {(_isAiming ? "ON" : "OFF")} | aimFOV: {AimFOV}");
    }

    // ── Shooting ──────────────────────────────────────────────────────────────

    private void Fire(HandSlot hand)
    {
        var weapon = hand == HandSlot.Left ? _leftHand : _rightHand;
        if (weapon == null || !weapon.IsRanged) return;

        if (weapon.bulletPrefab == null)
        {
            Debug.LogWarning($"[Equipment] '{weapon.displayName}' has no bulletPrefab assigned.");
            return;
        }

        // Spawn position and direction
        Vector3 spawnPos;
        Vector3 spawnDir;

        var muzzle = hand == HandSlot.Left ? _leftMuzzle : _rightMuzzle;
        if (muzzle != null)
        {
            spawnPos = muzzle.transform.position;
            spawnDir = muzzle.transform.forward;
        }
        else
        {
            // Fallback: screen centre ray until weapon models are parented to hands
            var cam = Camera.main;
            if (cam == null) { Debug.LogWarning("[Equipment] No main camera."); return; }
            var ray = cam.ScreenPointToRay(
                new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            spawnPos = ray.origin + ray.direction * 0.5f;
            spawnDir = ray.direction;
        }

        var bulletGO = Instantiate(weapon.bulletPrefab, spawnPos,
                                   Quaternion.LookRotation(spawnDir));

        var bullet = bulletGO.GetComponent<Bullet>();
        if (bullet == null)
        {
            Debug.LogWarning($"[Equipment] bulletPrefab '{weapon.bulletPrefab.name}' " +
                             $"needs a Bullet component.");
            Destroy(bulletGO);
            return;
        }

        // Inherit the player's current velocity so bullets feel natural at any speed
        var playerRb = GetComponent<Rigidbody>();
        var inheritedVel = playerRb != null ? playerRb.linearVelocity : Vector3.zero;
        // If no Rigidbody (KCC), read velocity from PlayerCharacter state
        if (playerRb == null)
        {
            var pc = GetComponent<PlayerCharacter>();
            if (pc != null) inheritedVel = pc.GetState().Velocity;
        }

        bullet.speed = weapon.bulletSpeed;
        bullet.drop = weapon.bulletDrop;
        bullet.lifetime = weapon.bulletLifetime;
        bullet.Launch(spawnDir, inheritedVel);

        Debug.Log($"[Equipment] Fired '{weapon.displayName}' — " +
                  $"speed: {weapon.bulletSpeed}, drop: {weapon.bulletDrop}, " +
                  $"lifetime: {(weapon.bulletLifetime <= 0f ? "∞" : weapon.bulletLifetime + "s")}");
    }

    // ── Muzzle refresh ────────────────────────────────────────────────────────

    // TODO: when weapon models are instantiated and parented to hand bones,
    // call RefreshMuzzle(hand) after instantiation to pick up the WeaponMuzzle
    // component from the model hierarchy automatically.
    private void RefreshMuzzle(HandSlot hand)
    {
        // For now muzzle is null until a weapon model is parented.
        // When the model is instantiated call:
        //   var instance = Instantiate(item.worldPrefab, handBoneTransform);
        //   var muzzle = instance.GetComponentInChildren<WeaponMuzzle>();
        //   if (hand == HandSlot.Left) _leftMuzzle = muzzle;
        //   else _rightMuzzle = muzzle;
        if (hand == HandSlot.Left) _leftMuzzle = null;
        else _rightMuzzle = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ItemDefinition GetAimingWeapon()
    {
        if (_rightHand != null && _rightHand.IsRanged) return _rightHand;
        if (_leftHand != null && _leftHand.IsRanged) return _leftHand;
        return null;
    }

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
