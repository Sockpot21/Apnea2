// Player.cs
// ── New Input Actions required in Gameplay action map ─────────────────────────
//   Prone         → Z              (Button)
//   Grapple       → F              (Button)
//   GrappleAscend → Forward [Mouse](Button) — extra side button
//   GrappleDescend→ Back [Mouse]   (Button) — extra side button
// Regenerate C# class after adding these.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class Player : MonoBehaviour
{
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private PlayerCamera playerCamera;
    [SerializeField] private CameraSpring cameraSpring;
    [SerializeField] private CameraLean cameraLean;
    [SerializeField] private CameraFOV cameraFOV;
    [SerializeField] private Volume volume;
    [SerializeField] private StanceVinette stanceVignette;
    [SerializeField] private HealthManager healthManager;
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private GrapplingHook grapplingHook;

    private PlayerInput _inputActions;

    void Start()
    {
        _inputActions = new PlayerInput();
        _inputActions.Enable();
        GetComponent<PlayerInteraction>()?.SetInputActions(_inputActions);
        playerCharacter.Initialize();
        playerCamera.Initialize(playerCharacter.GetCameraTarget());
        cameraSpring.Initialize();
        cameraLean.Initialize();
        cameraFOV.Initialize();
        stanceVignette.Initialize(volume.profile);
    }

    private void OnDestroy() => _inputActions.Dispose();

    private void Update()
    {
        if (!GetComponent<PlayerInteraction>()._uiOpen)
            Cursor.lockState = CursorLockMode.Locked;

        var input = _inputActions.Gameplay;
        var deltaTime = Time.deltaTime;

        var cameraInput = new CameraInput { Look = input.Look.ReadValue<Vector2>() };
        playerCamera.UpdateRotation(cameraInput);

        var characterInput = new CharacterInput
        {
            Rotation = playerCamera.transform.rotation,
            Move = input.Move.ReadValue<Vector2>(),
            Jump = input.Jump.WasPressedThisFrame(),
            JumpSustain = input.Jump.IsPressed(),
            Crouch = input.Crouch.WasPressedThisFrame()
                          ? CrouchInput.Toggle : CrouchInput.None,
            Sprint = input.Sprint.IsPressed(),
            Prone = input.Prone.WasPressedThisFrame()
        };
        playerCharacter.UpdateInput(characterInput);
        playerCharacter.UpdateBody(deltaTime);

        // ── Grapple inputs ────────────────────────────────────────────────────
        if (grapplingHook != null)
        {
            if (input.Grapple.WasPressedThisFrame())
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var ray = cam.ScreenPointToRay(
                        new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
                    grapplingHook.ToggleGrapple(ray.origin, ray.direction);
                }
            }

            if (input.GrappleAscend.IsPressed())
                grapplingHook.Ascend(deltaTime);
            else
                grapplingHook.StopAdjust();
        }

#if UNITY_EDITOR
        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (Physics.Raycast(ray, out var hit))
                Teleport(hit.point);
        }
#endif
    }

    private void LateUpdate()
    {
        var deltaTime = Time.deltaTime;
        var cameraTarget = playerCharacter.GetCameraTarget();
        var state = playerCharacter.GetState();

        playerCamera.UpdatePosition(cameraTarget);
        cameraSpring.UpdateSpring(deltaTime, cameraTarget.up);

        cameraLean.UpdateLean(deltaTime, state.Stance is Stance.Slide,
            state.Acceloration, cameraTarget.up);
        cameraLean.UpdateWallRunTilt(deltaTime, state.IsWallRunning && !state.IsVerticalWallRunning,
            state.WallNormal, cameraTarget.up);

        if (playerEquipment == null)
        {
            Debug.LogError("[Player] PlayerEquipment not assigned!");
            cameraFOV.UpdateFOV(deltaTime, state.Stance, state.Velocity, false, 45f);
        }
        else
        {
            cameraFOV.UpdateFOV(deltaTime, state.Stance, state.Velocity,
                playerEquipment.IsAiming, playerEquipment.AimFOV);
        }

        stanceVignette.UpdateVignette(deltaTime, state.Stance);
    }

    public void Teleport(Vector3 position) => playerCharacter.SetPosition(position);
}
