// AugmentCatalogue.cs
// The full list of every augment available in the game.
// Each entry is a BodyPartDefinition that replaces or modifies a body part.
// HealthManager calls GetAugment() when swapping parts or sub-parts at runtime.
//
// Create via: Assets > Create > Augment > AugmentCatalogue

using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// AugmentEntry
// A single augment as it appears in the catalogue.
// Wraps a BodyPartDefinition with catalogue-specific metadata.
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class AugmentEntry
{
    [Header("Catalogue Identity")]
    [Tooltip("Unique string ID used to look up this augment at runtime, e.g. 'TITANIUM_FOREARM_L'")]
    public string augmentID;

    [Tooltip("Human-readable name shown in UI, e.g. 'Titanium Left Forearm'")]
    public string displayName;

    [TextArea]
    public string description;

    [Header("Classification")]
    public AugmentCategory category;

    [Tooltip("Which body part slot this augment occupies when equipped")]
    public BodyPart targetBodyPart;

    [Header("Definition")]
    [Tooltip("The full body part blueprint this augment provides. " +
             "Sub-parts here override those from AugmentDb when this augment is installed.")]
    public BodyPartDefinition definition;

    // ── Optional: sub-part-only augments ─────────────────────────────────────
    // If this augment only replaces a single sub-part rather than a whole body part,
    // set isSubPartAugment = true and fill subPartAugment instead of definition.

    [Header("Sub-Part Augment (optional)")]
    [Tooltip("Set true if this augment replaces a single sub-part rather than a whole body part.")]
    public bool isSubPartAugment = false;

    [Tooltip("Which sub-part category this replaces, e.g. Bone replaces the Bone layer " +
             "in the target body part.")]
    public SubPartCategory targetSubPartCategory;

    [Tooltip("The sub-part definition this augment provides when isSubPartAugment is true.")]
    public SubPartDefinition subPartDefinition;
}

// ─────────────────────────────────────────────────────────────────────────────
// AugmentCatalogue — ScriptableObject asset
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "AugmentCatalogue", menuName = "Augment/AugmentCatalogue")]
public class AugmentCatalogue : ScriptableObject
{
    [Tooltip("Every augment available in the game. " +
             "Add entries here as new augments are designed.")]
    public List<AugmentEntry> augments = new List<AugmentEntry>();

    // ── Lookup helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Find an augment by its unique ID string. Returns null if not found.
    /// </summary>
    public AugmentEntry GetAugment(string augmentID)
    {
        foreach (var entry in augments)
            if (entry.augmentID == augmentID) return entry;

        Debug.LogWarning($"[AugmentCatalogue] No augment found with ID '{augmentID}'");
        return null;
    }

    /// <summary>
    /// Returns all augments that target a given body part.
    /// </summary>
    public List<AugmentEntry> GetAugmentsForBodyPart(BodyPart part)
    {
        var results = new List<AugmentEntry>();
        foreach (var entry in augments)
            if (entry.targetBodyPart == part) results.Add(entry);
        return results;
    }

    /// <summary>
    /// Returns all whole-body-part augments (not sub-part augments).
    /// </summary>
    public List<AugmentEntry> GetFullBodyPartAugments()
    {
        var results = new List<AugmentEntry>();
        foreach (var entry in augments)
            if (!entry.isSubPartAugment) results.Add(entry);
        return results;
    }

    /// <summary>
    /// Returns all sub-part augments targeting a specific category.
    /// </summary>
    public List<AugmentEntry> GetSubPartAugments(SubPartCategory category)
    {
        var results = new List<AugmentEntry>();
        foreach (var entry in augments)
            if (entry.isSubPartAugment && entry.targetSubPartCategory == category)
                results.Add(entry);
        return results;
    }
}
