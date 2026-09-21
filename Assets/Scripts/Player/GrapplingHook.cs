// GrapplingHook.cs
// Momentum-preserving single-point grapple for the Kinematic Character Controller.
// The rope constrains only outward radial motion; gravity, steering, and earned
// tangential speed remain available for a pendulum swing and release launch.

using UnityEngine;

public class GrapplingHook : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KinematicCharacterController.KinematicCharacterMotor motor;
    [Tooltip("Optional hand/muzzle transform used as the visible rope origin.")]
    [SerializeField] private Transform ropeOrigin;

    [Header("Targeting")]
    [SerializeField, Min(1f)] private float maxRange = 30f;
    [SerializeField] private LayerMask grappleLayers = ~0;

    [Header("Swing")]
    [Tooltip("Speed above which gentle drag begins. This is not a hard ceiling.")]
    [SerializeField, Min(1f)] private float maxSwingSpeed = 55f;
    [SerializeField, Min(0f)] private float hardSafetySpeed = 85f;
    [SerializeField, Min(0f)] private float overspeedDrag = 0.35f;
    [SerializeField, Range(0f, 1f)] private float swingInfluence = 0.2f;
    [SerializeField, Range(0f, 1f)] private float constraintCorrection = 0.18f;
    [SerializeField, Min(0f)] private float maxConstraintCorrectionSpeed = 24f;
    [SerializeField, Min(0f)] private float ropeTolerance = 0.04f;

    [Header("Reel / Pull")]
    [SerializeField, Min(0f)] private float reelSpeed = 24f;
    [SerializeField, Min(0f)] private float descendSpeed = 18f;
    [SerializeField, Min(0f)] private float reelTargetSpeed = 38f;
    [SerializeField, Min(0f)] private float reelAcceleration = 105f;
    [SerializeField, Min(0.25f)] private float minRopeLength = 1.5f;
    [SerializeField, Min(0f)] private float pullObjectSpeed = 14f;
    [SerializeField, Min(0f)] private float pullObjectAcceleration = 55f;
    [SerializeField, Min(0f), Tooltip("Bodies at or above this mass behave as moving anchors.")]
    private float heavyAnchorMass = 20f;

    [Header("Release Launch")]
    [SerializeField, Min(0f)] private float releaseBoost = 7f;
    [SerializeField, Min(0f)] private float releaseBoostMinSpeed = 28f;
    [SerializeField, Min(0f)] private float releaseBoostFullSpeed = 58f;

    [Header("Visual")]
    [SerializeField] private LineRenderer ropeRenderer;
    [SerializeField, Min(0.001f)] private float ropeWidth = 0.035f;
    [SerializeField] private Color ropeColor = new(0.05f, 0.05f, 0.05f, 1f);
    [SerializeField, Range(2, 24)] private int ropeSegments = 10;
    [SerializeField, Min(0f)] private float slackSagMultiplier = 0.35f;
    [SerializeField, Min(0f)] private float maxSlackSag = 1.25f;

    public bool IsGrappling => _isGrappling;
    public bool IsReeling => _isGrappling && _reelInput > 0f;
    public float Tension01 => _tension01;

    private bool _isGrappling;
    private Vector3 _anchor;
    private Rigidbody _anchorRb;
    private Vector3 _anchorLocalPoint;
    private float _ropeLength;
    private float _reelInput;
    private float _tension01;
    private float _releasePulse;

    private Vector3 AnchorPosition => _anchorRb != null
        ? _anchorRb.transform.TransformPoint(_anchorLocalPoint)
        : _anchor;

    private bool PullsPlayer => _anchorRb == null || _anchorRb.mass >= heavyAnchorMass;

    private void Awake()
    {
        if (ropeRenderer == null) return;

        if (ropeRenderer.sharedMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (shader != null) ropeRenderer.material = new Material(shader);
        }

        ropeRenderer.widthMultiplier = ropeWidth;
        ropeRenderer.startWidth = ropeWidth;
        ropeRenderer.endWidth = ropeWidth * 0.65f;
        ropeRenderer.startColor = ropeColor;
        ropeRenderer.endColor = ropeColor;
        if (ropeRenderer.material != null) ropeRenderer.material.color = ropeColor;
        ropeRenderer.numCapVertices = 4;
        ropeRenderer.positionCount = Mathf.Max(2, ropeSegments);
        ropeRenderer.enabled = false;
    }

    public void ToggleGrapple(Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (_isGrappling)
        {
            Release();
            return;
        }

        Fire(rayOrigin, rayDirection);
    }

    /// <summary>Set every render frame. Positive reels in; negative pays rope out.</summary>
    public void SetReelInput(float input)
    {
        _reelInput = _isGrappling ? Mathf.Clamp(input, -1f, 1f) : 0f;
    }

    public float ConsumeReleasePulse()
    {
        float pulse = _releasePulse;
        _releasePulse = 0f;
        return pulse;
    }

    public void Release()
    {
        if (!_isGrappling) return;

        if (PullsPlayer && motor != null)
        {
            Vector3 velocity = motor.BaseVelocity;
            Vector3 fromAnchor = transform.position - AnchorPosition;
            if (fromAnchor.sqrMagnitude > 0.0001f)
            {
                Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity, fromAnchor.normalized);
                float tangentSpeed = tangentVelocity.magnitude;
                float launchStrength = Mathf.InverseLerp(
                    releaseBoostMinSpeed, releaseBoostFullSpeed, tangentSpeed);

                if (launchStrength > 0f && tangentSpeed > 0.01f)
                {
                    velocity += tangentVelocity.normalized * (releaseBoost * launchStrength);
                    velocity = Vector3.ClampMagnitude(velocity, hardSafetySpeed);
                    motor.BaseVelocity = velocity;
                    motor.ForceUnground(0.05f);
                }

                _releasePulse = Mathf.Max(_releasePulse, launchStrength);
            }
        }

        _isGrappling = false;
        _anchorRb = null;
        _reelInput = 0f;
        _tension01 = 0f;
        if (ropeRenderer != null) ropeRenderer.enabled = false;
    }

    private void Fire(Vector3 origin, Vector3 direction)
    {
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, maxRange,
            grappleLayers, QueryTriggerInteraction.Ignore))
            return;

        bool isPlayer = hit.collider.CompareTag("Player")
            || hit.collider.transform.root == transform.root
            || hit.collider.GetComponentInParent<PlayerCharacter>() != null;
        if (isPlayer) return;

        _anchor = hit.point;
        _anchorRb = hit.collider.attachedRigidbody;
        if (_anchorRb != null && _anchorRb.isKinematic) _anchorRb = null;
        _anchorLocalPoint = _anchorRb != null
            ? _anchorRb.transform.InverseTransformPoint(hit.point)
            : Vector3.zero;

        _ropeLength = Vector3.Distance(transform.position, hit.point);
        _reelInput = 0f;
        _tension01 = 0f;
        _isGrappling = true;

        if (ropeRenderer != null)
        {
            ropeRenderer.positionCount = Mathf.Max(2, ropeSegments);
            ropeRenderer.enabled = true;
        }
    }

    private void LateUpdate()
    {
        if (!_isGrappling || ropeRenderer == null) return;

        Vector3 start = ropeOrigin != null ? ropeOrigin.position : transform.position;
        Vector3 end = AnchorPosition;
        float visibleDistance = Vector3.Distance(start, end);
        float slack = Mathf.Max(0f, _ropeLength - visibleDistance);
        float sag = Mathf.Min(maxSlackSag, slack * slackSagMultiplier);
        Vector3 down = motor != null ? -motor.CharacterUp : Vector3.down;
        int segments = Mathf.Max(2, ropeSegments);

        if (ropeRenderer.positionCount != segments) ropeRenderer.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float t = (float)i / (segments - 1);
            Vector3 point = Vector3.Lerp(start, end, t)
                + down * (Mathf.Sin(t * Mathf.PI) * sag);
            ropeRenderer.SetPosition(i, point);
        }

        float width = Mathf.Lerp(ropeWidth * 0.82f, ropeWidth * 1.12f, _tension01);
        ropeRenderer.startWidth = width;
        ropeRenderer.endWidth = width * 0.65f;
    }

    /// <summary>Called by PlayerCharacter after its ordinary airborne movement.</summary>
    public void ApplyGrappleVelocity(ref Vector3 currentVelocity, float deltaTime,
        Vector3 requestedMovement, float airAcceleration)
    {
        if (!_isGrappling || deltaTime <= Mathf.Epsilon) return;

        Vector3 anchor = AnchorPosition;
        Vector3 anchorToPlayer = transform.position - anchor;
        float distance = anchorToPlayer.magnitude;
        if (distance < 0.01f) return;

        if (!PullsPlayer)
        {
            PullLightObject(anchor, deltaTime);
            _tension01 = _reelInput > 0f ? 0.65f : 0f;
            return;
        }

        Vector3 ropeDirection = anchorToPlayer / distance; // anchor -> player
        Vector3 towardAnchor = -ropeDirection;

        if (_reelInput > 0f)
        {
            _ropeLength = Mathf.Max(minRopeLength, _ropeLength - reelSpeed * _reelInput * deltaTime);

            float inwardSpeed = Vector3.Dot(currentVelocity, towardAnchor);
            float wantedInwardSpeed = reelTargetSpeed * _reelInput;
            float speedGain = Mathf.Clamp(wantedInwardSpeed - inwardSpeed,
                0f, reelAcceleration * deltaTime);
            currentVelocity += towardAnchor * speedGain;

            if (motor != null && Vector3.Dot(towardAnchor, motor.CharacterUp) > 0.12f)
                motor.ForceUnground(0.05f);
        }
        else if (_reelInput < 0f)
        {
            _ropeLength = Mathf.Min(maxRange,
                _ropeLength + descendSpeed * -_reelInput * deltaTime);
        }

        float stretch = distance - _ropeLength;
        float outwardSpeed = Vector3.Dot(currentVelocity, ropeDirection);
        bool taut = stretch >= -ropeTolerance;

        // A rope may stop motion away from its anchor, but never consumes the
        // tangential velocity that makes a deep swing pay off.
        if (taut && outwardSpeed > 0f)
            currentVelocity -= ropeDirection * outwardSpeed;

        float correctionSpeed = 0f;
        if (stretch > 0f)
        {
            correctionSpeed = Mathf.Min(maxConstraintCorrectionSpeed,
                stretch * constraintCorrection / deltaTime);
            currentVelocity += towardAnchor * correctionSpeed;
        }

        if (requestedMovement.sqrMagnitude > 0.001f)
        {
            Vector3 tangentInput = Vector3.ProjectOnPlane(requestedMovement, ropeDirection);
            if (tangentInput.sqrMagnitude > 0.001f)
            {
                float inputMagnitude = Mathf.Clamp01(requestedMovement.magnitude);
                currentVelocity += tangentInput.normalized
                    * (airAcceleration * swingInfluence * inputMagnitude * deltaTime);
            }
        }

        ApplyTangentialOverspeedDrag(ref currentVelocity, ropeDirection, deltaTime);
        currentVelocity = Vector3.ClampMagnitude(currentVelocity, hardSafetySpeed);

        float load = Mathf.Max(Mathf.Max(0f, outwardSpeed), correctionSpeed);
        float baseTension = taut ? 0.15f : 0f;
        _tension01 = Mathf.Max(baseTension, Mathf.InverseLerp(0f, 32f, load));
        if (_reelInput > 0f) _tension01 = Mathf.Max(_tension01, 0.45f * _reelInput);
    }

    private void PullLightObject(Vector3 anchor, float deltaTime)
    {
        if (_anchorRb == null || _reelInput <= 0f) return;

        Vector3 toPlayer = transform.position - anchor;
        if (toPlayer.sqrMagnitude < 0.001f) return;

        Vector3 direction = toPlayer.normalized;
        float currentTowardSpeed = Vector3.Dot(_anchorRb.linearVelocity, direction);
        float velocityGain = Mathf.Clamp(pullObjectSpeed - currentTowardSpeed,
            0f, pullObjectAcceleration * deltaTime);
        _anchorRb.AddForce(direction * velocityGain, ForceMode.VelocityChange);
    }

    private void ApplyTangentialOverspeedDrag(ref Vector3 velocity,
        Vector3 ropeDirection, float deltaTime)
    {
        Vector3 radial = Vector3.Project(velocity, ropeDirection);
        Vector3 tangent = velocity - radial;
        float tangentSpeed = tangent.magnitude;
        if (tangentSpeed <= maxSwingSpeed || tangentSpeed <= 0.001f) return;

        float excess = tangentSpeed - maxSwingSpeed;
        float reducedSpeed = tangentSpeed - excess
            * (1f - Mathf.Exp(-overspeedDrag * deltaTime));
        velocity = radial + tangent * (reducedSpeed / tangentSpeed);
    }
}
