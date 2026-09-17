// PlayerHealth.cs
// Receives body state and damage results from HealthManager.
// Owns all UI-facing concerns: current condition per body part, overall health,
// event broadcasting for HUD / VFX / audio systems.
//
// Attach to the player root GameObject alongside HealthManager.

using UnityEngine;
using System.Collections.Generic;
using System;

// ─────────────────────────────────────────────────────────────────────────────
// Body part condition snapshot — sent to UI each time state changes
// ─────────────────────────────────────────────────────────────────────────────

public class BodyPartCondition
{
    public BodyPart bodyPart;
    public string displayName;

    /// <summary>
    /// 0–1. Product of all sub-part HP ratios.
    /// 1.0 = fully intact, 0.0 = completely destroyed.
    /// </summary>
    public float conditionRatio;
    public float currentHealth;
    public float maxHealth;
    public float baseMaxHealth;
    public bool isDisabled;
    public bool isDestroyed;

    /// <summary>Convenience: condition as a 0–100 percentage.</summary>
    public float ConditionPercent => conditionRatio * 100f;

    /// <summary>Per-sub-part breakdown for detailed UI panels.</summary>
    public List<SubPartCondition> subPartConditions = new List<SubPartCondition>();
}

public class SubPartCondition
{
    public string displayName;
    public SubPartCategory category;
    public bool isOrgan;
    public float baseMaxHealth;
    public float currentHealth;
    public float maxHealth;
    public float HealthRatio => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public bool IsDepleted => currentHealth <= 0f;
    public bool IsDestroyed => maxHealth <= 0f;
    public List<HealthConditionSnapshot> activeConditions = new List<HealthConditionSnapshot>();
}

public class HealthConditionSnapshot
{
    public HealthConditionType type;
    public DamageType sourceDamageType;
    public float remainingSeconds;
    public bool requiresTreatment;

    public bool IsPermanent => remainingSeconds < 0f || requiresTreatment;
}

