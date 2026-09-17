// AugmentCatalogue.cs
// Augment Catalogue — all swappable augments available in the game.
// Each entry replaces either a whole body part or a single sub-part layer.
// HealthManager calls GetAugment() at runtime to look up and install augments.
// Renamed menu path for clarity; class name unchanged.
//
// Create via: Assets > Create > Body > AugmentCatalogue

using UnityEngine;
using System.Collections.Generic;

public enum PlayerAugmentStat
{
    WalkSpeed, CrouchSpeed, WalkResponse, CrouchResponse,
    SprintEnabled, SprintSpeed, SprintResponse,
    JumpSpeed, CoyoteTime, JumpSustainGravity, Gravity, DoubleJumpEnabled,
    SlideStartSpeed, SlideEndSpeed, SlideFriction, SlideSteerAcceleration, SlideGravity,
    AirSpeed, AirAcceleration,
    WallRunEnabled, WallRunMinEntrySpeed, HorizontalWallRunEntrySpeedMultiplier,
    HorizontalWallRunDuration, HorizontalWallRunDecayRate, WallRunGravityFadeTime,
    WallRunCooldown, WallDetectRadius, WallNormalTolerance, WallRunPitchThreshold,
    VerticalWallRunStartSpeed, VerticalWallRunDuration, VerticalWallRunDecayRate,
    WallRunArcHeight,
    ProneHeight, ProneSpeed, ProneResponse,
    MaxSaturation, SaturationDepletionRate,
    MaxStamina, SprintStamina, JumpStamina, StaminaRegen, StaminaRegenDelay,
    VerticalWallRunMaxDownwardSpeed,
    HorizontalWallRunVerticalVelocityDeadZone,
    HorizontalWallRunVerticalVelocityRetention,
    MovingStaminaRegen
}

public enum PlayerStatModifierMode
{
    Override,
    PercentageIncrease,
    PercentageDecrease
}

[System.Serializable]
public class AugmentStatOverride
{
    [Tooltip("The player-controller stat this modifier changes while it is active.")]
    public PlayerAugmentStat stat;
    [Tooltip("Override replaces the stat. Percentage modes change the effective value by this percentage and compose in list order. Toggle stats always use Bool Value.")]
    public PlayerStatModifierMode mode = PlayerStatModifierMode.Override;
    [Tooltip("Absolute value for Override, or a positive percentage such as 15 for percentage modes.")]
    public float value;
    [Tooltip("Value used for toggle stats (Sprint Enabled, Double Jump Enabled, Wall Run Enabled).")]
    public bool boolValue;
    [Min(0f), Tooltip("Per-effect duration used only when this modifier belongs to a consumable with Share Timer disabled.")]
    public float duration = 1f;
}

// ─────────────────────────────────────────────────────────────────────────────
// AugmentEntry
// One installable augment. Custom editor (AugmentEntryDrawer) hides fields
// that are irrelevant based on isSubPartAugment.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class AugmentEntry
{
    // ── Always shown ──────────────────────────────────────────────────────────
    public string augmentID;
    public string displayName;
    [TextArea]
    public string description;
    public AugmentCategory category;
    public BodyPart targetBodyPart;

    // ── Type selector ─────────────────────────────────────────────────────────
    [Tooltip("True = replaces a single sub-part layer. " +
             "False = replaces the entire body part.")]
    public bool isSubPartAugment = false;

    // ── Full body part replacement (isSubPartAugment = false) ─────────────────
    [Tooltip("Full body part blueprint. Used when isSubPartAugment is false.")]
    public BodyPartDefinition definition;

    // ── Sub-part replacement (isSubPartAugment = true) ───────────────────────
    [Tooltip("Which sub-part category within the target body part to replace.")]
    public SubPartCategory targetSubPartCategory;

    [Tooltip("The replacement sub-part definition. Used when isSubPartAugment is true.")]
    public SubPartDefinition subPartDefinition;

    [Header("Player Stat Modifiers")]
    [Tooltip("Only add stats this augment changes. Numeric stats may be overridden or changed by a percentage.")]
    public List<AugmentStatOverride> statOverrides = new List<AugmentStatOverride>();
}

// ─────────────────────────────────────────────────────────────────────────────
// AugmentCatalogue — ScriptableObject
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "AugmentCatalogue", menuName = "Body/AugmentCatalogue")]
public class AugmentCatalogue : ScriptableObject
{
    [Tooltip("Every augment available in the game. " +
             "Add entries here as new augments are designed.")]
    public List<AugmentEntry> augments = new List<AugmentEntry>();

    public AugmentEntry GetAugment(string id)
    {
        if (TryGetAugment(id, out AugmentEntry augment)) return augment;
        Debug.LogWarning($"[AugmentCatalogue] No augment found with ID '{id}'");
        return null;
    }

    public bool TryGetAugment(string id, out AugmentEntry augment)
    {
        foreach (AugmentEntry entry in augments)
        {
            if (entry.augmentID == id)
            {
                augment = entry;
                return true;
            }
        }

        augment = null;
        return false;
    }

    public List<AugmentEntry> GetAugmentsForBodyPart(BodyPart part)
    {
        var list = new List<AugmentEntry>();
        foreach (var e in augments)
            if (e.targetBodyPart == part) list.Add(e);
        return list;
    }

    public List<AugmentEntry> GetFullBodyPartAugments()
    {
        var list = new List<AugmentEntry>();
        foreach (var e in augments)
            if (!e.isSubPartAugment) list.Add(e);
        return list;
    }

    public List<AugmentEntry> GetSubPartAugments(SubPartCategory category)
    {
        var list = new List<AugmentEntry>();
        foreach (var e in augments)
            if (e.isSubPartAugment && e.targetSubPartCategory == category) list.Add(e);
        return list;
    }
}
