using System.Collections;
using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;

public enum StanceCommand { None, Crouch, Prone, Sprint }
enum StanceGoal { Stand, Crouch, Prone, Sprint }

public enum Stance { Stand, Crouch, Slide, Sprint, WallRun, Prone }

public struct CharacterInput
{
    public Quaternion Rotation;
    public Vector2 Move;
    public bool Jump;
    public bool JumpSustain;
    public bool Sprint;
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

public sealed class TimedPlayerStatEffect
{
    public string sourceID;
    public string itemSourceID;
    public string displayName;
    public float remainingSeconds;
    public List<AugmentStatOverride> statOverrides;
    public ItemDefinition sourceItem;
    public HealthManager healthManager;
    public bool hasTargetBodyPart;
    public BodyPart targetBodyPart;
    public bool triggersLifecycleOnExpiry;
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

    [Header("Saturation & Stamina")]
    [SerializeField, Min(1f)] private float maxSaturation = 100f;
    [SerializeField, Min(0f), Tooltip("Saturation lost per second.")]
    private float saturationDepletionRate = 0.01f;
    [SerializeField, Min(1f)] private float maxStamina = 100f;
    [SerializeField, Min(0f), Tooltip("Stamina lost per second while sprinting.")]
    private float sprintStamina = 10f;
    [SerializeField, Min(0f), Tooltip("Stamina spent by each successful jump, including wall and double jumps.")]
    private float jumpStamina = 15f;
    [SerializeField, Min(0f), Tooltip("Stamina restored per second while stationary.")]
    private float staminaRegen = 20f;
    [SerializeField, Min(0f), Tooltip("Stamina restored per second while movement input is held.")]
    private float movingStaminaRegen = 10f;
    [SerializeField, Min(0f), Tooltip("Delay after spending stamina before regeneration starts.")]
    private float staminaRegenDelay = 0.75f;

    [Header("Organic Screen Feedback")]
    [SerializeField] private bool enableHealthScreenFeedback = true;
    [SerializeField] private bool enableStaminaScreenFeedback = true;
    [SerializeField] private Color criticalHealthVignetteColor = new(0.55f, 0f, 0.04f, 1f);
    [SerializeField] private Color criticalHealthOverlayColor = new(1f, 0.12f, 0.12f, 0.58f);
    [SerializeField, Range(0f, 1f)] private float criticalHealthVignetteIntensity = 0.48f;
    [SerializeField, Min(0.01f)] private float organicFeedbackResponse = 8f;

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
    [Min(0f), Tooltip("Entry vertical speed at or below this magnitude is discarded during a horizontal wall run.")]
    [SerializeField] private float horizontalWallRunVerticalVelocityDeadZone = 4f;
    [Range(0f, 1f), Tooltip("Fraction of entry vertical velocity retained above the dead zone. It never contributes to horizontal wall-run speed.")]
    [SerializeField] private float horizontalWallRunVerticalVelocityRetention = 0.15f;
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
    [Min(0f), Tooltip("Fastest allowed downward speed when entering a vertical wall run. Falling faster cannot reattach to the wall.")]
    [SerializeField] private float verticalWallRunMaxDownwardSpeed = 5f;
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
    // KKC consumes input during FixedUpdate. Commands are latched by Player's
    // input-action callbacks, so a press cannot be lost between simulation ticks.
    // Assigning this value replaces any older unconsumed command: latest press wins.
    private StanceCommand _queuedStanceCommand;
    private StanceGoal _stanceGoal;
    private bool _pendingProne;
    private bool _pendingStand;
    private bool _sprintSuppressed;
    private bool _requestedCrouchInAir;
    private bool _requestedSprint;
    private bool _forcedCrawl;
    private bool _staminaSprintLocked;

    private float _currentSaturation;
    private float _currentStamina;
    private float _staminaRegenCooldown;

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
    private float _horizontalWallRunVerticalVelocity;
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
    private readonly List<AugmentStatOverride> _activeAugmentOverrides = new();
    private readonly List<TimedPlayerStatEffect> _timedStatEffects = new();

