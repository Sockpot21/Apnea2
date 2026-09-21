// CameraFOV.cs
// Layered movement FOV: directional speed, grapple tension/reel, and a short
// release pulse. Aim remains an authoritative override.

using UnityEngine;

public class CameraFOV : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag the Main Camera here.")]
    [SerializeField] private Camera targetCamera;

    [Header("FOV Range")]
    [SerializeField] private float baseFOV = 60f;
    [Tooltip("Legacy scene value. The locomotion contribution is limited below.")]
    [SerializeField] private float sprintFOV = 82f;
    [SerializeField, Min(1f)] private float speedFOVLimit = 82f;
    [SerializeField, Min(1f)] private float absoluteMaxFOV = 92f;

    [Header("Speed Thresholds")]
    [SerializeField] private float minSpeed = 15f;
    [SerializeField] private float maxSpeed = 30f;
    [Tooltip("How much sideways/vertical velocity contributes compared with camera-forward speed.")]
    [SerializeField, Range(0f, 1f)] private float offAxisSpeedWeight = 0.35f;

    [Header("Grapple")]
    [SerializeField, Min(0f)] private float attachedFOVBoost = 2.5f;
    [SerializeField, Min(0f)] private float reelFOVBoost = 4.5f;
    [SerializeField, Min(0f)] private float tensionFOVBoost = 3f;
    [SerializeField, Min(0f)] private float releaseFOVBoost = 6f;
    [SerializeField, Min(0.01f)] private float releasePulseDuration = 0.32f;

    [Header("Response")]
    [Tooltip("How quickly FOV rises when speeding up.")]
    [SerializeField] private float fovAccelResponse = 10f;
    [Tooltip("How quickly FOV settles after slowing down.")]
    [SerializeField] private float fovDecelResponse = 4f;

    [Header("Aim")]
    [Tooltip("How quickly FOV transitions when entering/leaving aim.")]
    [SerializeField] private float aimResponse = 10f;

    private float _currentFOV;
    private float _targetFOV;
    private float _releasePulse;

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

    public void UpdateFOV(float deltaTime, Stance stance, Vector3 velocity,
        bool isAiming, float aimFOV, GrapplingHook grapple)
    {
        if (targetCamera == null) return;

        if (grapple != null)
            _releasePulse = Mathf.Max(_releasePulse, grapple.ConsumeReleasePulse());

        if (isAiming)
        {
            _currentFOV = Mathf.Lerp(_currentFOV, aimFOV,
                1f - Mathf.Exp(-aimResponse * deltaTime));
        }
        else
        {
            float speed = velocity.magnitude;
            float forwardSpeed = Mathf.Abs(Vector3.Dot(velocity, targetCamera.transform.forward));
            float perceivedSpeed = Mathf.Lerp(speed * offAxisSpeedWeight, speed,
                speed > 0.001f ? forwardSpeed / speed : 0f);
            float speedT = Mathf.InverseLerp(minSpeed, maxSpeed, perceivedSpeed);
            float speedFOV = Mathf.Lerp(baseFOV,
                Mathf.Min(sprintFOV, speedFOVLimit), speedT);

            float grappleBoost = 0f;
            if (grapple != null && grapple.IsGrappling)
            {
                grappleBoost += attachedFOVBoost;
                grappleBoost += tensionFOVBoost * grapple.Tension01;
                if (grapple.IsReeling) grappleBoost += reelFOVBoost;
            }

            grappleBoost += releaseFOVBoost * _releasePulse;
            _targetFOV = Mathf.Min(absoluteMaxFOV, speedFOV + grappleBoost);
            float response = _targetFOV > _currentFOV
                ? fovAccelResponse
                : fovDecelResponse;
            _currentFOV = Mathf.Lerp(_currentFOV, _targetFOV,
                1f - Mathf.Exp(-response * deltaTime));
        }

        _releasePulse = Mathf.MoveTowards(_releasePulse, 0f,
            deltaTime / Mathf.Max(releasePulseDuration, Mathf.Epsilon));
        targetCamera.fieldOfView = _currentFOV;
    }
}
