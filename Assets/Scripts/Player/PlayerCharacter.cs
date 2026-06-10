using System.Collections;
using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;

public enum CrouchInput { None, Toggle }

public enum Stance
{
    Stand, Crouch, Slide, Sprint, WallRun, Prone
}

public struct CharacterInput
{
    public Quaternion Rotation;
    public Vector2 Move;
    public bool Jump;
    public bool JumpSustain;
    public CrouchInput Crouch;
    public bool Sprint;
    public bool Prone;
}

public struct CharacterState
{
    public bool Grounded;
    public Stance Stance;
    public Vector3 Velocity;
    public Vector3 Acceloration;

    // Wall run info exposed for camera tilt
    public bool IsWallRunning;
    public Vector3 WallNormal;
}

public class PlayerCharacter : MonoBehaviour, ICharacterController
{
    [SerializeField] private KinematicCharacterMotor motor;
    [SerializeField] private Transform root;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private GrapplingHook grapplingHook; // optional — assign if grapple enabled

    // ── Ground Movement ───────────────────────────────────────────────────────
    [Header("Ground Movement")]
    [SerializeField] private float walkSpeed = 20f;
    [SerializeField] private float crouchSpeed = 7f;
    [SerializeField] private float walkResponse = 25f;
    [SerializeField] private float crouchResponse = 20f;

    // ── Sprint ────────────────────────────────────────────────────────────────
    [Header("Sprint")]
    [SerializeField] private bool sprintEnabled = true;
    [SerializeField] private float sprintSpeed = 30f;
    [SerializeField] private float sprintResponse = 20f;

    // ── Jump ──────────────────────────────────────────────────────────────────
    [Header("Jump")]
    [SerializeField] private float jumpSpeed = 20f;
    [SerializeField] private float coyoteTime = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float jumpSustainGravity = 0.4f;
    [SerializeField] private float gravity = -90f;

    // ── Double Jump ───────────────────────────────────────────────────────────
    [Header("Double Jump")]
    [SerializeField] private bool doubleJumpEnabled = true;

    // ── Slide ─────────────────────────────────────────────────────────────────
    [Header("Slide")]
    [SerializeField] private float slideStartSpeed = 25f;
    [SerializeField] private float slideEndSpeed = 15f;
    [SerializeField] private float slideFriction = 0.8f;
    [SerializeField] private float slideSteerAccelaration = 5f;
    [SerializeField] private float slideGravity = -90f;

    // ── Air Movement ──────────────────────────────────────────────────────────
    [Header("Air Movement")]
    [SerializeField] private float airSpeed = 15f;
    [SerializeField] private float airAccelaration = 70f;

    // ── Wall Run ──────────────────────────────────────────────────────────────
    [Header("Wall Run")]
    [SerializeField] private bool wallRunEnabled = true;
    [SerializeField] private float wallRunMinEntrySpeed = 10f;
    [SerializeField] private float wallRunSpeedBoost = 5f;
    [SerializeField] private float wallRunGravityScale = 0.15f;   // fraction of normal gravity
    [SerializeField] private float wallRunDecceleration = 8f;      // how fast speed bleeds to 0
    [SerializeField] private float wallStickForce = 15f;     // inward force keeping player on wall
    [SerializeField] private float wallNormalTolerance = 8f;      // degrees from 90 accepted
    [SerializeField] private float wallJumpWallForce = 12f;     // away from wall
    [SerializeField] private float wallJumpUpForce = 14f;     // upward
    [SerializeField] private float wallJumpForwardForce = 8f;      // in player velocity dir

    // ── Prone ─────────────────────────────────────────────────────────────────
    [Header("Prone")]
    [SerializeField] private float proneHeight = 0.5f;
    [SerializeField] private float proneSpeed = 3f;
    [SerializeField] private float proneResponse = 15f;
    [Range(0f, 1f)]
    [SerializeField] private float proneCameraTargetHeight = 0.3f;

    // ── Capsule ───────────────────────────────────────────────────────────────
    [Header("Capsule")]
    [SerializeField] private float standHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float crouchHeightResponse = 15f;

