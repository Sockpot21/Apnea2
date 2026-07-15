using System.Collections;
using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;

public enum CrouchInput { None, Toggle }

public enum Stance { Stand, Crouch, Slide, Sprint, WallRun, Prone }

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
    public bool IsWallRunning;
    public bool IsVerticalWallRunning;
    public Vector3 WallNormal;
}

public class PlayerCharacter : MonoBehaviour, ICharacterController
{
    [SerializeField] private KinematicCharacterMotor motor;
    [SerializeField] private Transform root;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private GrapplingHook grapplingHook;

    [Header("Ground Movement")]
    [SerializeField] private float walkSpeed = 20f;
    [SerializeField] private float crouchSpeed = 7f;
    [SerializeField] private float walkResponse = 25f;
    [SerializeField] private float crouchResponse = 20f;

    [Header("Sprint")]
    [SerializeField] private bool sprintEnabled = true;
    [SerializeField] private float sprintSpeed = 30f;
    [SerializeField] private float sprintResponse = 20f;

    [Header("Jump")]
    [SerializeField] private float jumpSpeed = 20f;
    [SerializeField] private float coyoteTime = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float jumpSustainGravity = 0.4f;
    [SerializeField] private float gravity = -90f;

    [Header("Double Jump")]
    [SerializeField] private bool doubleJumpEnabled = true;

    [Header("Slide")]
    [SerializeField] private float slideStartSpeed = 25f;
    [SerializeField] private float slideEndSpeed = 15f;
    [SerializeField] private float slideFriction = 0.8f;
    [SerializeField] private float slideSteerAccelaration = 5f;
    [SerializeField] private float slideGravity = -90f;

    [Header("Air Movement")]
    [SerializeField] private float airSpeed = 15f;
    [SerializeField] private float airAccelaration = 70f;

    [Header("Wall Run")]
    [SerializeField] private bool wallRunEnabled = true;
    [SerializeField] private float wallRunMinEntrySpeed = 10f;
    [Tooltip("Multiplier applied to the player's entry velocity for horizontal wall runs.")]
    [SerializeField] private float horizontalWallRunEntrySpeedMultiplier = 1.1f;
    [SerializeField] private float horizontalWallRunDuration = 1.5f;
    [Tooltip("Horizontal wall-run speed lost per second.")]
    [SerializeField] private float horizontalWallRunDecayRate = 12f;
    [SerializeField] private float wallRunGravityFadeTime = 0.5f;
    [SerializeField] private float wallRunCooldown = 1f;
    [SerializeField] private float wallDetectRadius = 0.6f; // raycast dist for wall check
    [SerializeField] private float wallNormalTolerance = 8f;
    [Tooltip("Minimum upward look angle required to start a vertical wall run.")]
    [Range(0f, 89f)]
    [SerializeField] private float wallRunPitchThreshold = 70f;
    [Tooltip("Initial upward speed for a vertical wall run. This is independent of entry speed.")]
    [SerializeField] private float verticalWallRunStartSpeed = 12f;
    [SerializeField] private float verticalWallRunDuration = 1.2f;
    [Tooltip("Vertical wall-run speed lost per second.")]
    [SerializeField] private float verticalWallRunDecayRate = 10f;
    [SerializeField] private float wallJumpWallForce = 12f;
    [SerializeField] private float wallJumpUpForce = 14f;
    [SerializeField] private float wallJumpForwardForce = 8f;
    [Tooltip("Peak height of the subtle parabolic arc used by horizontal wall runs.")]
    [SerializeField] private float wallRunArcHeight = 0.3f;
    [SerializeField] private bool drawWallRunDebugPath;
    [SerializeField, Min(2)] private int wallRunDebugPathSegments = 24;

    [Header("Prone")]
    [SerializeField] private float proneHeight = 0.5f;
    [SerializeField] private float proneSpeed = 3f;
    [SerializeField] private float proneResponse = 15f;
    [Range(0f, 1f)]
    [SerializeField] private float proneCameraTargetHeight = 0.3f;

    [Header("Capsule")]
    [SerializeField] private float standHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;
    [SerializeField] private float crouchHeightResponse = 15f;

    [Header("Camera Target Heights")]
    [Range(0f, 1f)][SerializeField] private float standCameraTargetHeight = 0.9f;
    [Range(0f, 1f)][SerializeField] private float crouchCameraTargetHeight = 0.7f;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private CharacterState _state;
    private CharacterState _lastState;
    private CharacterState _tempState;

    private Quaternion _requestedRotation;
    private Vector3 _requestedMovement;
    private bool _requestedJump;
    private bool _requestedSustainedJump;
    private bool _requestedCrouch;
    private bool _requestedCrouchPress;
    private bool _requestedCrouchInAir;
    private bool _requestedSprint;
    private bool _requestedProne;
    private bool _forcedCrawl;

    private float _timeSinceUngrounded;
    private float _timeSinceJumpRequested;
    private bool _ungroundedDueToJump;
    private bool _doubleJumpAvailable;

