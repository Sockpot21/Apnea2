// CameraFOV.cs
// Velocity-driven FOV with aim override.
// Attach to the Camera GameObject. Drag Main Camera into Target Camera.
// Call Initialize() in Player.Start() and UpdateFOV() in Player.LateUpdate().

using UnityEngine;

public class CameraFOV : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the Main Camera here.")]
    [SerializeField] private Camera targetCamera;

    [Header("FOV Range")]
    [SerializeField] private float baseFOV = 60f;
    [SerializeField] private float sprintFOV = 80f;

    [Header("Speed Thresholds")]
    [SerializeField] private float minSpeed = 15f;
    [SerializeField] private float maxSpeed = 30f;

    [Header("Response")]
    [Tooltip("How quickly FOV rises when speeding up.")]
    [SerializeField] private float fovAccelResponse = 4f;
    [Tooltip("How quickly FOV drops when slowing down.")]
    [SerializeField] private float fovDecelResponse = 8f;

    [Header("Aim")]
    [Tooltip("How quickly FOV transitions when entering/leaving aim.")]
    [SerializeField] private float aimResponse = 10f;

    private float _currentFOV;
    private float _targetFOV;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>();
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            Debug.LogError("[CameraFOV] No Camera found. Drag Main Camera into Target Camera.");
            return;
        }

        _currentFOV = baseFOV;
        _targetFOV = baseFOV;
        targetCamera.fieldOfView = _currentFOV;
    }

    // ── Called from Player.LateUpdate() ──────────────────────────────────────

    /// <summary>
    /// Pass isAiming and aimFOV from PlayerEquipment.
    /// When aiming with a ranged weapon the aim FOV overrides velocity-driven FOV.
    /// </summary>
    public void UpdateFOV(float deltaTime, Stance stance, Vector3 velocity,
                          bool isAiming, float aimFOV)
    {
        if (targetCamera == null) return;

        if (isAiming)
        {
            // Aim overrides everything — lerp quickly to weapon's aimFOV
            _currentFOV = Mathf.Lerp(_currentFOV, aimFOV,
                1f - Mathf.Exp(-aimResponse * deltaTime));
        }
        else
        {
            // Velocity-driven FOV
            var speed = velocity.magnitude;
            var t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
            _targetFOV = Mathf.Lerp(baseFOV, sprintFOV, t);
            var response = _targetFOV > _currentFOV ? fovAccelResponse : fovDecelResponse;
            _currentFOV = Mathf.Lerp(_currentFOV, _targetFOV,
                1f - Mathf.Exp(-response * deltaTime));
        }

        targetCamera.fieldOfView = _currentFOV;
    }
}
