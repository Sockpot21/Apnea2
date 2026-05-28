// PlayerInteraction.cs
// Handles E key interaction raycast and action map switching.
// Attach to the same GameObject as Player.cs.
// Player.cs needs one small addition — see the comment at the bottom of this file.
//
// ── Input Asset setup you need to do ─────────────────────────────────────────
// 1. Open Assets > Input > PlayerInput in the Input Action Editor.
// 2. Select the "UI" action map.
// 3. Add one action: name it "Close", binding: Escape key.
//    (The UI action map already exists — just needs this binding so the player
//     can close the station UI with Escape as a fallback.)
// 4. Save the asset and regenerate the C# class
//    (select the asset → Inspector → Generate C# Class → hit the button).
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerCharacter    playerCharacter;
    [SerializeField] private PlayerCamera       playerCamera;    // used for raycast origin
    [SerializeField] private ModificationStationUI stationUI;

    [Header("Interaction")]
    [SerializeField] private float interactionRange = 2.5f;
    [SerializeField] private LayerMask interactionMask = ~0; // everything by default

    // Set by Player.cs via SetInputActions()
    private PlayerInput _inputActions;
    public bool        _uiOpen = false;

    // ── Called by Player.cs after it creates _inputActions ───────────────────

    /// <summary>
    /// Call this from Player.Start() after creating and enabling _inputActions.
    /// Example:
    ///     GetComponent<PlayerInteraction>()?.SetInputActions(_inputActions);
    /// </summary>
    public void SetInputActions(PlayerInput inputActions)
    {
        _inputActions = inputActions;

        // Subscribe to UI Close action (Escape) to allow closing via keyboard
        _inputActions.UI.Close.performed += _ => TryCloseUI();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (stationUI != null)
        {
            stationUI.OnUIOpened += HandleUIOpened;
            stationUI.OnUIClosed += HandleUIClosed;
        }
    }

    private void OnDisable()
    {
        if (stationUI != null)
        {
            stationUI.OnUIOpened -= HandleUIOpened;
            stationUI.OnUIClosed -= HandleUIClosed;
        }
    }

    private void Update()
    {
        if (_inputActions == null) return;

        //Debug.Log($"Interact pressed: {_inputActions.Gameplay.Interact.WasPressedThisFrame()}");

        if (!_uiOpen && _inputActions.Gameplay.Interact.WasPressedThisFrame())
            TryInteract();
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    private void TryInteract()
    {
        var camTransform = Camera.main.transform;

        var station = FindObjectOfType<ModificationStation>();
        if (station == null) return;

        float dist = Vector3.Distance(camTransform.position, station.transform.position);
        if (dist > interactionRange)
        {
            Debug.Log($"[Interact] Too far: {dist}");
            return;
        }

        station.Interact();

        _uiOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _inputActions.Gameplay.Disable();
    }

    private void TryCloseUI()
    {
        if (_uiOpen && stationUI != null && stationUI.IsOpen)
            stationUI.Close();
    }

    // ── Action map switching ──────────────────────────────────────────────────

    private void HandleUIOpened()
    {
        _uiOpen = true;

        // Disable gameplay input, enable UI input
        _inputActions.Gameplay.Disable();
        _inputActions.UI.Enable();

        // Unlock cursor for UI navigation
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void HandleUIClosed()
    {
        _uiOpen = false;

        // Re-enable gameplay input, disable UI input
        _inputActions.UI.Disable();
        _inputActions.Gameplay.Enable();

        // Re-lock cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }
}


// ═════════════════════════════════════════════════════════════════════════════
// CHANGES NEEDED IN Player.cs
// Add these two lines to the Start() method, after _inputActions.Enable():
//
//     GetComponent<PlayerInteraction>()?.SetInputActions(_inputActions);
//
// That's the only change. Everything else in Player.cs stays identical.
// ═════════════════════════════════════════════════════════════════════════════