    // OnMovementHit sets these; read at start of BeforeCharacterUpdate
    private bool _touchingWall;
    private Vector3 _currentWallNormal;
    private readonly RaycastHit[] _wallProbeHits = new RaycastHit[16];

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
    public float CurrentSaturation => _currentSaturation;
    public float MaxSaturation => maxSaturation;
    public float CurrentStamina => _currentStamina;
    public float MaxStamina => maxStamina;
    public bool HealthScreenFeedbackEnabled => enableHealthScreenFeedback;
    public bool StaminaScreenFeedbackEnabled => enableStaminaScreenFeedback;
    public Color CriticalHealthVignetteColor => criticalHealthVignetteColor;
    public Color CriticalHealthOverlayColor => criticalHealthOverlayColor;
    public float CriticalHealthVignetteIntensity => criticalHealthVignetteIntensity;
    public float OrganicFeedbackResponse => organicFeedbackResponse;
    public IReadOnlyList<TimedPlayerStatEffect> ActiveTimedEffects => _timedStatEffects;

    /// <summary>
    /// Hunger only starts penalising regeneration below 20% saturation. The
    /// multiplier blends from 1 at 20% to 0.5 at empty.
    /// </summary>
    public float SaturationEfficiency
    {
        get
        {
            float ratio = maxSaturation > 0f ? _currentSaturation / maxSaturation : 0f;
            return ratio >= .2f ? 1f : Mathf.Lerp(.5f, 1f, Mathf.Clamp01(ratio / .2f));
        }
    }

    public void QueueStanceCommand(StanceCommand command)
    {
        if (command is not StanceCommand.None)
            _queuedStanceCommand = command;
    }

    public void SetForcedCrawl(bool forced)
    {
        _forcedCrawl = forced;
        if (_forcedCrawl && _state.Stance is not Stance.Prone)
            EnterProne();
    }

    public void Initialize()
    {
        CaptureBaseAugmentStats();
        _currentSaturation = maxSaturation;
        _currentStamina = maxStamina;
        _state.Stance = Stance.Stand;
        _stanceGoal = StanceGoal.Stand;
        _lastState = _state;
        _uncrouchOverlapResults = new Collider[8];
        _doubleJumpAvailable = doubleJumpEnabled;
        motor.CharacterController = this;
    }

    public void ApplyAugmentStatOverrides(IEnumerable<AugmentStatOverride> overrides)
    {
        CaptureBaseAugmentStats();
        _activeAugmentOverrides.Clear();
        if (overrides != null) _activeAugmentOverrides.AddRange(overrides);
        RebuildEffectiveStats();
    }

    public void RestoreSaturation(float amount)
    {
        ModifySaturation(Mathf.Max(0f, amount));
    }

    public void ModifySaturation(float amount)
    {
        _currentSaturation = Mathf.Clamp(_currentSaturation + amount, 0f, maxSaturation);
    }

    public void ModifyStamina(float amount)
    {
        _currentStamina = Mathf.Clamp(_currentStamina + amount, 0f, maxStamina);
    }

    public void ApplyTimedConsumableEffect(ItemDefinition item, HealthManager sourceHealthManager = null,
        bool hasTargetBodyPart = false, BodyPart targetBodyPart = default)
    {
        if (item == null) return;

        HealthManager resolvedHealth = sourceHealthManager
            ?? GetComponent<HealthManager>()
            ?? GetComponentInParent<HealthManager>();
        string itemSourceID = string.IsNullOrWhiteSpace(item.itemID) ? item.name : item.itemID;
        _timedStatEffects.RemoveAll(effect => effect.itemSourceID == itemSourceID);

        ApplyLifecycleEffects(item, ConsumableEffectTiming.AfterUse, resolvedHealth,
            hasTargetBodyPart, targetBodyPart, itemSourceID);

        bool hasExpiryEffects = item.lifecycleEffects != null
            && item.lifecycleEffects.Exists(effect =>
                effect != null && effect.timing == ConsumableEffectTiming.AfterTimedEffectExpires);
        var primaryEffects = new List<TimedPlayerStatEffect>();
        if (item.timedStatEffects != null && item.timedStatEffects.Count > 0)
        {
            if (item.timedEffectsShareTimer)
            {
                if (item.effectDuration > 0f)
                    primaryEffects.Add(CreateTimedEffect(item, itemSourceID,
                        $"{itemSourceID}:timed", item.effectDuration, item.timedStatEffects,
                        resolvedHealth, hasTargetBodyPart, targetBodyPart));
            }
            else
            {
                for (int i = 0; i < item.timedStatEffects.Count; i++)
                {
                    AugmentStatOverride modifier = item.timedStatEffects[i];
                    if (modifier == null || modifier.duration <= 0f) continue;
                    primaryEffects.Add(CreateTimedEffect(item, itemSourceID,
                        $"{itemSourceID}:timed:{i}", modifier.duration,
                        new List<AugmentStatOverride> { modifier }, resolvedHealth,
                        hasTargetBodyPart, targetBodyPart, modifier.stat.ToString()));
                }
            }
        }

        if (primaryEffects.Count == 0 && hasExpiryEffects && item.effectDuration > 0f)
            primaryEffects.Add(CreateTimedEffect(item, itemSourceID,
                $"{itemSourceID}:expiry", item.effectDuration, null, resolvedHealth,
                hasTargetBodyPart, targetBodyPart));

        TimedPlayerStatEffect lastPrimary = null;
        foreach (TimedPlayerStatEffect effect in primaryEffects)
        {
            _timedStatEffects.Add(effect);
            if (lastPrimary == null || effect.remainingSeconds >= lastPrimary.remainingSeconds)
                lastPrimary = effect;
        }
        if (lastPrimary != null) lastPrimary.triggersLifecycleOnExpiry = hasExpiryEffects;
        RebuildEffectiveStats();
    }

