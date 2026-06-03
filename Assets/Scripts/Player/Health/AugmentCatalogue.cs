// AugmentCatalogue.cs
// Augment Catalogue — all swappable augments available in the game.
// Each entry replaces either a whole body part or a single sub-part layer.
// HealthManager calls GetAugment() at runtime to look up and install augments.
// Renamed menu path for clarity; class name unchanged.
//
// Create via: Assets > Create > Body > AugmentCatalogue

using UnityEngine;
using System.Collections.Generic;

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
        foreach (var e in augments)
            if (e.augmentID == id) return e;
        Debug.LogWarning($"[AugmentCatalogue] No augment found with ID '{id}'");
        return null;
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