    // ── Camera Target Heights ─────────────────────────────────────────────────
    [Header("Camera Target Heights")]
    [Range(0f, 1f)]
    [SerializeField] private float standCameraTargetHeight = 0.9f;
    [Range(0f, 1f)]
    [SerializeField] private float crouchCameraTargetHeight = 0.7f;

    // ── Runtime state ─────────────────────────────────────────────────────────

    private CharacterState _state;
    private CharacterState _lastState;
    private CharacterState _tempState;

    private Quaternion _requestedRotation;
    private Vector3 _requestedMovement;
    private bool _requestedJump;
    private bool _requestedSustainedJump;
    private bool _requestedCrouch;
    private bool _requestedCrouchInAir;
    private bool _requestedSprint;
    private bool _requestedProne;

    private float _timeSinceUngrounded;
    private float _timeSinceJumpRequested;
    private bool _ungroundedDueToJump;

    // Double jump
    private bool _doubleJumpAvailable = false;

    // Wall run
    private Vector3 _wallNormal = Vector3.zero;
    private Vector3 _wallRunDirection = Vector3.zero;
    private bool _touchingWall = false;
    private Vector3 _currentWallNormal = Vector3.zero;

    private Collider[] _uncrouchOverlapResults;

    // ── Public accessors ──────────────────────────────────────────────────────

    public Transform GetCameraTarget() => cameraTarget;
    public CharacterState GetState() => _state;
    public CharacterState GetLastState() => _lastState;

    // ── Init ──────────────────────────────────────────────────────────────────