    private TimedPlayerStatEffect CreateTimedEffect(ItemDefinition item, string itemSourceID,
        string sourceID, float duration, List<AugmentStatOverride> modifiers,
        HealthManager resolvedHealth, bool hasTargetBodyPart, BodyPart targetBodyPart,
        string suffix = null)
    {
        string itemName = string.IsNullOrWhiteSpace(item.displayName) ? item.name : item.displayName;
        return new TimedPlayerStatEffect
        {
            sourceID = sourceID,
            itemSourceID = itemSourceID,
            displayName = string.IsNullOrWhiteSpace(suffix) ? itemName : $"{itemName} — {suffix}",
            remainingSeconds = duration,
            statOverrides = modifiers,
            sourceItem = item,
            healthManager = resolvedHealth,
            hasTargetBodyPart = hasTargetBodyPart,
            targetBodyPart = targetBodyPart
        };
    }

    private bool ApplyLifecycleEffects(ItemDefinition item, ConsumableEffectTiming timing,
        HealthManager resolvedHealth, bool hasTargetBodyPart, BodyPart targetBodyPart,
        string itemSourceID)
    {
        if (item?.lifecycleEffects == null) return false;
        bool addedTimedModifier = false;
        var sharedModifiers = new List<AugmentStatOverride>();
        for (int i = 0; i < item.lifecycleEffects.Count; i++)
        {
            ConsumableLifecycleEffect effect = item.lifecycleEffects[i];
            if (effect == null || effect.timing != timing) continue;
            switch (effect.changes)
            {
                case ConsumableLifecycleChange.PlayerStat:
                    if (effect.playerStatModifier == null) break;
                    if (item.lifecycleEffectsShareTimer)
                    {
                        sharedModifiers.Add(effect.playerStatModifier);
                    }
                    else if (effect.playerStatModifier.duration > 0f)
                    {
                        _timedStatEffects.Add(CreateTimedEffect(item, itemSourceID,
                            $"{itemSourceID}:lifecycle:{timing}:{i}", effect.playerStatModifier.duration,
                            new List<AugmentStatOverride> { effect.playerStatModifier }, resolvedHealth,
                            hasTargetBodyPart, targetBodyPart, effect.playerStatModifier.stat.ToString()));
                        addedTimedModifier = true;
                    }
                    break;
                case ConsumableLifecycleChange.Saturation:
                    ModifySaturation(effect.amount);
                    break;
                case ConsumableLifecycleChange.Stamina:
                    ModifyStamina(effect.amount);
                    break;
                default:
                    resolvedHealth?.ApplyConsumableLifecycleEffect(effect,
                        hasTargetBodyPart, targetBodyPart);
                    break;
            }
        }

        if (sharedModifiers.Count > 0 && item.lifecycleEffectDuration > 0f)
        {
            _timedStatEffects.Add(CreateTimedEffect(item, itemSourceID,
                $"{itemSourceID}:lifecycle:{timing}", item.lifecycleEffectDuration,
                sharedModifiers, resolvedHealth, hasTargetBodyPart, targetBodyPart,
                "Lifecycle"));
            addedTimedModifier = true;
        }
        return addedTimedModifier;
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
            { PlayerAugmentStat.VerticalWallRunMaxDownwardSpeed, verticalWallRunMaxDownwardSpeed },
            { PlayerAugmentStat.HorizontalWallRunVerticalVelocityDeadZone, horizontalWallRunVerticalVelocityDeadZone },
            { PlayerAugmentStat.HorizontalWallRunVerticalVelocityRetention, horizontalWallRunVerticalVelocityRetention },
            { PlayerAugmentStat.WallRunArcHeight, wallRunArcHeight }, { PlayerAugmentStat.ProneHeight, proneHeight },
            { PlayerAugmentStat.ProneSpeed, proneSpeed }, { PlayerAugmentStat.ProneResponse, proneResponse },
            { PlayerAugmentStat.MaxSaturation, maxSaturation },
            { PlayerAugmentStat.SaturationDepletionRate, saturationDepletionRate },
            { PlayerAugmentStat.MaxStamina, maxStamina }, { PlayerAugmentStat.SprintStamina, sprintStamina },
            { PlayerAugmentStat.JumpStamina, jumpStamina }, { PlayerAugmentStat.StaminaRegen, staminaRegen },
            { PlayerAugmentStat.StaminaRegenDelay, staminaRegenDelay },
            { PlayerAugmentStat.MovingStaminaRegen, movingStaminaRegen }
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

    private void RebuildEffectiveStats()
    {
        float saturationRatio = maxSaturation > 0f ? _currentSaturation / maxSaturation : 1f;
        float staminaRatio = maxStamina > 0f ? _currentStamina / maxStamina : 1f;
        var effectiveFloatStats = new Dictionary<PlayerAugmentStat, float>(_baseFloatStats);
        RestoreBaseAugmentStats();
        foreach (AugmentStatOverride statOverride in _activeAugmentOverrides)
            ApplyAugmentStatOverride(statOverride, effectiveFloatStats);
        foreach (TimedPlayerStatEffect effect in _timedStatEffects)
            if (effect.statOverrides != null)
                foreach (AugmentStatOverride statOverride in effect.statOverrides)
                    ApplyAugmentStatOverride(statOverride, effectiveFloatStats);
        foreach (var stat in effectiveFloatStats)
            SetAugmentFloatStat(stat.Key, stat.Value);
        _currentSaturation = Mathf.Clamp01(saturationRatio) * maxSaturation;
        _currentStamina = Mathf.Clamp01(staminaRatio) * maxStamina;
        _doubleJumpAvailable = doubleJumpEnabled;
    }

    private void ApplyAugmentStatOverride(AugmentStatOverride statOverride,
        Dictionary<PlayerAugmentStat, float> effectiveFloatStats)
    {
        if (statOverride == null) return;
        if (_baseToggleStats.ContainsKey(statOverride.stat))
            SetAugmentToggleStat(statOverride.stat, statOverride.boolValue);
        else if (effectiveFloatStats.TryGetValue(statOverride.stat, out float currentValue))
        {
            float percentage = Mathf.Abs(statOverride.value) * .01f;
            effectiveFloatStats[statOverride.stat] = statOverride.mode switch
            {
                PlayerStatModifierMode.PercentageIncrease => currentValue * (1f + percentage),
                PlayerStatModifierMode.PercentageDecrease => currentValue * Mathf.Max(0f, 1f - percentage),
                _ => statOverride.value
            };
        }
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
            case PlayerAugmentStat.VerticalWallRunMaxDownwardSpeed: verticalWallRunMaxDownwardSpeed = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.HorizontalWallRunVerticalVelocityDeadZone: horizontalWallRunVerticalVelocityDeadZone = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.HorizontalWallRunVerticalVelocityRetention: horizontalWallRunVerticalVelocityRetention = Mathf.Clamp01(value); break;
            case PlayerAugmentStat.WallRunArcHeight: wallRunArcHeight = value; break;
            case PlayerAugmentStat.ProneHeight: proneHeight = value; break;
            case PlayerAugmentStat.ProneSpeed: proneSpeed = value; break;
            case PlayerAugmentStat.ProneResponse: proneResponse = value; break;
            case PlayerAugmentStat.MaxSaturation: maxSaturation = Mathf.Max(1f, value); break;
            case PlayerAugmentStat.SaturationDepletionRate: saturationDepletionRate = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.MaxStamina: maxStamina = Mathf.Max(1f, value); break;
            case PlayerAugmentStat.SprintStamina: sprintStamina = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.JumpStamina: jumpStamina = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.StaminaRegen: staminaRegen = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.StaminaRegenDelay: staminaRegenDelay = Mathf.Max(0f, value); break;
            case PlayerAugmentStat.MovingStaminaRegen: movingStaminaRegen = Mathf.Max(0f, value); break;
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

        if (!input.Sprint)
        {
            _sprintSuppressed = false;
            _staminaSprintLocked = false;
        }
        _requestedSprint = sprintEnabled && input.Sprint && !_staminaSprintLocked && _currentStamina > 0f;
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    public void UpdateBody(float deltaTime)
    {
        UpdateVitals(deltaTime);
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

    private void UpdateVitals(float deltaTime)
    {
        _currentSaturation = Mathf.Max(0f, _currentSaturation - saturationDepletionRate * deltaTime);

        if (_state.Stance is Stance.Sprint)
        {
            _currentStamina = Mathf.Max(0f, _currentStamina - sprintStamina * deltaTime);
            _staminaRegenCooldown = staminaRegenDelay;
            if (_currentStamina <= 0f)
            {
                _staminaSprintLocked = true;
                _requestedSprint = false;
                _state.Stance = Stance.Stand;
            }
        }
        else if (_staminaRegenCooldown > 0f)
        {
            _staminaRegenCooldown = Mathf.Max(0f, _staminaRegenCooldown - deltaTime);
        }
        else
        {
            bool moving = _requestedMovement.sqrMagnitude > .01f;
            float activeRegen = moving ? movingStaminaRegen : staminaRegen;
            _currentStamina = Mathf.Min(maxStamina,
                _currentStamina + activeRegen * SaturationEfficiency * deltaTime);
        }

        List<TimedPlayerStatEffect> expiredEffects = null;
        for (int i = _timedStatEffects.Count - 1; i >= 0; i--)
        {
            _timedStatEffects[i].remainingSeconds -= deltaTime;
            if (_timedStatEffects[i].remainingSeconds > 0f) continue;
            expiredEffects ??= new List<TimedPlayerStatEffect>();
            expiredEffects.Add(_timedStatEffects[i]);
            _timedStatEffects.RemoveAt(i);
        }
        if (expiredEffects != null)
        {
            RebuildEffectiveStats();
            foreach (TimedPlayerStatEffect expired in expiredEffects)
                if (expired.triggersLifecycleOnExpiry)
                    ApplyLifecycleEffects(expired.sourceItem,
                    ConsumableEffectTiming.AfterTimedEffectExpires, expired.healthManager,
                    expired.hasTargetBodyPart, expired.targetBodyPart, expired.itemSourceID);
            RebuildEffectiveStats();
        }
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

        ProcessStanceInput();

        // ── Wall run cooldown ─────────────────────────────────────────────────
        if (_wallRunCooldownTimer > 0f) _wallRunCooldownTimer -= deltaTime;

        // ── Wall run: continuous wall detection via raycast ───────────────────
        if (_state.Stance is Stance.WallRun)
        {
            // Raycast toward the wall every frame — exit if wall is gone, angle is
            // no longer valid, or the surface hit is no longer the same wall
            // (catches corners, where a stale raycast can clip an adjacent surface).
            bool wallStillPresent = false;
            if (TryGetWallSurface(transform.position, -_wallNormal, wallDetectRadius + 0.1f,
                out RaycastHit wallCheckHit))
            {
                float checkAngle = Vector3.Angle(wallCheckHit.normal, Vector3.up);
                bool validAngle = Mathf.Abs(checkAngle - 90f) <= wallNormalTolerance;
                bool sameWall = Vector3.Dot(wallCheckHit.normal, _wallNormal) > 0.9f; 
                wallStillPresent = validAngle && sameWall;
            }

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
            && TryGetWallSurface(transform.position, transform.forward, wallDetectRadius + 0.1f,
                out RaycastHit wallHit))
        {
            candidateWallNormal = wallHit.normal;
            hasWallContact = true;
        }

        // Confirm the candidate is a real, flat wall surface rather than a
        // one-off corner/edge collision normal (which can point almost anywhere).
        // Re-probe straight toward where the wall should be and require a
        // closely matching surface. This is what prevents corner clips from
        // launching a full wall run into open air.
        if (hasWallContact)
        {
            if (TryGetWallSurface(transform.position, -candidateWallNormal, wallDetectRadius + 0.2f,
                out RaycastHit confirmHit))
            {
                float confirmAngle = Vector3.Angle(confirmHit.normal, Vector3.up);
                bool confirmValidAngle = Mathf.Abs(confirmAngle - 90f) <= wallNormalTolerance;
                bool confirmSameWall = Vector3.Dot(confirmHit.normal, candidateWallNormal) > 0.9f;
                if (!confirmValidAngle || !confirmSameWall) hasWallContact = false;
            }
            else
            {
                hasWallContact = false; // nothing there — spurious contact
            }
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
            float verticalVelocity = Vector3.Dot(_tempState.Velocity, motor.CharacterUp);
            bool fallingWithinVerticalEntryRange = verticalVelocity >= -verticalWallRunMaxDownwardSpeed;

            Debug.Log($"[WallRun] Touch — angle:{wallAngle:F1} isVWall:{isVWall} " +
                      $"speed:{planarVel.magnitude:F1} pitch:{lookPitch:F1} " +
                      $"vertical:{wantsVerticalWallRun} verticalSpeed:{verticalVelocity:F1} " +
                      $"fallRange:{fallingWithinVerticalEntryRange} cooldown:{onCooldown}");

            // Horizontal runs retain their speed gate. Vertical runs instead use
            // the upward look gate, so they can start from a standstill on a wall.
            bool validEntryMotion = wantsVerticalWallRun
                ? fallingWithinVerticalEntryRange
                : hasHorizontalEntrySpeed;
            if (isVWall && validEntryMotion && !onCooldown)
            {
                Debug.Log("[WallRun] ENTERING");
                EnterWallRun(candidateWallNormal, wantsVerticalWallRun);
            }
        }
    }

    // ── Post ground update ────────────────────────────────────────────────────

    private void ProcessStanceInput()
    {
        if (_forcedCrawl)
        {
            _queuedStanceCommand = StanceCommand.None;
            _pendingProne = false;
            _pendingStand = false;
            _requestedCrouch = true;
            _stanceGoal = StanceGoal.Prone;
            if (_state.Stance is not Stance.Prone) EnterProne();
            return;
        }

        StanceCommand command = _queuedStanceCommand;
        _queuedStanceCommand = StanceCommand.None;

        // Each command replaces unfinished work from an older command. The
        // movement state still advances through mandatory intermediate stances.
        switch (command)
        {
            case StanceCommand.Crouch:
                _pendingProne = false;
                _pendingStand = false;
                _stanceGoal = _stanceGoal is StanceGoal.Crouch
                    ? StanceGoal.Stand
                    : StanceGoal.Crouch;
                if (_stanceGoal is StanceGoal.Stand) CommitStandCommand();
                else CommitCrouchCommand();
                break;
            case StanceCommand.Prone:
                _pendingProne = false;
                _pendingStand = false;
                _stanceGoal = _stanceGoal is StanceGoal.Prone
                    ? StanceGoal.Stand
                    : StanceGoal.Prone;
                if (_stanceGoal is StanceGoal.Stand) CommitStandCommand();
                else CommitProneCommand();
                break;
            case StanceCommand.Sprint:
                _pendingProne = false;
                _pendingStand = false;
                _stanceGoal = StanceGoal.Sprint;
                _sprintSuppressed = false;
                CommitSprintCommand();
                break;
            default:
                if (_requestedJump && _state.Stance is Stance.Prone)
                {
                    _requestedJump = false;
                    _pendingProne = false;
                    _pendingStand = true;
                    _requestedCrouch = false;
                    _stanceGoal = StanceGoal.Stand;
                    ExitProne(); // Prone jump is a stand-up command, never an actual jump.
                }
                else
                {
                    // A prone command issued just before landing remains the
                    // active goal until KKC reports stable ground.
                    if (_stanceGoal is StanceGoal.Prone
                        && !_pendingProne
                        && _state.Stance is not Stance.Prone)
                        CommitProneCommand();

                    ResolvePendingStance();
                    CommitSprintCommand();
                }
                break;
        }
    }

    private void CommitCrouchCommand()
    {
        _sprintSuppressed = true;
        if (!_requestedCrouch)
            _requestedCrouchInAir = !_state.Grounded;

        switch (_state.Stance)
        {
            case Stance.Sprint:
                _requestedCrouch = true;
                EnterCrouch();
                _state.Stance = Stance.Slide; // sprint crouch always commits to slide
                break;
            case Stance.Prone:
                _requestedCrouch = true;
                ExitProne();
                break;
            case Stance.Slide:
                _requestedCrouch = true;
                _state.Stance = Stance.Crouch;
                break;
            case Stance.WallRun:
                _requestedCrouch = true;
                ExitWallRun();
                EnterCrouch();
                break;
            case Stance.Crouch:
                _requestedCrouch = true;
                break;
            default:
                _requestedCrouch = true;
                EnterCrouch();
                break;
        }
    }

    private void CommitStandCommand()
    {
        _requestedCrouch = false;
        switch (_state.Stance)
        {
            case Stance.Prone:
                _pendingStand = true;
                ExitProne(); // prone → crouch → stand
                break;
            case Stance.Slide:
                _state.Stance = Stance.Crouch;
                _pendingStand = true;
                break;
            case Stance.Crouch:
                TryStand();
                break;
            case Stance.WallRun:
                ExitWallRun();
                TryStand();
                break;
            case Stance.Sprint:
                _state.Stance = Stance.Stand;
                break;
        }
    }

    private void CommitProneCommand()
    {
        // KKC must report stable ground before changing into the prone capsule.
        // The caller retains the prone goal if this cannot happen yet.
        if (!_state.Grounded) return;

        _sprintSuppressed = true;
        _requestedCrouch = true;
        switch (_state.Stance)
        {
            case Stance.Sprint:
                EnterCrouch();
                _state.Stance = Stance.Slide; // sprint → slide → prone
                _pendingProne = true;
                break;
            case Stance.Stand:
                EnterCrouch(); // walk/stand → crouch → prone
                _pendingProne = true;
                break;
            case Stance.Crouch:
                EnterProne();
                break;
            case Stance.Slide:
                _pendingProne = true; // wait for slide velocity to finish
                break;
            case Stance.Prone:
                _requestedCrouch = false;
                _pendingStand = true;
                ExitProne();
                break;
            case Stance.WallRun:
                ExitWallRun();
                EnterCrouch();
                _pendingProne = true;
                break;
        }
    }

    private void ResolvePendingStance()
    {
        if (_pendingProne && _stanceGoal is StanceGoal.Prone
            && _state.Grounded && _state.Stance is Stance.Crouch)
        {
            EnterProne();
            _pendingProne = false;
        }

        if (_pendingStand && _stanceGoal is StanceGoal.Stand or StanceGoal.Sprint
            && _state.Stance is Stance.Crouch)
        {
            TryStand();
            if (_state.Stance is Stance.Stand) _pendingStand = false;
        }
    }

    private void CommitSprintCommand()
    {
        if (_sprintSuppressed) return;

        if (_requestedSprint)
        {
            switch (_state.Stance)
            {
                case Stance.Prone:
                    _requestedCrouch = true;
                    ExitProne(); // prone → crouch; held sprint advances next frame
                    return;
                case Stance.Crouch:
                    _requestedCrouch = false;
                    TryStand();
                    break;
                case Stance.Sprint:
                    if (!_state.Grounded || _requestedMovement.sqrMagnitude <= 0f)
                        _state.Stance = Stance.Stand;
                    return;
            }

            if (_state.Stance is Stance.Stand && _state.Grounded
                && _requestedMovement.sqrMagnitude > 0f)
                _state.Stance = Stance.Sprint;
        }
        else if (_state.Stance is Stance.Sprint)
        {
            _state.Stance = Stance.Stand;
        }
    }

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
        bool wantToStand = _stanceGoal is StanceGoal.Stand
            && !_requestedCrouch
            && !_pendingStand
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

            if (moving && crouching && !_pendingProne && (wasStanding || wasSprinting || wasInAir))
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
                currentVelocity += _wallRunArcUp
                    * (arcVelocity + _horizontalWallRunVerticalVelocity);
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
            if (!TrySpendJumpStamina()) return;
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
            if (!TrySpendJumpStamina()) return;
            _requestedCrouch = false;
            _requestedCrouchInAir = false;
            motor.ForceUnground(0f);
            _ungroundedDueToJump = true;
            _doubleJumpAvailable = doubleJumpEnabled;

            if (_state.Stance is Stance.Crouch or Stance.Slide)
                TryStand();

            float cv = Vector3.Dot(currentVelocity, motor.CharacterUp);
            float tv = Mathf.Max(cv, jumpSpeed);
            currentVelocity += motor.CharacterUp * (tv - cv);
        }
        else if (doubleJumpEnabled && _doubleJumpAvailable)
        {
            Debug.Log("[DoubleJump] Executing");
            _requestedJump = false;
            if (!TrySpendJumpStamina()) return;
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

    private bool TrySpendJumpStamina()
    {
        if (jumpStamina <= 0f) return true;
        if (_currentStamina + Mathf.Epsilon < jumpStamina) return false;
        _currentStamina = Mathf.Max(0f, _currentStamina - jumpStamina);
        _staminaRegenCooldown = staminaRegenDelay;
        return true;
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

        // Horizontal run speed comes exclusively from planar movement along the
        // wall. Vertical fall speed is handled separately so it can never be
        // converted into a horizontal launch.
        Vector3 planarEntryVelocity = Vector3.ProjectOnPlane(
            _tempState.Velocity, motor.CharacterUp);
        Vector3 planarVelocityOnWall = Vector3.ProjectOnPlane(planarEntryVelocity, wallNormal);
        float alongWallEntrySpeed = Mathf.Abs(Vector3.Dot(planarVelocityOnWall, _wallRunDirection));
        float entryVerticalVelocity = Vector3.Dot(_tempState.Velocity, motor.CharacterUp);
        _horizontalWallRunVerticalVelocity = 0f;
        if (!_wallRunIsVertical
            && Mathf.Abs(entryVerticalVelocity) > horizontalWallRunVerticalVelocityDeadZone)
        {
            float retainedMagnitude = (Mathf.Abs(entryVerticalVelocity)
                - horizontalWallRunVerticalVelocityDeadZone)
                * horizontalWallRunVerticalVelocityRetention;
            _horizontalWallRunVerticalVelocity = Mathf.Sign(entryVerticalVelocity)
                * retainedMagnitude;
        }
        _wallRunStartSpeed = _wallRunIsVertical
            ? verticalWallRunStartSpeed
            : Mathf.Max(alongWallEntrySpeed, wallRunMinEntrySpeed)
              * horizontalWallRunEntrySpeedMultiplier;
        _wallRunCurrentSpeed = _wallRunStartSpeed;

        Debug.Log($"[WallRun] Enter — pitchAngle:{pitchAngle:F1} " +
                  $"vertical:{_wallRunIsVertical} dir:{_wallRunDirection} " +
                  $"entrySpeed:{_wallRunStartSpeed:F1} " +
                  $"retainedVertical:{_horizontalWallRunVerticalVelocity:F1} normal:{wallNormal}");
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
        _horizontalWallRunVerticalVelocity = 0f;
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
            float retainedVerticalOffset = _wallRunIsVertical
                ? 0f
                : _horizontalWallRunVerticalVelocity * elapsed;
            Vector3 point = _wallRunStartPosition + _wallRunDirection * distance
                + _wallRunArcUp * (arcOffset + retainedVerticalOffset);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawRay(_wallRunStartPosition, _wallNormal);
    }

    private bool TryGetWallSurface(Vector3 origin, Vector3 direction, float distance,
        out RaycastHit nearestHit)
    {
        nearestHit = default;
        if (direction.sqrMagnitude <= Mathf.Epsilon) return false;

        int hitCount = Physics.RaycastNonAlloc(origin, direction.normalized, _wallProbeHits,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = _wallProbeHits[i];
            if (IsOwnCollider(candidate.collider) || candidate.distance >= nearestDistance) continue;
            nearestDistance = candidate.distance;
            nearestHit = candidate;
            found = true;
        }
        return found;
    }

    private bool IsOwnCollider(Collider coll)
    {
        if (coll == null) return true;
        if (motor != null && (coll == motor.Capsule
            || coll.GetComponentInParent<KinematicCharacterMotor>() == motor)) return true;
        if (coll.GetComponentInParent<PlayerCharacter>() == this) return true;
        Transform candidate = coll.transform;
        return candidate == transform || candidate.IsChildOf(transform)
            || (root != null && (candidate == root || candidate.IsChildOf(root)));
    }

    public bool IsColliderValidForCollisions(Collider coll) => !IsOwnCollider(coll);
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }
    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport r)
    { }

    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        ref HitStabilityReport r)
    {
        if (IsOwnCollider(hitCollider)) return;
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
