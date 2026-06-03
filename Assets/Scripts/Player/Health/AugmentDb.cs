// AugmentDb.cs
// Body Part Library — canonical blueprints for every body part and sub-part.
// HealthManager reads this on startup to build the player's living body state.
// Renamed menu path for clarity; class name unchanged so existing Inspector
// references don't break.
//
// Create via: Assets > Create > Body > BodyPartLibrary

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
    // ── Equipped armour (outermost, index 0 when present) ─────────────────────
    Armour,

    // ── Structural layers — damage cascade order (outer → inner) ─────────────
    Skin,
    Muscle,
    Bone,

    // ── Internal organs — roll-based, not in cascade order ───────────────────
    Heart,
    Lung,
    Stomach,
    Intestines,
    Eye,
    Brain
}

// ─────────────────────────────────────────────────────────────────────────────
// ResistanceEntry
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ResistanceEntry
{
    public DamageType damageType;

    [Tooltip("Damage multiplier for this type.\n" +
             "1.0 = neutral | 1.2 = 20% more damage | 0.8 = 20% less damage")]
    public float multiplier = 1f;
}

// ─────────────────────────────────────────────────────────────────────────────
// SubPartDefinition
// Blueprint for a single structural layer or organ.
// Custom editor (SubPartDefinitionDrawer) hides irrelevant fields per type.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class SubPartDefinition
{
    public string subPartID;
    public string displayName;
    public SubPartCategory category;
    public bool isOrgan = false;

    // Organ only
    [Range(0f, 1f)]
    public float organHitChance = 0f;

    // Shared
    public float maxHealth = 100f;

    // Structural layers only (not organs, not armour — armour uses this too)
    public float breachThreshold = 20f;

    public List<ResistanceEntry> resistances = new List<ResistanceEntry>();

    public float GetMultiplier(DamageType type)
    {
        foreach (var e in resistances)
            if (e.damageType == type) return e.multiplier;
        return 1f;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BodyPartDefinition
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class BodyPartDefinition
{
    public string displayName;
    public BodyPart bodyPart;
    public AugmentCategory category = AugmentCategory.Natural;

    [TextArea]
    public string description;

    [Tooltip("Sub-parts: structural layers ordered outer → inner, " +
             "then organs (isOrgan = true) in any order.")]
    public List<SubPartDefinition> subParts = new List<SubPartDefinition>();

    public List<SubPartDefinition> GetStructuralLayers()
    {
        var list = new List<SubPartDefinition>();
        foreach (var sp in subParts) if (!sp.isOrgan) list.Add(sp);
        return list;
    }

    public List<SubPartDefinition> GetOrgans()
    {
        var list = new List<SubPartDefinition>();
        foreach (var sp in subParts) if (sp.isOrgan) list.Add(sp);
        return list;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AugmentDb — Body Part Library ScriptableObject
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "BodyPartLibrary", menuName = "Body/BodyPartLibrary")]
public class AugmentDb : ScriptableObject
{
    [Tooltip("Canonical blueprint for every body part. " +
             "One entry per BodyPart enum value. " +
             "HealthManager deep-copies these into runtime instances on startup.")]
    public List<BodyPartDefinition> bodyParts = new List<BodyPartDefinition>();

    public BodyPartDefinition GetDefinition(BodyPart part)
    {
        foreach (var def in bodyParts)
            if (def.bodyPart == part) return def;
        return null;
    }
}