    public void Initialize()
    {
        _state.Stance = Stance.Stand;
        _lastState = _state;
        _uncrouchOverlapResults = new Collider[8];
        _doubleJumpAvailable = doubleJumpEnabled; // fix: available from the start
        motor.CharacterController = this;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public void UpdateInput(CharacterInput input)
    {
        _requestedRotation = input.Rotation;

        _requestedMovement = new Vector3(input.Move.x, 0f, input.Move.y);
        _requestedMovement = Vector3.ClampMagnitude(_requestedMovement, 1f);
        _requestedMovement = input.Rotation * _requestedMovement;

        var wasRequestingJump = _requestedJump;
        _requestedJump = _requestedJump || input.Jump;
        if (_requestedJump && !wasRequestingJump)
            _timeSinceJumpRequested = 0f;
        _requestedSustainedJump = input.JumpSustain;

        var wasRequestingCrouch = _requestedCrouch;
        _requestedCrouch = input.Crouch switch
        {
            CrouchInput.Toggle => !_requestedCrouch,
            CrouchInput.None => _requestedCrouch,
            _ => _requestedCrouch
        };
        if (_requestedCrouch && !wasRequestingCrouch)
            _requestedCrouchInAir = !_state.Grounded;
        else if (!_requestedCrouch && !wasRequestingCrouch)
            _requestedCrouchInAir = false;

        _requestedSprint = sprintEnabled && input.Sprint;
        _requestedProne = input.Prone;
    }

    // ── Body (visual scaling) ─────────────────────────────────────────────────

    public void UpdateBody(float deltaTime)
    {
        var currentHeight = motor.Capsule.height;
        var normalizedHeight = currentHeight / standHeight;

        float camHeightRatio = _state.Stance switch
        {
            Stance.Stand or Stance.Sprint or Stance.WallRun => standCameraTargetHeight,
            Stance.Prone => proneCameraTargetHeight,
            _ => crouchCameraTargetHeight
        };

        var cameraTargetHeight = currentHeight * camHeightRatio;
        var rootTargetScale = new Vector3(1f, normalizedHeight, 1f);

        cameraTarget.localPosition = Vector3.Lerp(
            cameraTarget.localPosition,
            new Vector3(0f, cameraTargetHeight, 0f),
            1f - Mathf.Exp(-crouchHeightResponse * deltaTime));

        root.localScale = Vector3.Lerp(
            root.localScale,
            rootTargetScale,
            1f - Mathf.Exp(-crouchHeightResponse * deltaTime));
    }

    // ── Before character update ───────────────────────────────────────────────

    public void BeforeCharacterUpdate(float deltaTime)
    {
        _tempState = _state;

        // Wall touch flag was set last frame by OnMovementHit — use it now,
        // then clear it so it must be re-set this frame to remain true
        bool wasTouchingWall = _touchingWall;
        Vector3 lastWallNormal = _currentWallNormal;
        _touchingWall = false;
        _currentWallNormal = Vector3.zero;

        // ── Prone state machine ───────────────────────────────────────────────

        if (_requestedProne)
        {
            _requestedProne = false; // consume

            switch (_state.Stance)
            {
                // Stand → Crouch (pressing prone while standing)
                case Stance.Stand:
                case Stance.Sprint:
                    EnterCrouch();
                    break;

                // Crouch → Prone
                case Stance.Crouch:
                    if (!_state.Grounded) break; // block prone while airborne
                    EnterProne();
                    break;

                // Prone → Crouch
                case Stance.Prone:
                    ExitProne();
                    break;

                // Slide → Prone
                case Stance.Slide:
                    if (!_state.Grounded) break;
                    EnterProne();
                    break;
            }
        }

        // ── Sprint ────────────────────────────────────────────────────────────

        if (_requestedSprint)
        {
            // Sprint from Prone goes to Crouch
            if (_state.Stance is Stance.Prone)
                ExitProne();

            // Sprint from Crouch goes to Stand
            else if (_state.Stance is Stance.Crouch)
            {
                // Try to stand
                TryStand();
            }
        }

        if (_requestedSprint && _state.Grounded && _state.Stance is Stance.Stand
            && _requestedMovement.sqrMagnitude > 0f)
            _state.Stance = Stance.Sprint;

        if (_requestedSprint && _state.Grounded && _state.Stance is Stance.Stand
            && _requestedMovement.sqrMagnitude > 0f && !_lastState.Grounded)
            _state.Stance = Stance.Sprint;

        if (_state.Stance is Stance.Sprint)
        {
            if (!_requestedSprint || _requestedMovement.sqrMagnitude <= 0f || !_state.Grounded)
                _state.Stance = Stance.Stand;
        }

        // ── Crouch ────────────────────────────────────────────────────────────

        if (_requestedCrouch && _state.Stance is Stance.Stand or Stance.Sprint)
            EnterCrouch();

        // ── Wall run entry ────────────────────────────────────────────────────

        if (wallRunEnabled && wasTouchingWall && !_state.Grounded
            && _state.Stance is not Stance.WallRun
            && _state.Stance is not Stance.Prone)
        {
            var wallAngle = Vector3.Angle(lastWallNormal, Vector3.up);
            var isVerticalWall = Mathf.Abs(wallAngle - 90f) <= wallNormalTolerance;
            var planarVel = Vector3.ProjectOnPlane(_tempState.Velocity, motor.CharacterUp);

            Debug.Log($"[WallRun] Touch detected — angle: {wallAngle:F1}°, " +
                      $"isVertical: {isVerticalWall}, speed: {planarVel.magnitude:F1}, " +
                      $"minEntry: {wallRunMinEntrySpeed}, grounded: {_state.Grounded}");

            if (isVerticalWall && planarVel.magnitude >= wallRunMinEntrySpeed)
            {
                Debug.Log("[WallRun] ENTERING wall run");
                EnterWallRun(lastWallNormal);
            }
        }

        // Exit wall run if no longer touching wall or now grounded
        if (_state.Stance is Stance.WallRun && (!wasTouchingWall || _state.Grounded))
        {
            Debug.Log($"[WallRun] EXITING — touchingWall: {wasTouchingWall}, grounded: {_state.Grounded}");
            ExitWallRun();
        }
    }

    // ── Post ground update ────────────────────────────────────────────────────

    public void PostGroundUpdate(float deltaTime)
    {
        if (!motor.GroundingStatus.IsStableOnGround && _state.Stance is Stance.Slide)
            _state.Stance = Stance.Crouch;

        if (!motor.GroundingStatus.IsStableOnGround && _state.Stance is Stance.Sprint)
            _state.Stance = Stance.Stand;

        if (motor.GroundingStatus.IsStableOnGround)
        {
            _doubleJumpAvailable = doubleJumpEnabled;
            if (_state.Stance is Stance.WallRun)
                ExitWallRun();
        }
    }

    // ── After character update ────────────────────────────────────────────────

    public void AfterCharacterUpdate(float deltaTime)
    {
        // Uncrouch / un-prone
        bool wantToStand = !_requestedCrouch
            && _state.Stance is not Stance.Stand
            && _state.Stance is not Stance.Sprint
            && _state.Stance is not Stance.WallRun;

        if (wantToStand)
            TryStand();

        _state.Grounded = motor.GroundingStatus.IsStableOnGround;
        _state.Velocity = motor.Velocity;
        _state.IsWallRunning = _state.Stance is Stance.WallRun;
        _state.WallNormal = _wallNormal;
        _lastState = _tempState;
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        var forward = Vector3.ProjectOnPlane(
            _requestedRotation * Vector3.forward, motor.CharacterUp);
        if (forward != Vector3.zero)
            currentRotation = Quaternion.LookRotation(forward, motor.CharacterUp);
    }

    // ── Velocity ──────────────────────────────────────────────────────────────

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        _state.Acceloration = Vector3.zero;

        // ── Wall run ──────────────────────────────────────────────────────────
        if (_state.Stance is Stance.WallRun)
        {
            UpdateWallRunVelocity(ref currentVelocity, deltaTime);
            HandleJump(ref currentVelocity, deltaTime, isWallRun: true);
            return;
        }

        // ── Grounded ──────────────────────────────────────────────────────────
        if (motor.GroundingStatus.IsStableOnGround)
        {
            _timeSinceUngrounded = 0f;
            _ungroundedDueToJump = false;

            var groundedMovement = motor.GetDirectionTangentToSurface(
                _requestedMovement, motor.GroundingStatus.GroundNormal)
                * _requestedMovement.magnitude;

            // Slide entry
            {
                var moving = groundedMovement.sqrMagnitude > 0f;
                var crouching = _state.Stance is Stance.Crouch;
                var wasStanding = _lastState.Stance is Stance.Stand;
                var wasSprinting = _lastState.Stance is Stance.Sprint;
                var wasInAir = !_lastState.Grounded;

                if (moving && crouching && (wasStanding || wasSprinting || wasInAir))
                {
                    _state.Stance = Stance.Slide;
                    if (wasInAir)
                        currentVelocity = Vector3.ProjectOnPlane(
                            _lastState.Velocity, motor.GroundingStatus.GroundNormal);

                    var effectiveSlideStartSpeed = slideStartSpeed;
                    if (!_lastState.Grounded && !_requestedCrouchInAir)
                    {
                        effectiveSlideStartSpeed = 0f;
                        _requestedCrouchInAir = false;
                    }

                    var slideSpeed = Mathf.Max(effectiveSlideStartSpeed, currentVelocity.magnitude);
                    currentVelocity = motor.GetDirectionTangentToSurface(
                        currentVelocity, motor.GroundingStatus.GroundNormal) * slideSpeed;
                }
            }

            // Walk / Crouch / Sprint / Prone
            if (_state.Stance is Stance.Stand or Stance.Crouch or Stance.Sprint or Stance.Prone)
            {
                var speed = _state.Stance switch
                {
                    Stance.Sprint => sprintSpeed,
                    Stance.Crouch => crouchSpeed,
                    Stance.Prone => proneSpeed,
                    _ => walkSpeed
                };
                var response = _state.Stance switch
                {
                    Stance.Sprint => sprintResponse,
                    Stance.Crouch => crouchResponse,
                    Stance.Prone => proneResponse,
                    _ => walkResponse
                };

                var targetVelocity = groundedMovement * speed;
                var moveVelocity = Vector3.Lerp(currentVelocity, targetVelocity,
                    1f - Mathf.Exp(-response * deltaTime));

                _state.Acceloration = moveVelocity - currentVelocity;
                currentVelocity = moveVelocity;
            }
            else // Sliding
            {
                currentVelocity -= currentVelocity * (slideFriction * deltaTime);

                var force = Vector3.ProjectOnPlane(-motor.CharacterUp,
                    motor.GroundingStatus.GroundNormal) * slideGravity;
                currentVelocity -= force * deltaTime;

                var currentSpeed = currentVelocity.magnitude;
                var targetVelocity = groundedMovement * currentVelocity.magnitude;
                var steerVelocity = currentVelocity;
                var steerForce = (targetVelocity - steerVelocity) * slideSteerAccelaration * deltaTime;
                steerVelocity += steerForce;
                steerVelocity = Vector3.ClampMagnitude(steerVelocity, currentSpeed);

                _state.Acceloration = (steerVelocity - currentVelocity) / deltaTime;
                currentVelocity = steerVelocity;

                if (currentVelocity.magnitude < slideEndSpeed)
                    _state.Stance = Stance.Crouch;
            }
        }
        // ── In the air ────────────────────────────────────────────────────────
        else
        {
            _timeSinceUngrounded += deltaTime;

            if (_requestedMovement.sqrMagnitude > 0f)
            {
                var planarMovement = Vector3.ProjectOnPlane(
                    _requestedMovement, motor.CharacterUp) * _requestedMovement.magnitude;

                var currentPlanarVelocity = Vector3.ProjectOnPlane(
                    currentVelocity, motor.CharacterUp);

                var movementForce = planarMovement * airAccelaration * deltaTime;

                if (currentPlanarVelocity.magnitude < airSpeed)
                {
                    var targetPlanarVelocity = Vector3.ClampMagnitude(
                        currentPlanarVelocity + movementForce, airSpeed);
                    movementForce = targetPlanarVelocity - currentPlanarVelocity;
                }
                else if (Vector3.Dot(currentPlanarVelocity, movementForce) > 0f)
                {
                    movementForce = Vector3.ProjectOnPlane(
                        movementForce, currentPlanarVelocity.normalized);
                }

                if (motor.GroundingStatus.FoundAnyGround)
                {
                    if (Vector3.Dot(movementForce, currentVelocity + movementForce) > 0f)
                    {
                        var obstructionNormal = Vector3.Cross(motor.CharacterUp,
                            Vector3.Cross(motor.CharacterUp,
                                motor.GroundingStatus.GroundNormal)).normalized;
                        movementForce = Vector3.ProjectOnPlane(movementForce, obstructionNormal);
                    }
                }

                currentVelocity += movementForce;
            }

            var effectiveGravity = gravity;
            var verticalSpeed = Vector3.Dot(currentVelocity, motor.CharacterUp);
            if (_requestedSustainedJump && verticalSpeed > 0f)
                effectiveGravity *= jumpSustainGravity;
            currentVelocity += motor.CharacterUp * effectiveGravity * deltaTime;
        }

        HandleJump(ref currentVelocity, deltaTime, isWallRun: false);

        // Grapple overrides/modifies velocity when active
        if (grapplingHook != null && grapplingHook.IsGrappling)
            grapplingHook.ApplyGrappleVelocity(ref currentVelocity, deltaTime,
                _requestedMovement, airAccelaration);
    }

