// CameraFOV.cs
// Velocity-magnitude-driven FOV. Lerps between baseFOV and sprintFOV based on
// how fast the player is moving relative to configurable min/max speed thresholds.
// Uses separate response values for acceleration and deceleration so braking
// feels distinct from speeding up.
// Attach to the Camera GameObject. Drag Main Camera into Target Camera in the Inspector.

using UnityEngine;

public class CameraFOV : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the Main Camera here.")]
    [SerializeField] private Camera targetCamera;

    [Header("FOV Range")]
    [Tooltip("FOV at or below the low speed threshold.")]
    [SerializeField] private float baseFOV = 60f;

    [Tooltip("FOV at or above the high speed threshold.")]
    [SerializeField] private float sprintFOV = 80f;

    [Header("Speed Thresholds")]
    [Tooltip("Below this speed the FOV stays at baseFOV.")]
    [SerializeField] private float minSpeed = 15f;

    [Tooltip("At or above this speed the FOV reaches sprintFOV.")]
    [SerializeField] private float maxSpeed = 30f;

    [Header("Response")]
    [Tooltip("How quickly the FOV rises when speeding up. Higher = snappier.")]
    [SerializeField] private float fovAccelResponse = 4f;

    [Tooltip("How quickly the FOV drops when slowing down. Higher = snappier.")]
    [SerializeField] private float fovDecelResponse = 8f;

    private float _currentFOV;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        if (targetCamera == null)
            targetCamera = GetComponentInChildren<Camera>();
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
        {
            Debug.LogError("[CameraFOV] No Camera found. Drag Main Camera into the " +
                           "Target Camera field on CameraFOV.");
            return;
        }

        _currentFOV = baseFOV;
        targetCamera.fieldOfView = _currentFOV;
    }

    // ── Called from Player.LateUpdate() ──────────────────────────────────────

    public void UpdateFOV(float deltaTime, Stance stance, Vector3 velocity)
    {
        if (targetCamera == null) return;

        // Derive a 0-1 t value from total velocity magnitude
        var speed = velocity.magnitude;
        var t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);
        var target = Mathf.Lerp(baseFOV, sprintFOV, t);

        // Use faster response when FOV is rising, slower when falling
        var response = target > _currentFOV ? fovAccelResponse : fovDecelResponse;

        _currentFOV = Mathf.Lerp
            (
                a: _currentFOV,
                b: target,
                t: 1f - Mathf.Exp(-response * deltaTime)
            );

        targetCamera.fieldOfView = _currentFOV;
    }
}
