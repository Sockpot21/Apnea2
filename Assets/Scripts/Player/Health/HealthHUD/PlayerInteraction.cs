// PlayerInteraction.cs
// Handles E key interaction (station + item pickup), inventory toggle,
// hand use (left/right click), aim (middle click), and action map switching.
// Attach to the same GameObject as Player.cs.
//
// ── Input Action setup required ──────────────────────────────────────────────
// In your PlayerInput asset, Gameplay action map, add:
//   UseLeft        → Left Mouse Button   (Button)
//   UseRight       → Right Mouse Button  (Button)
//   Aim            → Middle Mouse Button (Button)
//   Prone          → Z                   (Button)
//   Grapple        → F                   (Button)
//   GrappleAscend  → Forward [Mouse]     (Button)
//   GrappleDescend → Back [Mouse]        (Button)
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
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private float pickupSphereRadius = 0.6f;
    [Tooltip("Max angle (degrees) from camera forward a pickup can be and still be targeted.")]
    [SerializeField] private float pickupConeAngle = 40f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private PlayerInput _inputActions;
    public bool _uiOpen = false;

    // The pickup currently being highlighted — tracked so we can hide its prompt
    private PickupItem _highlightedPickup;

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

        if (!_uiOpen)
        {
            // Update which pickup is highlighted every frame
            UpdatePickupHighlight();

            if (_inputActions.Gameplay.Interact.WasPressedThisFrame())
                TryInteract();

            if (_inputActions.Gameplay.UseLeft.WasPressedThisFrame())
                playerEquipment?.UseLeftHand();

            if (_inputActions.Gameplay.UseRight.WasPressedThisFrame())
                playerEquipment?.UseRightHand();

            if (_inputActions.Gameplay.Aim.WasPressedThisFrame())
                playerEquipment?.ToggleAim();
        }
        else
        {
            // UI is open — make sure no prompt is showing
            ClearHighlight();
        }

        if (_inputActions.Gameplay.Inventory.WasPressedThisFrame())
            ToggleInventory();
    }

    // ── Pickup highlight (runs every frame) ───────────────────────────────────

    private void UpdatePickupHighlight()
    {
        var best = FindBestPickup();

        if (best == _highlightedPickup) return; // no change

        // Hide old
        _highlightedPickup?.HidePrompt();

        // Show new
        _highlightedPickup = best;
        _highlightedPickup?.ShowPrompt();
    }

    private void ClearHighlight()
    {
        if (_highlightedPickup == null) return;
        _highlightedPickup.HidePrompt();
        _highlightedPickup = null;
    }

    /// <summary>
    /// Finds the best pickup candidate using a two-phase approach.
    /// Rays from the screen centre so CameraSpring displacement never affects accuracy.
    ///   Phase 1: Raycast — if it hits a PickupItem directly, use it.
    ///   Phase 2: OverlapSphere at ray endpoint — score by angle + distance.
    /// </summary>
    private PickupItem FindBestPickup()
    {
        var cam = Camera.main;
        if (cam == null) return null;

        // Ray from exact screen centre — unaffected by CameraSpring position offset
        var ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        // Phase 1 — direct raycast
        if (Physics.Raycast(ray, out var hit, interactionRange, interactionMask))
        {
            var directPickup = hit.collider.GetComponent<PickupItem>();
            if (directPickup != null) return directPickup;
        }

        // Phase 2 — sphere overlap at ray endpoint
        var sphereCenter = ray.origin + ray.direction * interactionRange;
        var colliders = Physics.OverlapSphere(sphereCenter, pickupSphereRadius, interactionMask);

        PickupItem best = null;
        float bestScore = float.MaxValue;

        foreach (var col in colliders)
        {
            var pickup = col.GetComponent<PickupItem>();
            if (pickup == null) continue;

            var toPickup = pickup.transform.position - ray.origin;
            float dist = toPickup.magnitude;

            if (dist > interactionRange) continue;

            float angle = Vector3.Angle(ray.direction, toPickup);
            if (angle > pickupConeAngle) continue;

            // Weight angle more than distance so aiming beats proximity
            float score = angle * 2f + dist;
            if (score < bestScore) { bestScore = score; best = pickup; }
        }

        return best;
    }

    // ── Interact (E) ──────────────────────────────────────────────────────────

    private void TryInteract()
    {
        // Pickup is already highlighted — collect immediately
        if (_highlightedPickup != null)
        {
            TryPickup(_highlightedPickup);
            return;
        }

        // Screen-centre ray for station detection — same as pickup for consistency
        var cam = Camera.main;
        if (cam == null) return;

        var ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        var station = (ModificationStation)null;

        if (Physics.Raycast(ray, out var hit, interactionRange, interactionMask))
            station = hit.collider.GetComponent<ModificationStation>();

        if (station == null)
            station = FindClosestStation(ray.origin);

        if (station != null)
        {
            if (inventoryUI != null && inventoryUI.IsOpen) inventoryUI.Close();
            station.Interact();
        }
    }

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

        // Clear highlight before collecting so we don't try to hide a destroyed object
        _highlightedPickup = null;
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
        ClearHighlight();
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