    // ── Wall run velocity ─────────────────────────────────────────────────────

    private void UpdateWallRunVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        var wallUp = motor.CharacterUp;

        // ── Stick to wall ─────────────────────────────────────────────────────
        // Remove any velocity pushing away from wall, then add constant inward force
        var awayFromWall = Vector3.Dot(currentVelocity, _wallNormal);
        if (awayFromWall > 0f)
            currentVelocity -= _wallNormal * awayFromWall;
        currentVelocity -= _wallNormal * wallStickForce * deltaTime;

        // ── Horizontal speed (parallel to wall, ignoring normal component) ────
        var horizVel = Vector3.ProjectOnPlane(currentVelocity, wallUp);
        horizVel = Vector3.ProjectOnPlane(horizVel, _wallNormal);
        var horizSpeed = horizVel.magnitude;

        // Slide-style deceleration: MoveTowards 0 at wallRunDecceleration rate
        var newHorizSpeed = Mathf.MoveTowards(horizSpeed, 0f, wallRunDecceleration * deltaTime);

        if (newHorizSpeed <= 0.01f)
        {
            ExitWallRun();
            return;
        }

        var horizDir = horizSpeed > 0.01f ? horizVel / horizSpeed : _wallRunDirection;
        _wallRunDirection = horizDir;

