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
    [SerializeField] private PlayerCharacter playerCharacter;

    [Header("Melee Hit Check")]
    [SerializeField, Min(0.1f)] private float meleeHitDistance = 2f;
    [SerializeField] private LayerMask meleeHitMask = ~0;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private Dictionary<BodyPart, ItemInstance> _armourSlots = new();
    private Dictionary<BodyPart, RuntimeSubPart> _armourLayers = new();

    private ItemInstance _leftHand;
    private ItemInstance _rightHand;
    private ItemInstance _bag;
    private readonly HashSet<HandSlot> _disabledHands = new();
    private readonly Dictionary<ItemInstance, Coroutine> _reloads = new();
    private readonly Dictionary<ItemInstance, Coroutine> _bursts = new();

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
    public float AimFOV => GetAimingWeapon()?.definition.aimFOV ?? 45f;
    public bool HasRangedWeapon =>
        (_rightHand != null && _rightHand.definition.IsRanged) ||
        (_leftHand != null && _leftHand.definition.IsRanged);
    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (playerCharacter == null)
            playerCharacter = GetComponent<PlayerCharacter>()
                ?? GetComponentInParent<PlayerCharacter>()
                ?? FindAnyObjectByType<PlayerCharacter>();
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            _armourSlots[part] = null;
        AmmoHUD.GetOrCreate(this, inventory);
    }

    // ── Public read access ────────────────────────────────────────────────────

    public ItemInstance GetArmourSlot(BodyPart part)
    {
        _armourSlots.TryGetValue(part, out var item);
        return item;
    }

    public RuntimeSubPart GetArmourLayer(BodyPart part)
    {
        _armourLayers.TryGetValue(part, out var layer);
        return layer;
    }

    public ItemInstance GetHandSlot(HandSlot hand) =>
        hand == HandSlot.Left ? _leftHand : _rightHand;

    public ItemInstance GetBagSlot() => _bag;

    public int GetAmmoReserve(ItemInstance weapon) =>
        weapon?.definition?.ammoType != null && inventory != null
            ? inventory.CountAmmo(weapon.definition.ammoType)
            : 0;

    /// <summary>Applies body-part damage to equipped armour durability.</summary>
    public bool ApplyArmourDamage(BodyPart part, float sourceDamage)
    {
        var item = GetArmourSlot(part);
        if (item == null) return false;
        item.ApplyDurabilityDamage(sourceDamage);
        return item.IsBroken;
    }

    /// <summary>Moves an external equipped item back to inventory, preserving its runtime state.</summary>
    public bool ReturnItemToInventory(ItemInstance item)
    {
        if (item == null) return false;
        bool returned = inventory != null && inventory.TryAdd(item);
        if (!returned)
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.definition.displayName}'.");
        OnEquipmentChanged?.Invoke();
        return returned;
    }


    // ── Armour equip / unequip ────────────────────────────────────────────────

    public bool TryEquipArmour(ItemInstance item)
    {
        if (!item.definition.IsArmour)
        {
            Debug.LogWarning($"[Equipment] '{item.definition.displayName}' is not an armour item.");
            return false;
        }

        var part = item.definition.targetBodyPart;
        if (_armourSlots[part] != null)
            UnequipArmour(part);

        var runtimeLayer = RuntimeSubPart.FromDefinition(item.definition.layerStats);
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

        Debug.Log($"[Equipment] Armour equipped: '{item.definition.displayName}' → {part}.");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public void UnequipArmour(BodyPart part)
    {
        var item = _armourSlots[part];
        var bodyPart = healthManager.GetBodyPart(part);

        if (_armourLayers.TryGetValue(part, out var layer) && bodyPart != null)
        {
            bool destroyed = layer.IsDestroyed || item.IsBroken;
            if (destroyed) item.Break();
            bodyPart.layers.Remove(layer);
            _armourLayers.Remove(part);
            Debug.Log(destroyed
                ? $"[Equipment] '{item.definition.displayName}' destroyed — returned broken."
                : $"[Equipment] '{item.definition.displayName}' unequipped from {part}.");
        }

        _armourSlots[part] = null;

        ReturnItemToInventory(item);
    }

    // ── Hand equip / unequip ──────────────────────────────────────────────────

    public bool TryEquipHand(ItemInstance item, HandSlot hand)
    {
        if (_disabledHands.Contains(hand))
        {
            Debug.LogWarning($"[Equipment] Cannot equip {hand} hand: the arm is non-functional.");
            return false;
        }

        if (!item.definition.IsWeapon && !item.definition.IsConsumable)
        {
            Debug.LogWarning($"[Equipment] '{item.definition.displayName}' is not a weapon or consumable.");
            return false;
        }
        if (item.definition.IsAmmo)
        {
            Debug.LogWarning("[Equipment] Ammunition is loaded from inventory and cannot be equipped in a hand.");
            return false;
        }

        item.InitializeMagazineIfNeeded();

        if (item.definition.IsTwoHanded)
        {
            var requiredOtherHand = hand == HandSlot.Left ? HandSlot.Right : HandSlot.Left;
            if (_disabledHands.Contains(requiredOtherHand))
            {
                Debug.LogWarning("[Equipment] Cannot equip a two-handed item while one arm is non-functional.");
                return false;
            }
            if (_leftHand != null) ReturnHandItemToInventory(HandSlot.Left);
            if (_rightHand != null) ReturnHandItemToInventory(HandSlot.Right);
            _leftHand = item;
            _rightHand = item;
            RefreshMuzzle(HandSlot.Left);
            RefreshMuzzle(HandSlot.Right);
            Debug.Log($"[Equipment] Two-handed '{item.definition.displayName}' equipped.");
            OnEquipmentChanged?.Invoke();
            return true;
        }

        // If other hand holds a two-handed weapon, clear both first
        var otherHand = hand == HandSlot.Left ? HandSlot.Right : HandSlot.Left;
        var otherHandItem = GetHandSlot(otherHand);
        if (otherHandItem != null && otherHandItem.definition.IsTwoHanded)
            ReturnHandItemToInventory(otherHand);

        if (GetHandSlot(hand) != null)
            ReturnHandItemToInventory(hand);

        if (hand == HandSlot.Left) _leftHand = item;
        else _rightHand = item;

        RefreshMuzzle(hand);

        Debug.Log($"[Equipment] '{item.definition.displayName}' equipped in {hand} hand.");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public void UnequipHand(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;
        CancelWeaponActions(item);

        if (item.definition.IsTwoHanded)
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
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.definition.displayName}'.");

        Debug.Log($"[Equipment] '{item.definition.displayName}' unequipped from {hand} hand.");
        OnEquipmentChanged?.Invoke();
    }

    public void DisableHandAndDrop(HandSlot hand)
    {
        if (!_disabledHands.Add(hand)) return;
        ItemInstance item = GetHandSlot(hand);
        if (item != null)
        {
            CancelWeaponActions(item);
            bool wasTwoHanded = item.definition.IsTwoHanded;
            ClearHandItem(item);
            DropItem(item);
            if (wasTwoHanded)
                Debug.Log($"[Equipment] Two-handed '{item.definition.displayName}' dropped because {hand} arm failed.");
            else
                Debug.Log($"[Equipment] '{item.definition.displayName}' dropped because {hand} arm failed.");
        }
        if (!HasRangedWeapon) _isAiming = false;
        OnEquipmentChanged?.Invoke();
    }

    // ── Bag equip / unequip ───────────────────────────────────────────────────

    public bool TryEquipBag(ItemInstance item)
    {
        if (!item.definition.IsBag)
        {
            Debug.LogWarning($"[Equipment] '{item.definition.displayName}' is not a bag.");
            return false;
        }

        if (_bag != null) UnequipBag();

        _bag = item;
        inventory.AddBonusSlots(item.definition.bagSlotCapacity);
        Debug.Log($"[Equipment] Bag equipped: '{item.definition.displayName}' (+{item.definition.bagSlotCapacity} slots).");
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public void UnequipBag()
    {
        if (_bag == null) return;

        if (!inventory.RemoveBonusSlots(_bag.definition.bagSlotCapacity))
        {
            Debug.LogWarning($"[Equipment] Cannot unequip '{_bag.definition.displayName}' — empty the extra slots first.");
            return;
        }

        var item = _bag;
        _bag = null;

        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.definition.displayName}'.");

        Debug.Log($"[Equipment] Bag unequipped: '{item.definition.displayName}'.");
        OnEquipmentChanged?.Invoke();
    }

    // ── Use hand items ────────────────────────────────────────────────────────

    public void UseLeftHand()
    {
        HandleUseInput(HandSlot.Left, true, true);
    }

    public void UseRightHand()
    {
        HandleUseInput(HandSlot.Right, true, true);
    }

    public void HandleUseInput(HandSlot hand, bool pressedThisFrame, bool held)
    {
        ItemInstance item = GetHandSlot(hand);
        if (item == null) return;
        if (item.definition.IsRanged)
        {
            HandleRangedInput(hand, item, pressedThisFrame, held);
            return;
        }
        if (!pressedThisFrame) return;
        if (item.definition.IsWeapon) TryMeleeHit(hand);
        else if (item.definition.IsConsumable) UseConsumable(hand);
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

    private void HandleRangedInput(HandSlot hand, ItemInstance weapon, bool pressedThisFrame, bool held)
    {
        if (weapon.isReloading) return;
        switch (weapon.definition.fireMode)
        {
            case FireMode.SemiAutomatic:
                if (pressedThisFrame) TryFire(hand, weapon);
                break;
            case FireMode.BurstFire:
                if (pressedThisFrame && !_bursts.ContainsKey(weapon))
                    _bursts[weapon] = StartCoroutine(BurstRoutine(hand, weapon));
                break;
            case FireMode.Automatic:
                if (held) TryFire(hand, weapon);
                break;
        }
    }

    private System.Collections.IEnumerator BurstRoutine(HandSlot hand, ItemInstance weapon)
    {
        int shots = Mathf.Max(1, weapon.definition.burstCount);
        float interval = SecondsPerShot(weapon);
        for (int i = 0; i < shots; i++)
        {
            if (!IsEquipped(weapon) || weapon.isReloading || !TryFire(hand, weapon)) break;
            if (i + 1 < shots) yield return new WaitForSeconds(interval);
        }
        // Keep the routine alive through initial StartCoroutine registration,
        // including one-shot bursts or an immediately rejected shot.
        yield return null;
        _bursts.Remove(weapon);
    }

    private bool TryFire(HandSlot hand, ItemInstance weapon)
    {
        if (weapon == null || !weapon.definition.IsRanged || weapon.IsBroken || weapon.isReloading)
            return false;
        weapon.InitializeMagazineIfNeeded();
        if (Time.time < weapon.nextAllowedFireTime) return false;
        if (weapon.currentClipAmmo <= 0)
        {
            BeginReload(weapon);
            return false;
        }

        AmmoTypeDefinition ammoType = weapon.definition.ammoType;
        GameObject projectilePrefab = ammoType != null ? ammoType.projectilePrefab : null;
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"[Equipment] '{weapon.definition.displayName}' has no ammo type or projectile prefab assigned.");
            return false;
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
            if (cam == null) { Debug.LogWarning("[Equipment] No main camera."); return false; }
            var ray = cam.ScreenPointToRay(
                new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            spawnPos = ray.origin + ray.direction * 0.5f;
            spawnDir = ray.direction;
        }

        var bulletGO = Instantiate(projectilePrefab, spawnPos,
                                   Quaternion.LookRotation(spawnDir));

        var bullet = bulletGO.GetComponent<Bullet>();
        if (bullet == null)
        {
            Debug.LogWarning($"[Equipment] projectile prefab '{projectilePrefab.name}' " +
                             $"needs a Bullet component.");
            Destroy(bulletGO);
            return false;
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

        bullet.speed = weapon.definition.bulletSpeed;
        bullet.drop = weapon.definition.bulletDrop;
        bullet.lifetime = weapon.definition.bulletLifetime;
        bullet.damage = weapon.definition.weaponDamage;
        bullet.damageType = weapon.definition.weaponDamageType;
        bullet.Launch(spawnDir, inheritedVel, gameObject);

        weapon.currentClipAmmo--;
        weapon.nextAllowedFireTime = Time.time + SecondsPerShot(weapon);
        weapon.Degrade();

        Debug.Log($"[Equipment] Fired '{weapon.definition.displayName}' — " +
                  $"speed: {weapon.definition.bulletSpeed}, drop: {weapon.definition.bulletDrop}, " +
                  $"lifetime: {(weapon.definition.bulletLifetime <= 0f ? "∞" : weapon.definition.bulletLifetime + "s")}, " +
                  $"ammo: {weapon.currentClipAmmo}/{weapon.definition.clipSize}, " +
                  $"durability: {weapon.currentDurability:F1}/{weapon.definition.maxDurability:F1}");
        OnEquipmentChanged?.Invoke();
        if (weapon.currentClipAmmo <= 0) BeginReload(weapon);
        ReturnBrokenHandItem(hand);
        return true;
    }

    private static float SecondsPerShot(ItemInstance weapon) =>
        60f / Mathf.Max(1f, weapon.definition.roundsPerMinute);

    public void ReloadAll()
    {
        var uniqueWeapons = new HashSet<ItemInstance>();
        if (_leftHand?.definition != null && _leftHand.definition.IsRanged) uniqueWeapons.Add(_leftHand);
        if (_rightHand?.definition != null && _rightHand.definition.IsRanged) uniqueWeapons.Add(_rightHand);
        foreach (ItemInstance weapon in uniqueWeapons) BeginReload(weapon);
    }

    private void BeginReload(ItemInstance weapon)
    {
        if (weapon == null || weapon.isReloading || weapon.IsBroken || !IsEquipped(weapon)) return;
        weapon.InitializeMagazineIfNeeded();
        int needed = Mathf.Max(1, weapon.definition.clipSize) - weapon.currentClipAmmo;
        if (needed <= 0 || GetAmmoReserve(weapon) <= 0 || weapon.definition.ammoType == null) return;
        weapon.isReloading = true;
        weapon.reloadEndsAt = Time.time + Mathf.Max(0f, weapon.definition.reloadTime);
        _reloads[weapon] = StartCoroutine(ReloadRoutine(weapon));
        OnEquipmentChanged?.Invoke();
    }

    private System.Collections.IEnumerator ReloadRoutine(ItemInstance weapon)
    {
        float duration = Mathf.Max(0f, weapon.definition.reloadTime);
        if (duration > 0f) yield return new WaitForSeconds(duration);
        else yield return null;
        _reloads.Remove(weapon);
        if (!IsEquipped(weapon))
        {
            weapon.isReloading = false;
            yield break;
        }

        int needed = Mathf.Max(1, weapon.definition.clipSize) - weapon.currentClipAmmo;
        int loaded = inventory != null ? inventory.ConsumeAmmo(weapon.definition.ammoType, needed) : 0;
        weapon.currentClipAmmo += loaded;
        weapon.isReloading = false;
        weapon.reloadEndsAt = 0f;
        Debug.Log($"[Equipment] Reloaded '{weapon.definition.displayName}' with {loaded} round(s) — " +
                  $"{weapon.currentClipAmmo}/{weapon.definition.clipSize}.");
        OnEquipmentChanged?.Invoke();
    }

    private bool IsEquipped(ItemInstance weapon) =>
        ReferenceEquals(_leftHand, weapon) || ReferenceEquals(_rightHand, weapon);

    private void CancelWeaponActions(ItemInstance weapon)
    {
        if (weapon == null) return;
        if (_reloads.TryGetValue(weapon, out Coroutine reload))
        {
            StopCoroutine(reload);
            _reloads.Remove(weapon);
        }
        if (_bursts.TryGetValue(weapon, out Coroutine burst))
        {
            StopCoroutine(burst);
            _bursts.Remove(weapon);
        }
        weapon.isReloading = false;
        weapon.reloadEndsAt = 0f;
    }

    /// <summary>Call from melee hit detection after a weapon strikes a solid target.</summary>
    public void NotifyMeleeHit(HandSlot hand)
    {
        var weapon = GetHandSlot(hand);
        if (weapon == null || !weapon.definition.IsWeapon || weapon.definition.IsRanged) return;
        weapon.Degrade();
        Debug.Log($"[Equipment] Melee hit: '{weapon.definition.displayName}' — durability: " +
                  $"{weapon.currentDurability:F1}/{weapon.definition.maxDurability:F1}");
        ReturnBrokenHandItem(hand);
        OnEquipmentChanged?.Invoke();
    }

    public void EnableHand(HandSlot hand)
    {
        if (!_disabledHands.Remove(hand)) return;
        OnEquipmentChanged?.Invoke();
    }

    private void TryMeleeHit(HandSlot hand)
    {
        var weapon = GetHandSlot(hand);
        if (weapon == null) return;

        var camera = Camera.main;
        if (camera == null)
        {
            Debug.LogWarning("[Equipment] Cannot check melee hit: no main camera.");
            return;
        }

        Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f));
        foreach (var hit in Physics.RaycastAll(ray, meleeHitDistance, meleeHitMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            foreach (MonoBehaviour component in hit.collider.GetComponentsInParent<MonoBehaviour>())
            {
                if (component is not ICombatDamageReceiver receiver) continue;
                receiver.ReceiveCombatDamage(new CombatDamage(weapon.definition.weaponDamage,
                    weapon.definition.weaponDamageType, hit.point,
                    ray.direction * Mathf.Max(1f, weapon.definition.weaponDamage * .12f), gameObject));
                break;
            }
            NotifyMeleeHit(hand);
            return;
        }

        Debug.Log($"[Equipment] Melee swing missed: '{weapon.definition.displayName}' ({hand}).");
    }

    private void Consume(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;

        item.stackCount--;
        Debug.Log($"[Equipment] Used consumable: '{item.definition.displayName}' ({hand}).");
        if (item.stackCount <= 0)
        {
            if (hand == HandSlot.Left) _leftHand = null;
            else _rightHand = null;
            if (!HasRangedWeapon) _isAiming = false;
        }
        OnEquipmentChanged?.Invoke();
    }

    private void UseConsumable(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;
        if (item.definition.consumableKind == ConsumableKind.Ammo) return;
        if (item.definition.consumableKind is ConsumableKind.General or ConsumableKind.Edible)
        {
            playerCharacter?.ApplyTimedConsumableEffect(item.definition, healthManager);
            if (item.definition.IsEdible)
                playerCharacter?.RestoreSaturation(item.definition.saturationRestore);
            Consume(hand);
            return;
        }

        var selector = HealingTargetSelectionUI.GetOrCreate();
        selector.Open(healthManager, this, hand, item);
    }

    public bool ApplyConsumableToPart(HandSlot hand, BodyPart part)
    {
        var item = GetHandSlot(hand);
        if (item == null || healthManager == null || !healthManager.TryApplyHealing(item.definition, part)) return false;
        playerCharacter?.ApplyTimedConsumableEffect(item.definition, healthManager, true, part);
        Consume(hand);
        return true;
    }

    private void ReturnBrokenHandItem(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null || !item.IsBroken) return;
        Debug.Log($"[Equipment] '{item.definition.displayName}' broke — returned to inventory.");
        UnequipHand(hand);
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

    private ItemInstance GetAimingWeapon()
    {
        if (_rightHand != null && _rightHand.definition.IsRanged) return _rightHand;
        if (_leftHand != null && _leftHand.definition.IsRanged) return _leftHand;
        return null;
    }

    private void ReturnHandItemToInventory(HandSlot hand)
    {
        var item = GetHandSlot(hand);
        if (item == null) return;
        CancelWeaponActions(item);
        if (item.definition.IsTwoHanded)
            ClearHandItem(item);
        else if (hand == HandSlot.Left) _leftHand = null;
        else _rightHand = null;
        if (!inventory.TryAdd(item))
            Debug.LogWarning($"[Equipment] Inventory full — could not return '{item.definition.displayName}'.");
    }

    private void ClearHandItem(ItemInstance item)
    {
        if (_leftHand == item) { _leftHand = null; _leftMuzzle = null; }
        if (_rightHand == item) { _rightHand = null; _rightMuzzle = null; }
    }

    private void DropItem(ItemInstance item)
    {
        if (item.definition.worldPrefab == null)
        {
            Debug.LogWarning($"[Equipment] '{item.definition.displayName}' has no world prefab and could not be dropped.");
            return;
        }

        Vector3 origin = transform.position + transform.forward * 0.75f + Vector3.up;
        Vector3 dropPosition = origin;
        if (Physics.Raycast(origin + Vector3.up, Vector3.down, out RaycastHit hit, 4f))
            dropPosition = hit.point + Vector3.up * 0.1f;

        GameObject dropped = Instantiate(item.definition.worldPrefab, dropPosition, Quaternion.identity); PickupItem pickup = dropped.GetComponent<PickupItem>();
        if (pickup == null) pickup = dropped.AddComponent<PickupItem>();
        pickup.item = item;

        if (dropped.TryGetComponent(out Rigidbody rigidbody))
            rigidbody.AddForce(transform.forward * 1.5f + Vector3.up, ForceMode.Impulse);
    }
}
