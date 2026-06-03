// PlayerInteraction.cs
// Handles E key interaction (station + item pickup), inventory toggle,
// hand use (left/right click), aim (middle click), and action map switching.
// Attach to the same GameObject as Player.cs.
//
// ── Input Action setup required ──────────────────────────────────────────────
// In your PlayerInput asset, Gameplay action map, add:
//   UseLeft   → Left Mouse Button  (Button)
//   UseRight  → Right Mouse Button (Button)
//   Aim       → Middle Mouse Button (Button)
// Regenerate the C# class after saving.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private ModificationStationUI stationUI;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerEquipment playerEquipment;

    [Header("Interaction")]
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private PlayerInput _inputActions;
    public bool _uiOpen = false;

    // ── Called by Player.cs ───────────────────────────────────────────────────

    public void SetInputActions(PlayerInput inputActions)
    {
        _inputActions = inputActions;
        _inputActions.UI.Close.performed += _ => TryCloseActiveUI();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (stationUI != null) { stationUI.OnUIOpened += HandleUIOpened; stationUI.OnUIClosed += HandleUIClosed; }
        if (inventoryUI != null) { inventoryUI.OnUIOpened += HandleUIOpened; inventoryUI.OnUIClosed += HandleUIClosed; }
    }

    private void OnDisable()
    {
        if (stationUI != null) { stationUI.OnUIOpened -= HandleUIOpened; stationUI.OnUIClosed -= HandleUIClosed; }
        if (inventoryUI != null) { inventoryUI.OnUIOpened -= HandleUIOpened; inventoryUI.OnUIClosed -= HandleUIClosed; }
    }

    private void Update()
    {
        if (_inputActions == null) return;

        // ── Gameplay input (only when no UI open) ─────────────────────────────
        if (!_uiOpen)
        {
            if (_inputActions.Gameplay.Interact.WasPressedThisFrame())
                TryInteract();

            if (_inputActions.Gameplay.UseLeft.WasPressedThisFrame())
                playerEquipment?.UseLeftHand();

            if (_inputActions.Gameplay.UseRight.WasPressedThisFrame())
                playerEquipment?.UseRightHand();

            if (_inputActions.Gameplay.Aim.WasPressedThisFrame())
                playerEquipment?.Aim();
        }

        // Inventory toggle works regardless of other UI state
        if (_inputActions.Gameplay.Inventory.WasPressedThisFrame())
            ToggleInventory();
    }

    // ── Interact (E) ──────────────────────────────────────────────────────────

    private void TryInteract()
    {
        var cam = Camera.main.transform;
        var ray = new Ray(cam.position, cam.forward);

        if (Physics.Raycast(ray, out var hit, interactionRange, interactionMask))
        {
            var pickup = hit.collider.GetComponent<PickupItem>();
            if (pickup != null) { TryPickup(pickup); return; }

            var station = hit.collider.GetComponent<ModificationStation>();
            if (station != null)
            {
                if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.Close();
                station.Interact();
                return;
            }
        }
        else
        {
            var station = FindClosestStation(cam.position);
            if (station != null)
            {
                if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.Close();
                station.Interact();
            }
        }
    }

    private void TryPickup(PickupItem pickup)
    {
        if (playerInventory == null) { Debug.LogWarning("[Interact] No PlayerInventory assigned."); return; }
        if (!playerInventory.HasSpace()) { Debug.Log("[Interact] Inventory full."); return; }
        playerInventory.TryAdd(pickup.Collect());
    }

    // ── Inventory toggle ──────────────────────────────────────────────────────

    private void ToggleInventory()
    {
        if (inventoryUI == null) return;

        if (inventoryUI.IsOpen)
        {
            inventoryUI.Close();
        }
        else
        {
            if (stationUI != null && stationUI.IsOpen) stationUI.Close();
            inventoryUI.Open();
        }
    }

    // ── Close active UI (Escape) ──────────────────────────────────────────────

    private void TryCloseActiveUI()
    {
        if (!_uiOpen) return;
        if (stationUI != null && stationUI.IsOpen) { stationUI.Close(); return; }
        if (inventoryUI != null && inventoryUI.IsOpen) { inventoryUI.Close(); return; }
    }

    // ── Action map switching ──────────────────────────────────────────────────

    private void HandleUIOpened()
    {
        _uiOpen = true;
        _inputActions.Gameplay.Disable();
        _inputActions.UI.Enable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HandleUIClosed()
    {
        if ((stationUI != null && stationUI.IsOpen) ||
            (inventoryUI != null && inventoryUI.IsOpen))
            return;

        _uiOpen = false;
        _inputActions.UI.Disable();
        _inputActions.Gameplay.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ModificationStation FindClosestStation(Vector3 from)
    {
        var stations = FindObjectsByType<ModificationStation>(FindObjectsSortMode.None);
        ModificationStation closest = null;
        float minDist = interactionRange;
        foreach (var s in stations)
        {
            float d = Vector3.Distance(from, s.transform.position);
            if (d < minDist) { minDist = d; closest = s; }
        }
        return closest;
    }
}