    // Wall run
    private Vector3 _wallNormal;
    private Vector3 _wallRunDirection;
    private Vector3 _wallRunArcUp;
    private Vector3 _wallRunStartPosition;
    private float _wallRunStartSpeed;
    private float _wallRunCurrentSpeed;
    private bool _wallRunIsVertical;
    private float _wallRunTimer;
    private float _wallRunFadeTimer;
    private float _wallRunCooldownTimer;
    private Vector3 _lastWallNormal;

    // Augment effects are applied to runtime copies of inspector settings. The
    // captured values remain the player's baseline when no functional augment
    // overrides a stat.
    private bool _augmentStatsCaptured;
    private Dictionary<PlayerAugmentStat, float> _baseFloatStats;
    private Dictionary<PlayerAugmentStat, bool> _baseToggleStats;

    // OnMovementHit sets these; read at start of BeforeCharacterUpdate
    private bool _touchingWall;
    private Vector3 _currentWallNormal;

    private Collider[] _uncrouchOverlapResults;

    private float ActiveWallRunDuration => _wallRunIsVertical
        ? verticalWallRunDuration
        : horizontalWallRunDuration;

    private float ActiveWallRunDecayRate => _wallRunIsVertical
        ? verticalWallRunDecayRate
        : horizontalWallRunDecayRate;

    // ── Public ────────────────────────────────────────────────────────────────

    public Transform GetCameraTarget() => cameraTarget;
    public CharacterState GetState() => _state;
    public CharacterState GetLastState() => _lastState;

    public void SetForcedCrawl(bool forced)
    {
        _forcedCrawl = forced;
        if (_forcedCrawl && _state.Stance is not Stance.Prone)
            EnterProne();
    }

    public void Initialize()
    {
        CaptureBaseAugmentStats();
        _state.Stance = Stance.Stand;
        _lastState = _state;
        _uncrouchOverlapResults = new Collider[8];
        _doubleJumpAvailable = doubleJumpEnabled;
        motor.CharacterController = this;
    }

    public void ApplyAugmentStatOverrides(IEnumerable<AugmentStatOverride> overrides)
    {
        CaptureBaseAugmentStats();
        RestoreBaseAugmentStats();

        if (overrides != null)
            foreach (AugmentStatOverride statOverride in overrides)
                ApplyAugmentStatOverride(statOverride);

        _doubleJumpAvailable = doubleJumpEnabled;
    }

    private void CaptureBaseAugmentStats()
    {
        if (_augmentStatsCaptured) return;
        _augmentStatsCaptured = true;
        _baseFloatStats = new Dictionary<PlayerAugmentStat, float>
        {
            { PlayerAugmentStat.WalkSpeed, walkSpeed }, { PlayerAugmentStat.CrouchSpeed, crouchSpeed },
            { PlayerAugmentStat.WalkResponse, walkResponse }, { PlayerAugmentStat.CrouchResponse, crouchResponse },
            { PlayerAugmentStat.SprintSpeed, sprintSpeed }, { PlayerAugmentStat.SprintResponse, sprintResponse },
            { PlayerAugmentStat.JumpSpeed, jumpSpeed }, { PlayerAugmentStat.CoyoteTime, coyoteTime },
            { PlayerAugmentStat.JumpSustainGravity, jumpSustainGravity }, { PlayerAugmentStat.Gravity, gravity },
            { PlayerAugmentStat.SlideStartSpeed, slideStartSpeed }, { PlayerAugmentStat.SlideEndSpeed, slideEndSpeed },
            { PlayerAugmentStat.SlideFriction, slideFriction }, { PlayerAugmentStat.SlideSteerAcceleration, slideSteerAccelaration },
            { PlayerAugmentStat.SlideGravity, slideGravity }, { PlayerAugmentStat.AirSpeed, airSpeed },
            { PlayerAugmentStat.AirAcceleration, airAccelaration }, { PlayerAugmentStat.WallRunMinEntrySpeed, wallRunMinEntrySpeed },
            { PlayerAugmentStat.HorizontalWallRunEntrySpeedMultiplier, horizontalWallRunEntrySpeedMultiplier },
            { PlayerAugmentStat.HorizontalWallRunDuration, horizontalWallRunDuration },
            { PlayerAugmentStat.HorizontalWallRunDecayRate, horizontalWallRunDecayRate },
            { PlayerAugmentStat.WallRunGravityFadeTime, wallRunGravityFadeTime }, { PlayerAugmentStat.WallRunCooldown, wallRunCooldown },
            { PlayerAugmentStat.WallDetectRadius, wallDetectRadius }, { PlayerAugmentStat.WallNormalTolerance, wallNormalTolerance },
            { PlayerAugmentStat.WallRunPitchThreshold, wallRunPitchThreshold },
            { PlayerAugmentStat.VerticalWallRunStartSpeed, verticalWallRunStartSpeed },
            { PlayerAugmentStat.VerticalWallRunDuration, verticalWallRunDuration },
            { PlayerAugmentStat.VerticalWallRunDecayRate, verticalWallRunDecayRate },
            { PlayerAugmentStat.WallRunArcHeight, wallRunArcHeight }, { PlayerAugmentStat.ProneHeight, proneHeight },
            { PlayerAugmentStat.ProneSpeed, proneSpeed }, { PlayerAugmentStat.ProneResponse, proneResponse }
        };
        _baseToggleStats = new Dictionary<PlayerAugmentStat, bool>
        {
            { PlayerAugmentStat.SprintEnabled, sprintEnabled }, { PlayerAugmentStat.DoubleJumpEnabled, doubleJumpEnabled },
            { PlayerAugmentStat.WallRunEnabled, wallRunEnabled }
        };
    }

