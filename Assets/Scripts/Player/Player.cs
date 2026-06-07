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

    private void OnDestroy()
    {
        _inputActions.Dispose();
    }

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
                          ? CrouchInput.Toggle
                          : CrouchInput.None,
            Sprint = input.Sprint.IsPressed()
        };
        playerCharacter.UpdateInput(characterInput);
        playerCharacter.UpdateBody(deltaTime);

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

        // Pass aim state from PlayerEquipment into CameraFOV
        if (playerEquipment == null)
        {
            Debug.LogError("[Player] PlayerEquipment is not assigned in the Inspector!");
            cameraFOV.UpdateFOV(deltaTime, state.Stance, state.Velocity, false, 45f);
        }
        else
        {
            cameraFOV.UpdateFOV(deltaTime, state.Stance, state.Velocity,
                                playerEquipment.IsAiming, playerEquipment.AimFOV);
        }

        stanceVignette.UpdateVignette(deltaTime, state.Stance);
    }

    public void Teleport(Vector3 position)
    {
        playerCharacter.SetPosition(position);
    }
}
