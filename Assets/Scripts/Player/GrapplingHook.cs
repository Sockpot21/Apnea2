// GrapplingHook.cs
// Attach to the Character GameObject (child of Player).
// Multi-segment rope: wraps around geometry as the player swings.
// PlayerCharacter calls ApplyGrappleVelocity() each UpdateVelocity when active.

using System.Collections.Generic;
using UnityEngine;

public class GrapplingHook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KinematicCharacterController.KinematicCharacterMotor motor;

    [Header("Settings")]
    [SerializeField] private float maxRange = 30f;
    [SerializeField] private float ascendSpeed = 12f;
    [SerializeField] private float descendSpeed = 6f;
    [SerializeField] private float swingInfluence = 0.35f;

    [Header("Rope Visual")]
    [SerializeField] private LineRenderer ropeRenderer;

    // ── Public properties ─────────────────────────────────────────────────────

    public bool IsGrappling => _isGrappling;
    public float AscendSpeed => ascendSpeed;
    public float DescendSpeed => descendSpeed;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private bool _isGrappling = false;
    private bool _isFixed = false;
    private Rigidbody _anchorRb = null;

    // Segment chain: [0] = player position (updated live),
    // [1..n-1] = wrap points, [n] = original anchor
    private List<Vector3> _segments = new List<Vector3>();

    // Active rope length = distance from player to first wrap/anchor
    private float _ropeLength = 0f;

    // The point the player is currently swinging around
    private Vector3 ActivePivot => _segments.Count >= 2
        ? _segments[_segments.Count - 1] : Vector3.zero;

    // ── Public API ────────────────────────────────────────────────────────────

    public void ToggleGrapple(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_isGrappling) { Release(); return; }
        Fire(rayOrigin, rayDirection);
    }

    public void AdjustRope(float delta)
    {
        if (!_isGrappling) return;
        _ropeLength = Mathf.Max(0.5f, _ropeLength + delta);
    }

    public void Release()
    {
        _isGrappling = false;
        _isFixed = false;
        _anchorRb = null;
        _segments.Clear();

        if (ropeRenderer != null) ropeRenderer.enabled = false;
        Debug.Log("[Grapple] Released.");
    }

    // ── Fire ──────────────────────────────────────────────────────────────────

    private void Fire(Vector3 origin, Vector3 direction)
    {
        if (!Physics.Raycast(origin, direction, out var hit, maxRange)) return;

        _segments.Clear();
        _segments.Add(transform.position); // [0] player — updated each frame
        _segments.Add(hit.point);          // [1] anchor

        _ropeLength = Vector3.Distance(transform.position, hit.point);
        _isGrappling = true;

        var rb = hit.collider.attachedRigidbody;
        if (rb != null && !rb.isKinematic)
        {
            _anchorRb = rb;
            _isFixed = false;
            Debug.Log($"[Grapple] Attached to moveable: {hit.collider.name}");
        }
        else
        {
            _anchorRb = null;
            _isFixed = true;
            Debug.Log($"[Grapple] Attached to fixed: {hit.collider.name} at {hit.point}");
        }

        if (ropeRenderer != null) ropeRenderer.enabled = true;
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!_isGrappling) return;

        // Keep player segment up to date
        if (_segments.Count > 0)
            _segments[0] = transform.position;

        // Keep moveable anchor up to date
        if (_anchorRb != null && _segments.Count >= 2)
            _segments[_segments.Count - 1] = _anchorRb.position;

        ProcessRopeSegments();
        UpdateRopeVisual();
    }

    /// <summary>
    /// Checks each segment of the rope for new wrap points (geometry intersecting
    /// the rope) and removes wrap points the player has swung back past.
    /// </summary>
    private void ProcessRopeSegments()
    {
        if (_segments.Count < 2) return;

        // ── Add wrap points ────────────────────────────────────────────────────
        // Cast from each segment point toward the next; if blocked, insert a wrap point
        for (int i = 0; i < _segments.Count - 1; i++)
        {
            var from = _segments[i];
            var to = _segments[i + 1];
            var dir = to - from;
            var dist = dir.magnitude;

            if (dist < 0.05f) continue;

            if (Physics.Raycast(from, dir.normalized, out var hit, dist))
            {
                // Don't wrap on the character's own colliders
                if (hit.collider.transform.IsChildOf(transform)) continue;

                // Insert the wrap point between these two segments
                _segments.Insert(i + 1, hit.point);
                Debug.Log($"[Grapple] Rope wrapped around: {hit.collider.name}");

                // Update active rope length to new innermost segment
                _ropeLength = Vector3.Distance(_segments[0], _segments[1]);
                break; // process one wrap per frame to avoid runaway insertion
            }
        }

        // ── Remove stale wrap points ───────────────────────────────────────────
        // If the line of sight from the player to the second-to-last point is clear,
        // the last wrap point is no longer needed
        while (_segments.Count > 2)
        {
            var player = _segments[0];
            var wrapPoint = _segments[1];
            var nextPoint = _segments[2];

            // Compute the angle at the wrap point between the two adjacent segments
            var toPlayer = (player - wrapPoint).normalized;
            var toNext = (nextPoint - wrapPoint).normalized;
            var angle = Vector3.Angle(toPlayer, toNext);

            // If the player has swung past the wrap point (angle > 180 flattens),
            // or direct line of sight to next point is clear, remove the wrap point
            if (angle > 175f || !Physics.Linecast(player, nextPoint))
            {
                _segments.RemoveAt(1);
                _ropeLength = Vector3.Distance(_segments[0], _segments[1]);
            }
            else break;
        }
    }

    // ── Velocity applied to player ────────────────────────────────────────────

    public void ApplyGrappleVelocity(ref Vector3 currentVelocity, float deltaTime,
                                      Vector3 requestedMovement, float airAcceleration)
    {
        if (!_isGrappling || _segments.Count < 2) return;

        var playerPos = transform.position;
        var pivot = _segments[1]; // first wrap or anchor
        var toPlayer = playerPos - pivot;
        var dist = toPlayer.magnitude;

        if (dist < 0.01f) return;

        var ropeDir = toPlayer.normalized;

        // ── Constraint: stop rope extending beyond its length ─────────────────
        if (dist >= _ropeLength)
        {
            var outward = Vector3.Dot(currentVelocity, ropeDir);
            if (outward > 0f)
                currentVelocity -= ropeDir * outward;
        }

        // ── Gravity ───────────────────────────────────────────────────────────
        currentVelocity += Physics.gravity * deltaTime;

        // ── Light swing influence from WASD ───────────────────────────────────
        if (requestedMovement.sqrMagnitude > 0.01f)
        {
            var tangent = Vector3.ProjectOnPlane(requestedMovement, ropeDir).normalized;
            currentVelocity += tangent * airAcceleration * swingInfluence * deltaTime;
        }

        // ── Moveable object constraint ────────────────────────────────────────
        if (_anchorRb != null)
        {
            var objToPlayer = playerPos - _anchorRb.position;
            var objDist = objToPlayer.magnitude;
            if (objDist > _ropeLength)
            {
                var pullDir = objToPlayer.normalized;
                _anchorRb.linearVelocity += pullDir * (objDist - _ropeLength) / deltaTime * 0.5f;
            }
        }
    }

    // ── Rope visual ───────────────────────────────────────────────────────────

    private void UpdateRopeVisual()
    {
        if (ropeRenderer == null || _segments.Count < 2) return;

        ropeRenderer.positionCount = _segments.Count;
        for (int i = 0; i < _segments.Count; i++)
            ropeRenderer.SetPosition(i, _segments[i]);
    }
}