// ─────────────────────────────────────────────────────────────────────────────
// PlayerHealth
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerHealth : MonoBehaviour
{
    // ── Events — subscribe from HUD, VFX, audio, game-over systems ───────────

    /// <summary>Fired when the full body state is (re)initialised or an augment is swapped.</summary>
    public event Action<Dictionary<BodyPart, BodyPartCondition>> OnBodyStateUpdated;

    /// <summary>Fired after every hit with a full breakdown of what happened.</summary>
    public event Action<DamageResult> OnDamageProcessed;

    /// <summary>Fired when a specific body part's condition changes.</summary>
    public event Action<BodyPartCondition> OnBodyPartConditionChanged;

    /// <summary>Fired when any sub-part is destroyed.</summary>
    public event Action<BodyPart, string> OnSubPartDestroyed;  // (bodyPart, subPartDisplayName)

    /// <summary>Fired when an organ is hit.</summary>
    public event Action<OrganHitRecord> OnOrganHit;

    /// <summary>Fired when overall body condition drops to 0 across all parts.</summary>
    public event Action OnPlayerDeath;

    // ── Runtime condition state ───────────────────────────────────────────────

    private Dictionary<BodyPart, BodyPartCondition> _conditions
        = new Dictionary<BodyPart, BodyPartCondition>();

    private bool _isDead = false;

    // ── Called by HealthManager ───────────────────────────────────────────────

    /// <summary>
    /// Called on startup and whenever augments are swapped.
    /// Rebuilds the full condition snapshot from the current runtime body.
    /// </summary>
    public void OnBodyInitialised(Dictionary<BodyPart, RuntimeBodyPart> body)
    {
        _conditions.Clear();

        foreach (var kvp in body)
        {
            var condition = BuildCondition(kvp.Value);
            _conditions[kvp.Key] = condition;
        }

        OnBodyStateUpdated?.Invoke(_conditions);
    }

    /// <summary>
    /// Called by HealthManager after every damage event.
    /// Updates condition state and fires relevant events.
    /// </summary>
    public void OnDamageReceived(DamageResult result)
    {
        if (_isDead) return;

        // Update the condition snapshot for the affected body part
        var healthManager = GetComponent<HealthManager>();
        if (healthManager != null)
        {
            var runtimePart = healthManager.GetBodyPart(result.bodyPart);
            if (runtimePart != null)
            {
                var condition = BuildCondition(runtimePart);
                _conditions[result.bodyPart] = condition;
                OnBodyPartConditionChanged?.Invoke(condition);
            }
        }

        // Fire per-layer destroyed events
        foreach (var hit in result.layerHits)
        {
            if (hit.destroyed)
                OnSubPartDestroyed?.Invoke(result.bodyPart, hit.displayName);
        }

        // Fire organ hit events
        foreach (var organHit in result.organHits)
        {
            OnOrganHit?.Invoke(organHit);
            if (organHit.destroyed)
                OnSubPartDestroyed?.Invoke(result.bodyPart, organHit.displayName);
        }

        // Forward full result for HUD / combat log
        OnDamageProcessed?.Invoke(result);

    }

    /// <summary>Updates only the body-part rows changed by passive regeneration.</summary>
    public void OnBodyRegenerated(Dictionary<BodyPart, RuntimeBodyPart> body,
        IEnumerable<BodyPart> regeneratedParts)
    {
        foreach (BodyPart part in regeneratedParts)
        {
            if (!body.TryGetValue(part, out RuntimeBodyPart runtimePart)) continue;
            var condition = BuildCondition(runtimePart);
            _conditions[part] = condition;
            OnBodyPartConditionChanged?.Invoke(condition);
        }
    }

    // ── Public read access ────────────────────────────────────────────────────

    /// <summary>Returns the condition snapshot for a given body part.</summary>
    public BodyPartCondition GetCondition(BodyPart part)
    {
        _conditions.TryGetValue(part, out var c);
        return c;
    }

    /// <summary>Returns all current body part conditions.</summary>
    public IReadOnlyDictionary<BodyPart, BodyPartCondition> GetAllConditions() => _conditions;

    /// <summary>Creates a fresh UI-safe snapshot from live runtime health data.</summary>
    public BodyPartCondition BuildLiveSnapshot(RuntimeBodyPart runtimePart) =>
        BuildCondition(runtimePart);

    public void NotifyDeath()
    {
        if (_isDead) return;
        _isDead = true;
        Debug.Log("[PlayerHealth] Player has died.");
        OnPlayerDeath?.Invoke();
    }

    /// <summary>
    /// Overall body condition: average of all body part condition ratios.
    /// 1.0 = fully healthy, 0.0 = dead.
    /// </summary>
    public float OverallCondition
    {
        get
        {
            if (_conditions.Count == 0) return 0f;
            float sum = 0f;
            foreach (var c in _conditions.Values)
                sum += c.conditionRatio;
            return sum / _conditions.Count;
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private BodyPartCondition BuildCondition(RuntimeBodyPart runtimePart)
    {
        var condition = new BodyPartCondition
        {
            bodyPart = runtimePart.bodyPart,
            displayName = runtimePart.displayName,
            conditionRatio = runtimePart.ConditionRatio,
            currentHealth = runtimePart.CurrentStructuralHealth,
            maxHealth = runtimePart.maxHealth,
            baseMaxHealth = runtimePart.baseMaxHealth,
            isDisabled = runtimePart.IsDisabled,
            isDestroyed = runtimePart.IsDestroyed
        };

        // Structural layers
        foreach (var layer in runtimePart.layers)
        {
            var snapshot = new SubPartCondition
            {
                displayName = layer.displayName,
                category = layer.category,
                isOrgan = false,
                baseMaxHealth = layer.baseMaxHealth,
                currentHealth = layer.currentHealth,
                maxHealth = layer.maxHealth
            };
            CopyConditions(layer, snapshot);
            condition.subPartConditions.Add(snapshot);
        }

        // Organs
        foreach (var organ in runtimePart.organs)
        {
            var snapshot = new SubPartCondition
            {
                displayName = organ.displayName,
                category = organ.category,
                isOrgan = true,
                baseMaxHealth = organ.baseMaxHealth,
                currentHealth = organ.currentHealth,
                maxHealth = organ.maxHealth
            };
            CopyConditions(organ, snapshot);
            condition.subPartConditions.Add(snapshot);
        }

        return condition;
    }

    private static void CopyConditions(RuntimeSubPart source, SubPartCondition target)
    {
        foreach (var active in source.conditions)
        {
            target.activeConditions.Add(new HealthConditionSnapshot
            {
                type = active.type,
                sourceDamageType = active.sourceDamageType,
                remainingSeconds = active.remainingSeconds,
                requiresTreatment = active.requiresTreatment
            });
        }
    }

}
