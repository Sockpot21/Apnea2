// GrapplingHook.cs
// Attach to the Character child GameObject.
// Single-point grapple. Straight rope. Pendulum swing.
// Ascend applies velocity ONLY on frames the button is held — zero residual force
// the instant it's released. Pulls moveable Rigidbody targets toward the player;
// pulls the player toward fixed targets.

using UnityEngine;

public class GrapplingHook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KinematicCharacterController.KinematicCharacterMotor motor;

    [Header("Grapple")]
    [SerializeField] private float maxRange = 30f;
    [SerializeField] private float maxSwingSpeed = 25f;
    [SerializeField] private float swingInfluence = 0.4f;

    [Header("Ascend / Pull")]
    [SerializeField] private float ascendMaxSpeed = 12f;
    [SerializeField] private float adjustRampTime = 0.3f;
    [SerializeField] private float pullObjectSpeed = 14f;

    [Header("Visual")]
    [SerializeField] private LineRenderer ropeRenderer;

    // ── Public ────────────────────────────────────────────────────────────────

    public bool IsGrappling => _isGrappling;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private bool _isGrappling = false;
    private Vector3 _anchor = Vector3.zero;
    private Rigidbody _anchorRb = null;
    private float _ropeLength = 0f;

    // Ascend state — read by ApplyGrappleVelocity. Reset to 0 every frame
    // BEFORE Ascend() is called; only set back to non-zero if Ascend() runs
    // this frame. This guarantees zero residual the instant the button releases.
    private float _ascendTimer = 0f;
    private float _currentAscendSpeed = 0f; // 0 unless Ascend() called THIS frame

    // ── Public API ────────────────────────────────────────────────────────────

    public void ToggleGrapple(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_isGrappling) { Release(); return; }
        Fire(rayOrigin, rayDirection);
    }

    /// <summary>
    /// Call once per frame BEFORE UpdateVelocity runs, ONLY while the ascend
    /// button is held. If not called this frame, ascend speed is implicitly zero
    /// (see PreUpdateResetAscend, called every frame regardless).
    /// </summary>
    public void Ascend(float deltaTime)
    {
        if (!_isGrappling) return;

        _ascendTimer = Mathf.Min(_ascendTimer + deltaTime, adjustRampTime);
        float t = _ascendTimer / adjustRampTime;
        _currentAscendSpeed = ascendMaxSpeed * t;

        if (_anchorRb != null)
        {
            var dir = (transform.position - _anchorRb.position).normalized;
            _anchorRb.linearVelocity = dir * (pullObjectSpeed * t);
        }
    }

    /// <summary>
    /// Call once per frame when the ascend button is NOT held.
    /// Resets ramp and guarantees zero ascend velocity this frame.
    /// </summary>
    public void StopAdjust()
    {
        _ascendTimer = 0f;
        _currentAscendSpeed = 0f;

        if (_anchorRb != null)
            _anchorRb.linearVelocity = Vector3.zero;
    }

    public void Release()
    {
        _isGrappling = false;
        _anchorRb = null;
        StopAdjust();
        if (ropeRenderer != null) ropeRenderer.enabled = false;
        Debug.Log("[Grapple] Released.");
    }

    // ── Fire ──────────────────────────────────────────────────────────────────

    private void Fire(Vector3 origin, Vector3 direction)
    {
        if (!Physics.Raycast(origin, direction, out var hit, maxRange))
        {
            Debug.Log("[Grapple] Fire missed — nothing in range.");
            return;
        }

        _anchor = hit.point;
        _ropeLength = Vector3.Distance(transform.position, _anchor);
        _isGrappling = true;
        StopAdjust();

        var rb = hit.collider.attachedRigidbody;
        _anchorRb = (rb != null && !rb.isKinematic) ? rb : null;

        Debug.Log($"[Grapple] Fired → {hit.collider.name} | len:{_ropeLength:F1} | " +
                  $"moveable:{_anchorRb != null}");

        if (ropeRenderer != null)
        {
            ropeRenderer.positionCount = 2;
            ropeRenderer.enabled = true;
        }
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_isGrappling || ropeRenderer == null) return;

        var anchor = _anchorRb != null ? _anchorRb.position : _anchor;
        ropeRenderer.SetPosition(0, transform.position);
        ropeRenderer.SetPosition(1, anchor);
    }

    // ── Velocity applied to player ────────────────────────────────────────────

    /// <summary>
    /// Call from PlayerCharacter.UpdateVelocity every frame while IsGrappling.
    /// </summary>
    public void ApplyGrappleVelocity(ref Vector3 currentVelocity, float deltaTime,
                                      Vector3 requestedMovement, float airAcceleration)
    {
        if (!_isGrappling) return;

        var anchor = _anchorRb != null ? _anchorRb.position : _anchor;
        var toPlayer = transform.position - anchor;
        float dist = toPlayer.magnitude;
        if (dist < 0.01f) return;

        var ropeDir = toPlayer.normalized; // anchor → player

        // ── Ascend override — ONLY non-zero on frames Ascend() was called ─────
        // This is applied BEFORE the pendulum logic and directly sets the
        // rope-direction component of velocity. When _currentAscendSpeed is 0
        // (button not held this frame), this line does nothing at all.
        if (_currentAscendSpeed > 0f && _anchorRb == null)
        {
            // Remove existing rope-direction velocity, replace with ascend speed
            float existing = Vector3.Dot(currentVelocity, ropeDir);
            currentVelocity -= ropeDir * existing;
            currentVelocity -= ropeDir * _currentAscendSpeed; // move toward anchor

            // Shrink rope length to match so taut constraint doesn't fight this
            _ropeLength = Mathf.Max(1f, dist - _currentAscendSpeed * deltaTime);
        }

        // ── Taut constraint ───────────────────────────────────────────────────
        float err = dist - _ropeLength;
        if (Mathf.Abs(err) > 0.01f)
            currentVelocity -= ropeDir * (err / deltaTime) * 0.5f;

        // ── Pendulum: remove outward velocity ─────────────────────────────────
        float outward = Vector3.Dot(currentVelocity, ropeDir);
        if (outward > 0f) currentVelocity -= ropeDir * outward;

        // ── Gravity ───────────────────────────────────────────────────────────
        currentVelocity += Physics.gravity * deltaTime;

        // ── WASD swing influence ──────────────────────────────────────────────
        if (requestedMovement.sqrMagnitude > 0.01f)
        {
            var tangent = Vector3.ProjectOnPlane(requestedMovement, ropeDir).normalized;
            currentVelocity += tangent * airAcceleration * swingInfluence * deltaTime;
        }

        // ── Speed cap ─────────────────────────────────────────────────────────
        currentVelocity = Vector3.ClampMagnitude(currentVelocity, maxSwingSpeed);

        // Reset ascend speed AFTER use — must be called again next frame to persist
        _currentAscendSpeed = 0f;
    }
}
