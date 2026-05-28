// AugmentDb.cs
// Definition library. One ScriptableObject asset in the project.
// Contains the canonical blueprint of every body part and its sub-parts.
// HealthManager reads from this on startup to build the player's living state.
//
// Create via: Assets > Create > Augment > AugmentDb

using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// Enums
// ─────────────────────────────────────────────────────────────────────────────

public enum BodyPart
{
    Head,
    Chest,
    Abdomen,
    LeftUpperArm,
    RightUpperArm,
    LeftForearm,
    RightForearm,
    LeftThigh,
    RightThigh,
    LeftShin,
    RightShin
}

public enum DamageType
{
    Slash,
    Pierce,
    Blunt,
    Burn,
    Shock
}

public enum AugmentCategory
{
    Natural,
    Endoskeletal,
    Exoskeletal
}

public enum SubPartCategory
{
    // Structural — participate in the damage cascade (ordered outer → inner)
    Skin,
    Muscle,
    Bone,

    // Internal organs — roll-based hit chance, not in cascade order
    Heart,
    Lung,
    Stomach,
    Intestines,
    Eye,
    Brain
}

// ─────────────────────────────────────────────────────────────────────────────
// Serializable resistance entry (replaces Dictionary for Inspector support)
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ResistanceEntry
{
    public DamageType damageType;

    [Tooltip("Straight damage multiplier applied to incoming damage of this type.\n" +
             "1.0 = neutral  |  1.2 = 20% more damage  |  0.8 = 20% less damage")]
    public float multiplier = 1f;
}

// ─────────────────────────────────────────────────────────────────────────────
// SubPartDefinition
// Blueprint for a single layer or organ within a body part.
// Structural sub-parts participate in the cascade; organs are roll-based.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class SubPartDefinition
{
    [Header("Identity")]
    public string subPartID;
    public string displayName;
    public SubPartCategory category;

    [Header("Is this an internal organ? (roll-based, not in cascade)")]
    public bool isOrgan = false;

    [Tooltip("Only used when isOrgan = true.\n" +
             "Probability (0–1) this organ is struck when the layer above it is breached.\n" +
             "Does not need to add up to 100% across organs in the same body part.")]
    [Range(0f, 1f)]
    public float organHitChance = 0f;

    [Header("Health")]
    [Tooltip("Maximum hit points for this sub-part")]
    public float maxHealth = 100f;

    [Header("Breach Threshold")]
    [Tooltip("The total calculated damage (incoming × multiplier) must exceed this value " +
             "for damage to pierce through to the next layer. " +
             "If not exceeded, cascade stops here.")]
    public float breachThreshold = 20f;

    [Header("Damage Resistances")]
    [Tooltip("One entry per damage type you want to override from 1.0 neutral. " +
             "Any damage type not listed defaults to 1.0.")]
    public List<ResistanceEntry> resistances = new List<ResistanceEntry>();

    // ── Runtime helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the resistance multiplier for a given damage type.
    /// Unlisted types return 1.0 (neutral).
    /// </summary>
    public float GetMultiplier(DamageType type)
    {
        foreach (var entry in resistances)
            if (entry.damageType == type) return entry.multiplier;
        return 1f;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BodyPartDefinition
// Blueprint for one of the 11 body parts.
// Structural sub-parts must be ordered outer → inner (index 0 = outermost).
// Organs sit in the same list with isOrgan = true; order doesn't matter for them.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class BodyPartDefinition
{
    [Header("Identity")]
    public string displayName;
    public BodyPart bodyPart;
    public AugmentCategory category = AugmentCategory.Natural;

    [TextArea]
    public string description;

    [Header("Sub-Parts (structural: outer → inner | organs: any order, isOrgan = true)")]
    public List<SubPartDefinition> subParts = new List<SubPartDefinition>();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns only the structural sub-parts in cascade order.</summary>
    public List<SubPartDefinition> GetStructuralLayers()
    {
        var layers = new List<SubPartDefinition>();
        foreach (var sp in subParts)
            if (!sp.isOrgan) layers.Add(sp);
        return layers;
    }

    /// <summary>Returns only the organ sub-parts.</summary>
    public List<SubPartDefinition> GetOrgans()
    {
        var organs = new List<SubPartDefinition>();
        foreach (var sp in subParts)
            if (sp.isOrgan) organs.Add(sp);
        return organs;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AugmentDb  — ScriptableObject asset
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "AugmentDb", menuName = "Augment/AugmentDb")]
public class AugmentDb : ScriptableObject
{
    [Tooltip("Full blueprint library of every body part and its sub-parts. " +
             "One entry per BodyPart enum value recommended.")]
    public List<BodyPartDefinition> bodyParts = new List<BodyPartDefinition>();

    /// <summary>
    /// Finds a body part definition by enum. Returns null if not found.
    /// </summary>
    public BodyPartDefinition GetDefinition(BodyPart part)
    {
        foreach (var def in bodyParts)
            if (def.bodyPart == part) return def;
        return null;
    }
}