    private void RestoreBaseAugmentStats()
    {
        foreach (var stat in _baseFloatStats) SetAugmentFloatStat(stat.Key, stat.Value);
        foreach (var stat in _baseToggleStats) SetAugmentToggleStat(stat.Key, stat.Value);
    }

    private void ApplyAugmentStatOverride(AugmentStatOverride statOverride)
    {
        if (statOverride == null) return;
        if (_baseToggleStats.ContainsKey(statOverride.stat))
            SetAugmentToggleStat(statOverride.stat, statOverride.boolValue);
        else if (_baseFloatStats.ContainsKey(statOverride.stat))
            SetAugmentFloatStat(statOverride.stat, statOverride.value);
        else
            Debug.LogWarning($"[PlayerCharacter] Unsupported augment stat: {statOverride.stat}");
    }

    private void SetAugmentToggleStat(PlayerAugmentStat stat, bool value)
    {
        switch (stat)
        {
            case PlayerAugmentStat.SprintEnabled: sprintEnabled = value; break;
            case PlayerAugmentStat.DoubleJumpEnabled: doubleJumpEnabled = value; break;
            case PlayerAugmentStat.WallRunEnabled: wallRunEnabled = value; break;
        }
    }

    private void SetAugmentFloatStat(PlayerAugmentStat stat, float value)
    {
        switch (stat)
        {
            case PlayerAugmentStat.WalkSpeed: walkSpeed = value; break;
            case PlayerAugmentStat.CrouchSpeed: crouchSpeed = value; break;
            case PlayerAugmentStat.WalkResponse: walkResponse = value; break;
            case PlayerAugmentStat.CrouchResponse: crouchResponse = value; break;
            case PlayerAugmentStat.SprintSpeed: sprintSpeed = value; break;
            case PlayerAugmentStat.SprintResponse: sprintResponse = value; break;
            case PlayerAugmentStat.JumpSpeed: jumpSpeed = value; break;
            case PlayerAugmentStat.CoyoteTime: coyoteTime = value; break;
            case PlayerAugmentStat.JumpSustainGravity: jumpSustainGravity = value; break;
            case PlayerAugmentStat.Gravity: gravity = value; break;
            case PlayerAugmentStat.SlideStartSpeed: slideStartSpeed = value; break;
            case PlayerAugmentStat.SlideEndSpeed: slideEndSpeed = value; break;
            case PlayerAugmentStat.SlideFriction: slideFriction = value; break;
            case PlayerAugmentStat.SlideSteerAcceleration: slideSteerAccelaration = value; break;
            case PlayerAugmentStat.SlideGravity: slideGravity = value; break;
            case PlayerAugmentStat.AirSpeed: airSpeed = value; break;
            case PlayerAugmentStat.AirAcceleration: airAccelaration = value; break;
            case PlayerAugmentStat.WallRunMinEntrySpeed: wallRunMinEntrySpeed = value; break;
            case PlayerAugmentStat.HorizontalWallRunEntrySpeedMultiplier: horizontalWallRunEntrySpeedMultiplier = value; break;
            case PlayerAugmentStat.HorizontalWallRunDuration: horizontalWallRunDuration = value; break;
            case PlayerAugmentStat.HorizontalWallRunDecayRate: horizontalWallRunDecayRate = value; break;
            case PlayerAugmentStat.WallRunGravityFadeTime: wallRunGravityFadeTime = value; break;
            case PlayerAugmentStat.WallRunCooldown: wallRunCooldown = value; break;
            case PlayerAugmentStat.WallDetectRadius: wallDetectRadius = value; break;
            case PlayerAugmentStat.WallNormalTolerance: wallNormalTolerance = value; break;
            case PlayerAugmentStat.WallRunPitchThreshold: wallRunPitchThreshold = value; break;
            case PlayerAugmentStat.VerticalWallRunStartSpeed: verticalWallRunStartSpeed = value; break;
            case PlayerAugmentStat.VerticalWallRunDuration: verticalWallRunDuration = value; break;
            case PlayerAugmentStat.VerticalWallRunDecayRate: verticalWallRunDecayRate = value; break;
            case PlayerAugmentStat.WallRunArcHeight: wallRunArcHeight = value; break;
            case PlayerAugmentStat.ProneHeight: proneHeight = value; break;
            case PlayerAugmentStat.ProneSpeed: proneSpeed = value; break;
            case PlayerAugmentStat.ProneResponse: proneResponse = value; break;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public void UpdateInput(CharacterInput input)
    {
        _requestedRotation = input.Rotation;
        _requestedMovement = Vector3.ClampMagnitude(
            input.Rotation * new Vector3(input.Move.x, 0f, input.Move.y), 1f);

        var wasJump = _requestedJump;
        _requestedJump = _requestedJump || input.Jump;
        if (_requestedJump && !wasJump) _timeSinceJumpRequested = 0f;
        _requestedSustainedJump = input.JumpSustain;

        var wasCrouch = _requestedCrouch;
        _requestedCrouchPress = input.Crouch is CrouchInput.Toggle;
        _requestedCrouch = input.Crouch switch
        {
            CrouchInput.Toggle => !_requestedCrouch,
            _ => _requestedCrouch
        };
        if (_requestedCrouch && !wasCrouch) _requestedCrouchInAir = !_state.Grounded;
        else if (!_requestedCrouch && !wasCrouch) _requestedCrouchInAir = false;

        _requestedSprint = sprintEnabled && input.Sprint;
        _requestedProne = input.Prone;
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    public void UpdateBody(float deltaTime)
    {
        var h = motor.Capsule.height;
        float camRatio = _state.Stance switch
        {
            Stance.Stand or Stance.Sprint or Stance.WallRun => standCameraTargetHeight,
            Stance.Prone => proneCameraTargetHeight,
            _ => crouchCameraTargetHeight
        };

        cameraTarget.localPosition = Vector3.Lerp(cameraTarget.localPosition,
            new Vector3(0f, h * camRatio, 0f),
            1f - Mathf.Exp(-crouchHeightResponse * deltaTime));

        root.localScale = Vector3.Lerp(root.localScale,
            new Vector3(1f, h / standHeight, 1f),
            1f - Mathf.Exp(-crouchHeightResponse * deltaTime));
    }

    // ── Before character update ───────────────────────────────────────────────

    public void BeforeCharacterUpdate(float deltaTime)
    {
        _tempState = _state;

        // Consume wall-touch flag set by OnMovementHit last frame
        bool wasTouchingWall = _touchingWall;
        Vector3 lastWallNormal = _currentWallNormal;
        _touchingWall = false;
        _currentWallNormal = Vector3.zero;

        // ── Prone ─────────────────────────────────────────────────────────────
        if (_forcedCrawl)
        {
            _requestedProne = false;
            if (_state.Stance is not Stance.Prone) EnterProne();
        }
        else if (_requestedProne)
        {
            _requestedProne = false;
            switch (_state.Stance)
            {
                case Stance.Stand:
                case Stance.Sprint:
                    _requestedCrouch = true;
                    EnterCrouch();
                    break;
                case Stance.Crouch:
                    if (_state.Grounded) EnterProne();
                    break;
                case Stance.Prone:
                    _requestedCrouch = false;
                    TryStand();
                    break;
                case Stance.Slide:
                    if (_state.Grounded)
                    {
                        _requestedCrouch = true;
                        EnterProne();
                    }
                    break;
            }
        }

        // Crouch from prone always returns to crouch, rather than toggling the
        // persistent crouch request into an unrelated state.
        if (_requestedCrouchPress && _state.Stance is Stance.Prone && !_forcedCrawl)
        {
            ExitProne();
            _requestedCrouch = true;
        }
        _requestedCrouchPress = false;

        // ── Sprint ────────────────────────────────────────────────────────────
        if (_requestedSprint && !_forcedCrawl)
        {
            if (_state.Stance is Stance.Prone)
            {
                ExitProne();
                _requestedCrouch = false;
                TryStand();
            }
            else if (_state.Stance is Stance.Crouch) TryStand();
        }
        if (_requestedSprint && _state.Grounded && _state.Stance is Stance.Stand
            && _requestedMovement.sqrMagnitude > 0f)
            _state.Stance = Stance.Sprint;
        if (_state.Stance is Stance.Sprint
            && (!_requestedSprint || _requestedMovement.sqrMagnitude <= 0f || !_state.Grounded))
            _state.Stance = Stance.Stand;

        // ── Crouch ────────────────────────────────────────────────────────────
        if (_requestedCrouch && _state.Stance is Stance.Stand or Stance.Sprint)
            EnterCrouch();

        // ── Wall run cooldown ─────────────────────────────────────────────────
        if (_wallRunCooldownTimer > 0f) _wallRunCooldownTimer -= deltaTime;

        // ── Wall run: continuous wall detection via raycast ───────────────────
        if (_state.Stance is Stance.WallRun)
        {
            // Raycast toward the wall every frame — exit if wall is gone
            bool wallStillPresent = Physics.Raycast(
                transform.position, -_wallNormal, wallDetectRadius + 0.1f);

            // Also exit if player is not holding W (forward input)
            float forwardInput = Vector3.Dot(_requestedMovement.normalized,
                Vector3.ProjectOnPlane(_requestedRotation * Vector3.forward, motor.CharacterUp).normalized);
            bool holdingForward = _requestedMovement.sqrMagnitude > 0.1f && forwardInput > 0.3f;

            Debug.Log($"[WallRun] Active — wallPresent:{wallStillPresent} " +
                      $"holdingForward:{holdingForward} timer:{_wallRunTimer:F2}");

            if (!wallStillPresent || !holdingForward || _state.Grounded)
            {
                Debug.Log("[WallRun] EXIT — " +
                    $"wallGone:{!wallStillPresent} noForward:{!holdingForward} grounded:{_state.Grounded}");
                ExitWallRun();
            }
        }

        // ── Wall run entry ────────────────────────────────────────────────────
        bool wantsVerticalWallRun = WantsVerticalWallRun(out float lookPitch);
        Vector3 candidateWallNormal = lastWallNormal;
        bool hasWallContact = wasTouchingWall;

        // Vertical runs should not rely on having enough collision velocity to
        // produce a movement hit. Probe the wall the character is facing too.
        if (wantsVerticalWallRun && !hasWallContact
            && Physics.Raycast(transform.position, transform.forward, out RaycastHit wallHit,
                wallDetectRadius + 0.1f))
        {
            candidateWallNormal = wallHit.normal;
            hasWallContact = true;
        }

        if (wallRunEnabled && hasWallContact && !_state.Grounded
            && _state.Stance is not Stance.WallRun
            && _state.Stance is not Stance.Prone)
        {
            var wallAngle = Vector3.Angle(candidateWallNormal, Vector3.up);
            bool isVWall = Mathf.Abs(wallAngle - 90f) <= wallNormalTolerance;
            var planarVel = Vector3.ProjectOnPlane(_tempState.Velocity, motor.CharacterUp);
            bool sameWall = Vector3.Dot(candidateWallNormal, _lastWallNormal) > 0.98f;
            bool onCooldown = sameWall && _wallRunCooldownTimer > 0f;
            bool hasHorizontalEntrySpeed = planarVel.magnitude >= wallRunMinEntrySpeed;

            Debug.Log($"[WallRun] Touch — angle:{wallAngle:F1} isVWall:{isVWall} " +
                      $"speed:{planarVel.magnitude:F1} pitch:{lookPitch:F1} " +
                      $"vertical:{wantsVerticalWallRun} cooldown:{onCooldown}");

            // Horizontal runs retain their speed gate. Vertical runs instead use
            // the upward look gate, so they can start from a standstill on a wall.
            if (isVWall && (wantsVerticalWallRun || hasHorizontalEntrySpeed) && !onCooldown)
            {
                Debug.Log("[WallRun] ENTERING");
                EnterWallRun(candidateWallNormal, wantsVerticalWallRun);
            }
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
            if (_state.Stance is Stance.WallRun) ExitWallRun();
        }
    }

    // ── After character update ────────────────────────────────────────────────

    public void AfterCharacterUpdate(float deltaTime)
    {
        bool wantToStand = !_requestedCrouch
            && _state.Stance is not Stance.Stand
            && _state.Stance is not Stance.Sprint
            && _state.Stance is not Stance.WallRun
            && _state.Stance is not Stance.Prone;
        if (wantToStand) TryStand();

        _state.Grounded = motor.GroundingStatus.IsStableOnGround;
        _state.Velocity = motor.Velocity;
        _state.IsWallRunning = _state.Stance is Stance.WallRun;
        _state.IsVerticalWallRunning = _state.IsWallRunning && _wallRunIsVertical;
        _state.WallNormal = _wallNormal;
        _lastState = _tempState;
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        var fwd = Vector3.ProjectOnPlane(_requestedRotation * Vector3.forward, motor.CharacterUp);
        if (fwd != Vector3.zero)
            currentRotation = Quaternion.LookRotation(fwd, motor.CharacterUp);
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

            var groundedMove = motor.GetDirectionTangentToSurface(
                _requestedMovement, motor.GroundingStatus.GroundNormal)
                * _requestedMovement.magnitude;

            // Slide entry
            bool moving = groundedMove.sqrMagnitude > 0f;
            bool crouching = _state.Stance is Stance.Crouch;
            bool wasStanding = _lastState.Stance is Stance.Stand;
            bool wasSprinting = _lastState.Stance is Stance.Sprint;
            bool wasInAir = !_lastState.Grounded;

            if (moving && crouching && (wasStanding || wasSprinting || wasInAir))
            {
                _state.Stance = Stance.Slide;
                if (wasInAir)
                    currentVelocity = Vector3.ProjectOnPlane(
                        _lastState.Velocity, motor.GroundingStatus.GroundNormal);

                float ess = (!_lastState.Grounded && !_requestedCrouchInAir) ? 0f : slideStartSpeed;
                if (!_lastState.Grounded && !_requestedCrouchInAir) _requestedCrouchInAir = false;

                float ss = Mathf.Max(ess, currentVelocity.magnitude);
                currentVelocity = motor.GetDirectionTangentToSurface(
                    currentVelocity, motor.GroundingStatus.GroundNormal) * ss;
            }

            if (_state.Stance is Stance.Stand or Stance.Crouch or Stance.Sprint or Stance.Prone)
            {
                float speed = _state.Stance switch
                {
                    Stance.Sprint => sprintSpeed,
                    Stance.Crouch => crouchSpeed,
                    Stance.Prone => proneSpeed,
                    _ => walkSpeed
                };
                float resp = _state.Stance switch
                {
                    Stance.Sprint => sprintResponse,
                    Stance.Crouch => crouchResponse,
                    Stance.Prone => proneResponse,
                    _ => walkResponse
                };
                var mv = Vector3.Lerp(currentVelocity, groundedMove * speed,
                    1f - Mathf.Exp(-resp * deltaTime));
                _state.Acceloration = mv - currentVelocity;
                currentVelocity = mv;
            }
            else // Sliding
            {
                currentVelocity -= currentVelocity * (slideFriction * deltaTime);
                currentVelocity -= Vector3.ProjectOnPlane(-motor.CharacterUp,
                    motor.GroundingStatus.GroundNormal) * slideGravity * deltaTime;

                float cs = currentVelocity.magnitude;
                var sv = currentVelocity;
                sv += (groundedMove * cs - sv) * slideSteerAccelaration * deltaTime;
                sv = Vector3.ClampMagnitude(sv, cs);
                _state.Acceloration = (sv - currentVelocity) / deltaTime;
                currentVelocity = sv;

                if (currentVelocity.magnitude < slideEndSpeed) _state.Stance = Stance.Crouch;
            }
        }
        // ── Air ───────────────────────────────────────────────────────────────
        else
        {
            _timeSinceUngrounded += deltaTime;

            if (_requestedMovement.sqrMagnitude > 0f)
            {
                var pm = Vector3.ProjectOnPlane(_requestedMovement, motor.CharacterUp)
                          * _requestedMovement.magnitude;
                var cpv = Vector3.ProjectOnPlane(currentVelocity, motor.CharacterUp);
                var mf = pm * airAccelaration * deltaTime;

                if (cpv.magnitude < airSpeed)
                    mf = Vector3.ClampMagnitude(cpv + mf, airSpeed) - cpv;
                else if (Vector3.Dot(cpv, mf) > 0f)
                    mf = Vector3.ProjectOnPlane(mf, cpv.normalized);

                if (motor.GroundingStatus.FoundAnyGround
                    && Vector3.Dot(mf, currentVelocity + mf) > 0f)
                {
                    var on = Vector3.Cross(motor.CharacterUp,
                        Vector3.Cross(motor.CharacterUp,
                            motor.GroundingStatus.GroundNormal)).normalized;
                    mf = Vector3.ProjectOnPlane(mf, on);
                }
                currentVelocity += mf;
            }

            float eg = gravity;
            if (_requestedSustainedJump && Vector3.Dot(currentVelocity, motor.CharacterUp) > 0f)
                eg *= jumpSustainGravity;
            currentVelocity += motor.CharacterUp * eg * deltaTime;
        }

        HandleJump(ref currentVelocity, deltaTime, isWallRun: false);

        if (grapplingHook != null && grapplingHook.IsGrappling)
            grapplingHook.ApplyGrappleVelocity(ref currentVelocity, deltaTime,
                _requestedMovement, airAccelaration);
    }

    // ── Wall run velocity ─────────────────────────────────────────────────────

    private void UpdateWallRunVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        // Keep all wall-run motion in the wall's plane. This removes both the
        // incoming impact component and any later velocity away from the wall.
        currentVelocity = Vector3.ProjectOnPlane(currentVelocity, _wallNormal);

        _wallRunTimer += deltaTime;
        float duration = Mathf.Max(ActiveWallRunDuration, Mathf.Epsilon);
        float runT = Mathf.Clamp01(_wallRunTimer / duration);

        if (runT < 1f)
        {
            // Decay from entry speed instead of imposing a fixed wall-run speed.
            // The direction is fixed on entry so wall jumps stay predictable.
            _wallRunCurrentSpeed = Mathf.MoveTowards(_wallRunCurrentSpeed, 0f,
                Mathf.Max(0f, ActiveWallRunDecayRate) * deltaTime);
            currentVelocity = _wallRunDirection * _wallRunCurrentSpeed;

            // Horizontal runs rise then fall along a subtle parabola. Its
            // derivative supplies velocity, so the curve is physical rather
            // than a positional teleport.
            if (!_wallRunIsVertical && _wallRunArcUp.sqrMagnitude > 0f)
            {
                float arcVelocity = (4f * wallRunArcHeight / duration) * (1f - 2f * runT);
                currentVelocity += _wallRunArcUp * arcVelocity;
            }
        }
        else
        {
            _wallRunFadeTimer += deltaTime;
            float fadeT = Mathf.Clamp01(_wallRunFadeTimer / Mathf.Max(wallRunGravityFadeTime, Mathf.Epsilon));
            if (fadeT >= 1f) { ExitWallRun(); return; }

            // Reintroduce gravity gradually, while remaining constrained to the
            // wall plane until the wall run has fully released.
            currentVelocity += Vector3.ProjectOnPlane(motor.CharacterUp * gravity, _wallNormal)
                               * fadeT * deltaTime;
        }

        _state.WallNormal = _wallNormal;
    }

    // ── Jump ──────────────────────────────────────────────────────────────────

    private void HandleJump(ref Vector3 currentVelocity, float deltaTime, bool isWallRun)
    {
        if (!_requestedJump) return;

        if (_state.Stance is Stance.Prone)
        {
            _requestedJump = false;
            return;
        }

        if (isWallRun)
        {
            _requestedJump = false;
            var runDir = Vector3.ProjectOnPlane(_wallRunDirection, motor.CharacterUp).normalized;
            if (runDir.sqrMagnitude <= 0.01f)
                runDir = Vector3.ProjectOnPlane(currentVelocity, motor.CharacterUp).normalized;
            ExitWallRun();
            motor.ForceUnground(0f);
            _ungroundedDueToJump = true;

            currentVelocity = _wallNormal * wallJumpWallForce
                            + motor.CharacterUp * wallJumpUpForce
                            + runDir * wallJumpForwardForce;
            return;
        }

        bool grounded = motor.GroundingStatus.IsStableOnGround;
        bool canCoyote = _timeSinceUngrounded < coyoteTime && !_ungroundedDueToJump;

        if (grounded || canCoyote)
        {
            _requestedJump = false;
            _requestedCrouch = false;
            _requestedCrouchInAir = false;
            motor.ForceUnground(0f);
            _ungroundedDueToJump = true;
            _doubleJumpAvailable = doubleJumpEnabled;

            float cv = Vector3.Dot(currentVelocity, motor.CharacterUp);
            float tv = Mathf.Max(cv, jumpSpeed);
            currentVelocity += motor.CharacterUp * (tv - cv);
        }
        else if (doubleJumpEnabled && _doubleJumpAvailable)
        {
            Debug.Log("[DoubleJump] Executing");
            _requestedJump = false;
            _doubleJumpAvailable = false;
            motor.ForceUnground(0f);

            float cv = Vector3.Dot(currentVelocity, motor.CharacterUp);
            float tv = Mathf.Max(cv, jumpSpeed);
            currentVelocity += motor.CharacterUp * (tv - cv);
        }
        else
        {
            Debug.Log($"[DoubleJump] Failed — enabled:{doubleJumpEnabled} " +
                      $"available:{_doubleJumpAvailable} grounded:{motor.GroundingStatus.IsStableOnGround}");
            _timeSinceJumpRequested += deltaTime;
            _requestedJump = _timeSinceJumpRequested < coyoteTime;
        }
    }

    // ── Wall run enter / exit ─────────────────────────────────────────────────

    private bool WantsVerticalWallRun(out float lookPitch)
    {
        Vector3 lookForward = _requestedRotation * Vector3.forward;
        float upDot = Mathf.Clamp(Vector3.Dot(lookForward.normalized, motor.CharacterUp), -1f, 1f);
        lookPitch = Mathf.Asin(upDot) * Mathf.Rad2Deg;
        return lookPitch >= wallRunPitchThreshold;
    }

    private void EnterWallRun(Vector3 wallNormal, bool isVerticalWallRun)
    {
        _wallNormal = wallNormal;
        _state.Stance = Stance.WallRun;
        _doubleJumpAvailable = doubleJumpEnabled;
        _wallRunTimer = 0f;
        _wallRunFadeTimer = 0f;
        _wallRunStartPosition = motor.TransientPosition;

        // ── Direction decided by look pitch, not velocity ─────────────────────
        var lookForward = _requestedRotation * Vector3.forward;
        var horizForward = Vector3.ProjectOnPlane(lookForward, motor.CharacterUp).normalized;
        WantsVerticalWallRun(out float pitchAngle);
        _wallRunIsVertical = isVerticalWallRun;

        if (_wallRunIsVertical)
        {
            var upOnWall = Vector3.ProjectOnPlane(motor.CharacterUp, wallNormal).normalized;
            _wallRunDirection = upOnWall.sqrMagnitude > 0.01f ? upOnWall : motor.CharacterUp;
        }
        else
        {
            // Horizontal: pick the along-wall direction closest to where the player is looking
            var alongWallA = Vector3.Cross(wallNormal, motor.CharacterUp).normalized;
            var alongWallB = -alongWallA;
            _wallRunDirection = Vector3.Dot(horizForward, alongWallA) >
                                Vector3.Dot(horizForward, alongWallB)
                                ? alongWallA : alongWallB;
        }

        _wallRunArcUp = _wallRunIsVertical
            ? Vector3.zero
            : Vector3.ProjectOnPlane(motor.CharacterUp, wallNormal).normalized;

        // Capture only the component that can travel along the wall. The first
        // wall-run update boosts and decelerates this velocity from there.
        Vector3 entryVelocityOnWall = Vector3.ProjectOnPlane(_tempState.Velocity, wallNormal);
        _wallRunStartSpeed = _wallRunIsVertical
            ? verticalWallRunStartSpeed
            : Mathf.Max(entryVelocityOnWall.magnitude, wallRunMinEntrySpeed)
              * horizontalWallRunEntrySpeedMultiplier;
        _wallRunCurrentSpeed = _wallRunStartSpeed;

        Debug.Log($"[WallRun] Enter — pitchAngle:{pitchAngle:F1} " +
                  $"vertical:{_wallRunIsVertical} dir:{_wallRunDirection} " +
                  $"entrySpeed:{_wallRunStartSpeed:F1} normal:{wallNormal}");
    }

    private void ExitWallRun()
    {
        if (_state.Stance is Stance.WallRun) _state.Stance = Stance.Stand;
        _lastWallNormal = _wallNormal;
        _wallRunCooldownTimer = wallRunCooldown;
        _wallNormal = Vector3.zero;
        _wallRunDirection = Vector3.zero;
        _wallRunArcUp = Vector3.zero;
        _wallRunStartSpeed = 0f;
        _wallRunCurrentSpeed = 0f;
        _wallRunTimer = 0f;
        _wallRunFadeTimer = 0f;
        Debug.Log("[WallRun] Exited");
    }

    // ── Prone ─────────────────────────────────────────────────────────────────

    private void EnterProne()
    {
        _state.Stance = Stance.Prone;
        motor.SetCapsuleDimensions(motor.Capsule.radius, proneHeight, proneHeight * 0.5f);
    }

    private void ExitProne()
    {
        motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
        var pos = motor.TransientPosition;
        var rot = motor.TransientRotation;
        if (motor.CharacterOverlap(pos, rot, _uncrouchOverlapResults,
            motor.CollidableLayers, QueryTriggerInteraction.Ignore) > 0)
        {
            motor.SetCapsuleDimensions(motor.Capsule.radius, proneHeight, proneHeight * 0.5f);
            return;
        }
        _state.Stance = Stance.Crouch;
    }

    // ── Crouch ────────────────────────────────────────────────────────────────

    private void EnterCrouch()
    {
        _state.Stance = Stance.Crouch;
        motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
    }

    private void TryStand()
    {
        motor.SetCapsuleDimensions(motor.Capsule.radius, standHeight, standHeight * 0.5f);
        var pos = motor.TransientPosition;
        var rot = motor.TransientRotation;
        if (motor.CharacterOverlap(pos, rot, _uncrouchOverlapResults,
            motor.CollidableLayers, QueryTriggerInteraction.Ignore) > 0)
        {
            _requestedCrouch = true;
            motor.SetCapsuleDimensions(motor.Capsule.radius, crouchHeight, crouchHeight * 0.5f);
        }
        else _state.Stance = Stance.Stand;
    }

    // ── ICharacterController ──────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawWallRunDebugPath || _wallRunDirection.sqrMagnitude <= 0.01f)
            return;

        int segments = Mathf.Max(2, wallRunDebugPathSegments);
        float duration = Mathf.Max(ActiveWallRunDuration, Mathf.Epsilon);
        float decayRate = Mathf.Max(0f, ActiveWallRunDecayRate);
        Vector3 previous = _wallRunStartPosition;
        Gizmos.color = Color.cyan;

        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float elapsed = duration * t;

            // Integral of the speed lost per second in UpdateWallRunVelocity.
            float travelTime = decayRate > Mathf.Epsilon
                ? Mathf.Min(elapsed, _wallRunStartSpeed / decayRate)
                : elapsed;
            float distance = _wallRunStartSpeed * travelTime - 0.5f * decayRate * travelTime * travelTime;
            float arcOffset = _wallRunIsVertical ? 0f : 4f * wallRunArcHeight * t * (1f - t);
            Vector3 point = _wallRunStartPosition + _wallRunDirection * distance + _wallRunArcUp * arcOffset;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawRay(_wallRunStartPosition, _wallNormal);
    }

    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }
    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport r)
    { }

    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport r)
    {
        float angle = Vector3.Angle(hitNormal, Vector3.up);
        if (Mathf.Abs(angle - 90f) <= wallNormalTolerance)
        {
            _touchingWall = true;
            _currentWallNormal = hitNormal;
            Debug.Log($"[WallRun] Wall hit — angle:{angle:F1} normal:{hitNormal}");
        }
    }

    public void PostGroundingUpdate(float deltaTime) { }

    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal,
        Vector3 hitPoint, Vector3 atPos, Quaternion atRot, ref HitStabilityReport r)
    { }

    public void SetPosition(Vector3 position, bool killVelocity = true)
    {
        motor.SetPosition(position);
        if (killVelocity) motor.BaseVelocity = Vector3.zero;
    }
}
