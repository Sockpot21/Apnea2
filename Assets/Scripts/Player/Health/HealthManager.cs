// HealthManager.cs
// Owns the player's living body state at runtime.
// On startup: deep-copies blueprints from AugmentDb into mutable runtime instances.
// On damage: runs the full cascade — structural layers → organ rolls → PlayerHealth notify.
// Exposes install/swap methods for augments at runtime.
//
// Attach to the player root GameObject alongside PlayerHealth.

using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// Runtime sub-part state (mutable copy of SubPartDefinition)
// ─────────────────────────────────────────────────────────────────────────────

public class RuntimeSubPart
{
    public string subPartID;
    public string displayName;
    public SubPartCategory category;
    public bool isOrgan;
    public float organHitChance;
    public float maxHealth;
    public float currentHealth;
    public float breachThreshold;

    // Keyed by DamageType for O(1) runtime lookup
    public Dictionary<DamageType, float> resistanceMap = new Dictionary<DamageType, float>();

    public bool IsDestroyed => currentHealth <= 0f;

    public float GetMultiplier(DamageType type)
    {
        return resistanceMap.TryGetValue(type, out float m) ? m : 1f;
    }

    /// <summary>
    /// Deep-copies a SubPartDefinition into a new RuntimeSubPart.
    /// </summary>
    public static RuntimeSubPart FromDefinition(SubPartDefinition def)
    {
        var rt = new RuntimeSubPart
        {
            subPartID = def.subPartID,
            displayName = def.displayName,
            category = def.category,
            isOrgan = def.isOrgan,
            organHitChance = def.organHitChance,
            maxHealth = def.maxHealth,
            currentHealth = def.maxHealth,
            breachThreshold = def.breachThreshold
        };

        foreach (var entry in def.resistances)
            rt.resistanceMap[entry.damageType] = entry.multiplier;

        return rt;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Runtime body part state (mutable copy of BodyPartDefinition)
// ─────────────────────────────────────────────────────────────────────────────

public class RuntimeBodyPart
{
    public BodyPart bodyPart;
    public string displayName;

    // Structural layers in cascade order (outer → inner), organs excluded
    public List<RuntimeSubPart> layers = new List<RuntimeSubPart>();

    // Organs — hit by chance when a layer above them is breached
    public List<RuntimeSubPart> organs = new List<RuntimeSubPart>();

    // Overall condition as a 0–1 ratio (product of all sub-part HP ratios)
    public float ConditionRatio
    {
        get
        {
            var all = new List<RuntimeSubPart>(layers);
            all.AddRange(organs);

            if (all.Count == 0) return 0f;

            float product = 1f;
            foreach (var sp in all)
                product *= (sp.maxHealth > 0f ? sp.currentHealth / sp.maxHealth : 0f);

            return product;
        }
    }

    public static RuntimeBodyPart FromDefinition(BodyPartDefinition def)
    {
        var rt = new RuntimeBodyPart
        {
            bodyPart = def.bodyPart,
            displayName = def.displayName
        };

        foreach (var sp in def.GetStructuralLayers())
            rt.layers.Add(RuntimeSubPart.FromDefinition(sp));

        foreach (var sp in def.GetOrgans())
            rt.organs.Add(RuntimeSubPart.FromDefinition(sp));

        return rt;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Damage result — returned from ProcessDamage, forwarded to PlayerHealth
// ─────────────────────────────────────────────────────────────────────────────

public class DamageResult
{
    public BodyPart bodyPart;
    public List<LayerHitRecord> layerHits = new List<LayerHitRecord>();
    public List<OrganHitRecord> organHits = new List<OrganHitRecord>();
    public float conditionAfter; // ConditionRatio post-damage
}

public class LayerHitRecord
{
    public string displayName;
    public float incomingDamage;    // Raw damage that arrived at this layer
    public float totalDamage;       // After multiplier (incoming × multiplier)
    public float hpDamageDealt;     // Actual HP subtracted from this layer
    public float healthAfter;
    public bool breached;
    public bool destroyed;
}

public class OrganHitRecord
{
    public string displayName;
    public float incomingDamage;
    public float totalDamage;
    public float hpDamageDealt;
    public float healthAfter;
    public bool destroyed;
}

// ─────────────────────────────────────────────────────────────────────────────
// Collider → BodyPart mapping entry (set up in Inspector or via code)
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class ColliderBodyPartMapping
{
    [Tooltip("The Collider component on the hitbox child GameObject")]
    public Collider hitCollider;

    [Tooltip("Which body part this collider represents")]
    public BodyPart bodyPart;
}

// ─────────────────────────────────────────────────────────────────────────────
// HealthManager
// ─────────────────────────────────────────────────────────────────────────────

public class HealthManager : MonoBehaviour
{
    // ── Inspector references ──────────────────────────────────────────────────

    [Header("Data Sources")]
    [Tooltip("The definition library ScriptableObject")]
    public AugmentDb augmentDb;

    [Tooltip("The full augment catalogue ScriptableObject")]
    public AugmentCatalogue augmentCatalogue;

    [Header("Collider → Body Part Mappings")]
    [Tooltip("Assign each of the 11 hitbox colliders and their associated body part here. " +
             "Alternatively call RegisterCollider() at runtime.")]
    public List<ColliderBodyPartMapping> colliderMappings = new List<ColliderBodyPartMapping>();

    [Header("References")]
    public PlayerHealth playerHealth;

    // ── Runtime state ─────────────────────────────────────────────────────────

    // The live, mutable body. Keyed by BodyPart enum.
    private Dictionary<BodyPart, RuntimeBodyPart> _body
        = new Dictionary<BodyPart, RuntimeBodyPart>();

    // Fast collider lookup built from colliderMappings
    private Dictionary<Collider, BodyPart> _colliderMap
        = new Dictionary<Collider, BodyPart>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        InitialiseBody();
        BuildColliderMap();
    }

    private void Start()
    {
        // Push initial state to PlayerHealth for UI
        playerHealth?.OnBodyInitialised(_body);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Initialisation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deep-copies every body part definition from AugmentDb into runtime instances.
    /// </summary>
    private void InitialiseBody()
    {
        if (augmentDb == null)
        {
            Debug.LogError("[HealthManager] AugmentDb is not assigned.");
            return;
        }

        _body.Clear();
        foreach (var def in augmentDb.bodyParts)
        {
            var runtime = RuntimeBodyPart.FromDefinition(def);
            _body[def.bodyPart] = runtime;
        }

        Debug.Log($"[HealthManager] Body initialised with {_body.Count} body parts.");
    }

    private void BuildColliderMap()
    {
        _colliderMap.Clear();
        foreach (var mapping in colliderMappings)
        {
            if (mapping.hitCollider == null)
            {
                Debug.LogWarning("[HealthManager] A collider mapping has a null Collider — skipped.");
                continue;
            }
            _colliderMap[mapping.hitCollider] = mapping.bodyPart;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public damage entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this when the player is hit.
    /// hitCollider  — the collider that was struck (used to identify body part)
    /// damageEntries — one or more (DamageType, amount) pairs (supports multi-type hits)
    /// </summary>
    public void ReceiveDamage(Collider hitCollider,
                              List<(DamageType type, float amount)> damageEntries)
    {
        if (!_colliderMap.TryGetValue(hitCollider, out BodyPart part))
        {
            Debug.LogWarning($"[HealthManager] Hit collider '{hitCollider.name}' " +
                             $"has no body part mapping — damage ignored.");
            return;
        }

        ReceiveDamageOnPart(part, damageEntries);
    }

    /// <summary>
    /// Overload: accepts a BodyPart directly (useful for testing or scripted damage).
    /// </summary>
    public void ReceiveDamageOnPart(BodyPart part,
                                    List<(DamageType type, float amount)> damageEntries)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart bodyPart))
        {
            Debug.LogWarning($"[HealthManager] Body part {part} not found in runtime body.");
            return;
        }

        var result = new DamageResult { bodyPart = part };

        // Process each damage type independently through the same cascade
        foreach (var (type, amount) in damageEntries)
            ProcessDamageType(bodyPart, type, amount, result);

        result.conditionAfter = bodyPart.ConditionRatio;

        // Notify PlayerHealth
        playerHealth?.OnDamageReceived(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core damage cascade
    // ─────────────────────────────────────────────────────────────────────────

    private void ProcessDamageType(RuntimeBodyPart bodyPart, DamageType type,
                                   float incoming, DamageResult result)
    {
        // ── Structural cascade ────────────────────────────────────────────────

        bool anyLayerBreached = false;
        float spillover = incoming;

        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            var layer = bodyPart.layers[i];

            // Destroyed layers pass damage through without modification
            if (layer.IsDestroyed)
            {
                result.layerHits.Add(new LayerHitRecord
                {
                    displayName = layer.displayName,
                    incomingDamage = spillover,
                    totalDamage = spillover,
                    hpDamageDealt = 0f,
                    healthAfter = 0f,
                    breached = true,
                    destroyed = true
                });
                continue;
            }

            float multiplier = layer.GetMultiplier(type);
            float totalDamage = spillover * multiplier;

            bool breached = totalDamage > layer.breachThreshold;
            bool isLastLayer = (i == bodyPart.layers.Count - 1);

            float hpDamage;

            if (breached && !isLastLayer)
            {
                // Layer is breached but not the last:
                // This layer absorbs only the raw incoming, total spills to next layer
                hpDamage = spillover;
                spillover = totalDamage;
            }
            else
            {
                // No breach, or this is the last layer:
                // Layer absorbs the full calculated total, cascade ends
                hpDamage = totalDamage;
                spillover = 0f;
            }

            layer.currentHealth = Mathf.Max(0f, layer.currentHealth - hpDamage);

            result.layerHits.Add(new LayerHitRecord
            {
                displayName = layer.displayName,
                incomingDamage = incoming,
                totalDamage = totalDamage,
                hpDamageDealt = hpDamage,
                healthAfter = layer.currentHealth,
                breached = breached,
                destroyed = layer.IsDestroyed
            });

            if (breached) anyLayerBreached = true;

            // Stop cascade if not breached
            if (!breached) break;
        }

        // ── Organ rolls (only if at least one structural layer was breached) ──

        if (anyLayerBreached && bodyPart.organs.Count > 0)
            ProcessOrganRolls(bodyPart, type, spillover > 0f ? spillover : incoming, result);
    }

    private void ProcessOrganRolls(RuntimeBodyPart bodyPart, DamageType type,
                                   float incomingToOrgan, DamageResult result)
    {
        foreach (var organ in bodyPart.organs)
        {
            if (organ.IsDestroyed) continue;

            // Independent hit roll per organ
            if (Random.value > organ.organHitChance) continue;

            float multiplier = organ.GetMultiplier(type);
            float totalDamage = incomingToOrgan * multiplier;

            bool breached = totalDamage > organ.breachThreshold;

            // Organs are a single layer — they always absorb the full total
            float hpDamage = totalDamage;
            organ.currentHealth = Mathf.Max(0f, organ.currentHealth - hpDamage);

            result.organHits.Add(new OrganHitRecord
            {
                displayName = organ.displayName,
                incomingDamage = incomingToOrgan,
                totalDamage = totalDamage,
                hpDamageDealt = hpDamage,
                healthAfter = organ.currentHealth,
                destroyed = organ.IsDestroyed
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Augment install / swap API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces an entire body part with an augment's definition.
    /// </summary>
    public void InstallFullAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        if (entry == null || entry.isSubPartAugment) return;

        var runtime = RuntimeBodyPart.FromDefinition(entry.definition);
        _body[entry.targetBodyPart] = runtime;

        Debug.Log($"[HealthManager] Full augment installed: {entry.displayName} → {entry.targetBodyPart}");
        playerHealth?.OnBodyInitialised(_body);
    }

    /// <summary>
    /// Replaces a single sub-part layer within a body part.
    /// Matches by SubPartCategory — replaces the first matching layer found.
    /// </summary>
    public void InstallSubPartAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        if (entry == null || !entry.isSubPartAugment) return;

        if (!_body.TryGetValue(entry.targetBodyPart, out RuntimeBodyPart bodyPart))
        {
            Debug.LogWarning($"[HealthManager] Cannot install sub-part augment — " +
                             $"body part {entry.targetBodyPart} not found.");
            return;
        }

        var newRuntime = RuntimeSubPart.FromDefinition(entry.subPartDefinition);

        // Try structural layers first
        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            if (bodyPart.layers[i].category == entry.targetSubPartCategory)
            {
                bodyPart.layers[i] = newRuntime;
                Debug.Log($"[HealthManager] Sub-part augment installed: " +
                          $"{entry.displayName} → {entry.targetBodyPart} layer {i}");
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }

        // Then organs
        for (int i = 0; i < bodyPart.organs.Count; i++)
        {
            if (bodyPart.organs[i].category == entry.targetSubPartCategory)
            {
                bodyPart.organs[i] = newRuntime;
                Debug.Log($"[HealthManager] Sub-part augment installed: " +
                          $"{entry.displayName} → {entry.targetBodyPart} organ slot {i}");
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }

        Debug.LogWarning($"[HealthManager] No matching sub-part category " +
                         $"'{entry.targetSubPartCategory}' found in {entry.targetBodyPart}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Runtime collider registration (for procedural / non-Inspector setup)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a collider → body part mapping at runtime.
    /// Useful if hitbox GameObjects are spawned procedurally.
    /// </summary>
    public void RegisterCollider(Collider col, BodyPart part)
    {
        _colliderMap[col] = part;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Read-only state access (for PlayerHealth, UI, etc.)
    // ─────────────────────────────────────────────────────────────────────────

    public RuntimeBodyPart GetBodyPart(BodyPart part)
    {
        _body.TryGetValue(part, out var bp);
        return bp;
    }

    public IReadOnlyDictionary<BodyPart, RuntimeBodyPart> GetFullBody() => _body;
}