        // ── Vertical: reduced gravity, no player input drives it ──────────────
        var vertVel = Vector3.Dot(currentVelocity, wallUp);
        var newVertVel = vertVel + gravity * wallRunGravityScale * deltaTime;
        // Cap upward so entry boost doesn't rocket the player
        newVertVel = Mathf.Min(newVertVel, wallRunSpeedBoost);

        // ── Recombine — no air control added ─────────────────────────────────
        currentVelocity = horizDir * newHorizSpeed + wallUp * newVertVel;
        _state.WallNormal = _wallNormal;
    }

    // ── Jump handler (shared between grounded, air, wall run) ─────────────────

    private void HandleJump(ref Vector3 currentVelocity, float deltaTime, bool isWallRun)
    {
        if (!_requestedJump) return;

        if (isWallRun)
        {
            // Wall jump
            _requestedJump = false;
            ExitWallRun();
            motor.ForceUnground(0f);
            _ungroundedDueToJump = true;

            // Direction: away from wall + up + forward momentum
            var planarVelocityDir = Vector3.ProjectOnPlane(
                currentVelocity, motor.CharacterUp).normalized;

            var jumpDir = (_wallNormal * wallJumpWallForce
                         + motor.CharacterUp * wallJumpUpForce
                         + planarVelocityDir * wallJumpForwardForce).normalized;

            var jumpMagnitude = Mathf.Sqrt(
                wallJumpWallForce * wallJumpWallForce
                + wallJumpUpForce * wallJumpUpForce
                + wallJumpForwardForce * wallJumpForwardForce);

            currentVelocity = jumpDir * jumpMagnitude;
            return;
        }

        var grounded = motor.GroundingStatus.IsStableOnGround;
        var canCoyoteJump = _timeSinceUngrounded < coyoteTime && !_ungroundedDueToJump;

        if (grounded || canCoyoteJump)
        {
            // Normal jump — also reset double jump so it's available after this jump
            _requestedJump = false;
            _requestedCrouch = false;
            _requestedCrouchInAir = false;
            motor.ForceUnground(0f);
            _ungroundedDueToJump = true;
            _doubleJumpAvailable = doubleJumpEnabled; // reset here, not just on landing

            var currentVerticalSpeed = Vector3.Dot(currentVelocity, motor.CharacterUp);
            var targetVerticalSpeed = Mathf.Max(currentVerticalSpeed, jumpSpeed);
            currentVelocity += motor.CharacterUp * (targetVerticalSpeed - currentVerticalSpeed);
        }
        else if (doubleJumpEnabled && _doubleJumpAvailable)
        {
            // Double jump — momentum preserving
            Debug.Log("[DoubleJump] Executing double jump");
            _requestedJump = false;
            _doubleJumpAvailable = false;
            motor.ForceUnground(0f);

            var currentVerticalSpeed = Vector3.Dot(currentVelocity, motor.CharacterUp);
            var targetVerticalSpeed = Mathf.Max(currentVerticalSpeed, jumpSpeed);
            currentVelocity += motor.CharacterUp * (targetVerticalSpeed - currentVerticalSpeed);
        }
        else
        {
            Debug.Log($"[DoubleJump] Jump failed — doubleJumpEnabled: {doubleJumpEnabled}, " +
                      $"available: {_doubleJumpAvailable}, grounded: {motor.GroundingStatus.IsStableOnGround}");
            _timeSinceJumpRequested += deltaTime;
            _requestedJump = _timeSinceJumpRequested < coyoteTime;
        }
    }

    // ── Wall run helpers ──────────────────────────────────────────────────────

    private void EnterWallRun(Vector3 wallNormal)
    {
        _wallNormal = wallNormal;
        _state.Stance = Stance.WallRun;
        _doubleJumpAvailable = doubleJumpEnabled; // reset double jump on wall contact

        // Compute initial wall run direction from current velocity projected onto wall
        var planarVelocity = Vector3.ProjectOnPlane(_tempState.Velocity, motor.CharacterUp);
        _wallRunDirection = Vector3.ProjectOnPlane(planarVelocity, wallNormal).normalized;

        // Apply entry speed boost
        var currentHorizSpeed = Vector3.ProjectOnPlane(
            _tempState.Velocity, motor.CharacterUp).magnitude;
        motor.BaseVelocity = _wallRunDirection * (currentHorizSpeed + wallRunSpeedBoost)
            + motor.CharacterUp * Vector3.Dot(_tempState.Velocity, motor.CharacterUp);
    }

    private void ExitWallRun()
    {
        if (_state.Stance is Stance.WallRun)
            _state.Stance = Stance.Stand;
        _wallNormal = Vector3.zero;
        _wallRunDirection = Vector3.zero;
    }

    // ── Prone helpers ─────────────────────────────────────────────────────────

    private void EnterProne()
    {
        _state.Stance = Stance.Prone;
        motor.SetCapsuleDimensions(motor.Capsule.radius, proneHeight, proneHeight * 0.5f);
    }

    private void ExitProne()
    {
        // Try to stand to crouch first
        motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
        var pos = motor.TransientPosition;
        var rot = motor.TransientRotation;
        var mask = motor.CollidableLayers;
        if (motor.CharacterOverlap(pos, rot, _uncrouchOverlapResults, mask,
            QueryTriggerInteraction.Ignore) > 0)
        {
            // Can't stand up, stay prone
            motor.SetCapsuleDimensions(motor.Capsule.radius, proneHeight, proneHeight * 0.5f);
            return;
        }
        _state.Stance = Stance.Crouch;
    }

    // ── Crouch helpers ────────────────────────────────────────────────────────

    private void EnterCrouch()
    {
        _state.Stance = Stance.Crouch;
        motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
    }

    private void TryStand()
    {
        // Tentatively resize to stand height, check for obstructions
        motor.SetCapsuleDimensions(motor.Capsule.radius, standHeight, standHeight * 0.5f);
        var pos = motor.TransientPosition;
        var rot = motor.TransientRotation;
        var mask = motor.CollidableLayers;
        if (motor.CharacterOverlap(pos, rot, _uncrouchOverlapResults, mask,
            QueryTriggerInteraction.Ignore) > 0)
        {
            // Revert to crouch
            _requestedCrouch = true;
            motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
        }
        else
        {
            _state.Stance = Stance.Stand;
        }
    }

    // ── ICharacterController ──────────────────────────────────────────────────

    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }

    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport hitStabilityReport)
    { }

    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport hitStabilityReport)
    {
        var angle = Vector3.Angle(hitNormal, Vector3.up);
        Debug.Log($"[WallRun] OnMovementHit — collider: {hitCollider.name}, " +
                  $"normal angle from up: {angle:F1}°, tolerance: {wallNormalTolerance}°");

        if (Mathf.Abs(angle - 90f) <= wallNormalTolerance)
        {
            _touchingWall = true;
            _currentWallNormal = hitNormal;
            Debug.Log($"[WallRun] Wall contact registered — normal: {hitNormal}");
        }
    }

    public void PostGroundingUpdate(float deltaTime) { }

    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal,
        Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation,
        ref HitStabilityReport hitStabilityReport)
    { }

    public void SetPosition(Vector3 position, bool killVelocity = true)
    {
        motor.SetPosition(position);
        if (killVelocity)
            motor.BaseVelocity = Vector3.zero;
    }
}
