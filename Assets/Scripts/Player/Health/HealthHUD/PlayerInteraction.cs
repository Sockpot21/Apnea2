// PlayerInteraction.cs
// Handles E key interaction (station + item pickup), inventory toggle,
// and action map / cursor switching.
// Attach to the same GameObject as Player.cs.

using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private ModificationStationUI stationUI;
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private PlayerInventory playerInventory;

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
        if (stationUI != null)
        {
            stationUI.OnUIOpened += HandleUIOpened;
            stationUI.OnUIClosed += HandleUIClosed;
        }
        if (inventoryUI != null)
        {
            inventoryUI.OnUIOpened += HandleUIOpened;
            inventoryUI.OnUIClosed += HandleUIClosed;
        }
    }

    private void OnDisable()
    {
        if (stationUI != null)
        {
            stationUI.OnUIOpened -= HandleUIOpened;
            stationUI.OnUIClosed -= HandleUIClosed;
        }
        if (inventoryUI != null)
        {
            inventoryUI.OnUIOpened -= HandleUIOpened;
            inventoryUI.OnUIClosed -= HandleUIClosed;
        }
    }

    private void Update()
    {
        if (_inputActions == null) return;

        // Interact (E key) — only when no UI is open
        if (!_uiOpen && _inputActions.Gameplay.Interact.WasPressedThisFrame())
            TryInteract();

        // Inventory toggle
        if (_inputActions.Gameplay.Inventory.WasPressedThisFrame())
            ToggleInventory();
    }

    // ── Interact (E key) ──────────────────────────────────────────────────────

    private void TryInteract()
    {
        var cam = Camera.main.transform;

        // Cast a ray from the centre of the screen
        var ray = new Ray(cam.position, cam.forward);

        // Check what the ray hits within range
        if (Physics.Raycast(ray, out var hit, interactionRange, interactionMask))
        {
            // Try pickup first (most common world interaction)
            var pickup = hit.collider.GetComponent<PickupItem>();
            if (pickup != null)
            {
                TryPickup(pickup);
                return;
            }

            // Try modification station
            var station = hit.collider.GetComponent<ModificationStation>();
            if (station != null)
            {
                // Close inventory if open before opening station
                if (inventoryUI != null && inventoryUI.IsOpen)
                    inventoryUI.Close();

                station.Interact();
                return;
            }
        }
        else
        {
            // Raycast missed — fall back to proximity check for station
            // (station may not have a collider perfectly centred for raycast)
            var station = FindClosestStation(cam.position);
            if (station != null)
            {
                if (inventoryUI != null && inventoryUI.IsOpen)
                    inventoryUI.Close();

                station.Interact();
            }
        }
    }

    // ── Pickup ────────────────────────────────────────────────────────────────

    private void TryPickup(PickupItem pickup)
    {
        if (playerInventory == null)
        {
            Debug.LogWarning("[Interact] No PlayerInventory assigned.");
            return;
        }

        if (!playerInventory.HasSpace())
        {
            Debug.Log("[Interact] Inventory full.");
            return;
        }

        var item = pickup.Collect();
        playerInventory.TryAdd(item);
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
            // Close station UI if open
            if (stationUI != null && stationUI.IsOpen)
                stationUI.Close();

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
        // Only re-enable gameplay if no other UI is still open
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
