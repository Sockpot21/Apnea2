// HealthManager.cs
// DEBUG VERSION: Consolidated combat logging into single report per hit.
// Supports armour layer insertion/removal via PlayerEquipment.
// Auto-notifies PlayerEquipment when an armour layer is destroyed.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using System.Collections.Generic;
using System.Text;

public class RuntimeSubPart
{
    public string subPartID;
    public string displayName;
    public SubPartCategory category;
    public bool isOrgan;
    public float organHitChance;
    public float baseMaxHealth;
    public float maxHealth;
    public float currentHealth;
    public float breachThreshold;
    public bool requiresBiofluid;
    public float biofluidRequirement;
    public bool producesBiofluid;
    public float biofluidProductionRate;
    public bool pumpsBiofluid;
    public float biofluidPumpEfficiency;
    public float healingRate;
    public bool flammable;
    public float flammabilityModifier;
    public BiofluidProfile biofluidProfile;
    public readonly List<RuntimeHealthCondition> conditions = new();

    public Dictionary<DamageType, float> resistanceMap = new Dictionary<DamageType, float>();

    /// <summary>Temporarily unable to function. Ordinary healing may recover it.</summary>
    public bool IsDepleted => currentHealth <= 0f;

    /// <summary>Permanently destroyed until maximum-health restoration is used.</summary>
    public bool IsDestroyed => maxHealth <= 0f;

    public bool IsFunctional => !IsDestroyed && !IsDepleted;

    public float GetMultiplier(DamageType type)
    {
        return resistanceMap.TryGetValue(type, out float m) ? m : 1f;
    }

    public float ApplyDamage(float amount)
    {
        float before = currentHealth;
        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, amount));
        return before - currentHealth;
    }

    public void ApplyMaxHealthDamage(float amount)
    {
        maxHealth = Mathf.Max(0f, maxHealth - Mathf.Max(0f, amount));
        currentHealth = Mathf.Min(currentHealth, maxHealth);
    }

    public void AddOrRefreshCondition(RuntimeHealthCondition condition)
    {
        var existing = conditions.Find(c => c.type == condition.type);
        if (existing == null) { conditions.Add(condition); return; }
        existing.severity = Mathf.Max(existing.severity, condition.severity);
        existing.remainingSeconds = Mathf.Max(existing.remainingSeconds, condition.remainingSeconds);
        existing.biofluidLossPerSecond = Mathf.Max(existing.biofluidLossPerSecond, condition.biofluidLossPerSecond);
        existing.tickDamage = Mathf.Max(existing.tickDamage, condition.tickDamage);
        existing.maxHealthPenalty = Mathf.Max(existing.maxHealthPenalty, condition.maxHealthPenalty);
    }

    public static RuntimeSubPart FromDefinition(SubPartDefinition def)
    {
        var rt = new RuntimeSubPart
        {
            subPartID = def.subPartID,
            displayName = def.displayName,
            category = def.category,
            isOrgan = def.isOrgan,
            organHitChance = def.organHitChance,
            baseMaxHealth = def.maxHealth,
            maxHealth = def.maxHealth,
            currentHealth = def.maxHealth,
            breachThreshold = def.breachThreshold,
            requiresBiofluid = def.requiresBiofluid,
            biofluidRequirement = def.biofluidRequirement,
            producesBiofluid = def.producesBiofluid,
            biofluidProductionRate = def.biofluidProductionRate,
            pumpsBiofluid = def.pumpsBiofluid,
            biofluidPumpEfficiency = def.biofluidPumpEfficiency,
            healingRate = def.healingRate,
            flammable = def.flammable,
            flammabilityModifier = def.flammabilityModifier,
            biofluidProfile = def.biofluidProfile ?? new BiofluidProfile()
        };

        foreach (var entry in def.resistances)
            rt.resistanceMap[entry.damageType] = entry.multiplier;

        return rt;
    }
}

public class RuntimeBodyPart
{
    public BodyPart bodyPart;
    public string displayName;

    public List<RuntimeSubPart> layers = new List<RuntimeSubPart>();
    public List<RuntimeSubPart> organs = new List<RuntimeSubPart>();

    // Limb maximum HP is the sum of the original maxima of its structural
    // layers. Equipped armour is protection, not part of the limb itself.
    public float baseMaxHealth;
    public float maxHealth;

    public float CurrentStructuralHealth
    {
        get
        {
            float total = 0f;
            foreach (var layer in layers)
                if (layer.category != SubPartCategory.Armour)
                    total += Mathf.Max(0f, layer.currentHealth);
            return Mathf.Min(total, maxHealth);
        }
    }

    public bool IsDisabled
    {
        get
        {
            bool hasStructure = false;
            foreach (var layer in layers)
            {
                if (layer.category == SubPartCategory.Armour) continue;
                hasStructure = true;
                if (layer.IsFunctional) return false;
            }
            return hasStructure;
        }
    }

    public bool IsDestroyed => maxHealth <= 0f;

    public float ConditionRatio
    {
        get
        {
            return maxHealth > 0f ? CurrentStructuralHealth / maxHealth : 0f;
        }
    }

    public void RecalculateBaseMaximum(bool resetCurrentMaximum = false)
    {
        float total = 0f;
        foreach (var layer in layers)
            if (layer.category != SubPartCategory.Armour)
                total += Mathf.Max(0f, layer.baseMaxHealth);

        float previousBase = baseMaxHealth;
        baseMaxHealth = total;
        if (resetCurrentMaximum || previousBase <= 0f)
            maxHealth = baseMaxHealth;
        else
            maxHealth = Mathf.Clamp(maxHealth + (baseMaxHealth - previousBase), 0f, baseMaxHealth);
    }

    public void ApplyMaxHealthDamage(float amount)
    {
        maxHealth = Mathf.Max(0f, maxHealth - Mathf.Max(0f, amount));
        ClampStructuralHealthToMaximum();
        if (IsDestroyed)
        {
            foreach (var layer in layers)
                if (layer.category != SubPartCategory.Armour)
                    layer.ApplyMaxHealthDamage(layer.maxHealth);
            foreach (var organ in organs)
                organ.ApplyMaxHealthDamage(organ.maxHealth);
        }
    }

    public void RestoreMaxHealth(float amount)
    {
        maxHealth = Mathf.Min(baseMaxHealth, maxHealth + Mathf.Max(0f, amount));
    }

    public void RestoreStructuralMaxHealth(float amount)
    {
        float remaining = Mathf.Max(0f, amount);
        float restored = 0f;
        foreach (var layer in layers)
        {
            if (layer.category == SubPartCategory.Armour || remaining <= 0f) continue;
            float layerRestore = Mathf.Min(remaining,
                Mathf.Max(0f, layer.baseMaxHealth - layer.maxHealth));
            layer.maxHealth += layerRestore;
            remaining -= layerRestore;
            restored += layerRestore;
        }
        RestoreMaxHealth(restored);
    }

    public float RemainingRecoverableStructuralHealth =>
        Mathf.Max(0f, maxHealth - CurrentStructuralHealth);

    private void ClampStructuralHealthToMaximum()
    {
        float rawCurrent = 0f;
        foreach (var layer in layers)
            if (layer.category != SubPartCategory.Armour)
                rawCurrent += Mathf.Max(0f, layer.currentHealth);
        float excess = rawCurrent - maxHealth;
        for (int i = layers.Count - 1; i >= 0 && excess > 0f; i--)
        {
            var layer = layers[i];
            if (layer.category == SubPartCategory.Armour) continue;
            float removed = layer.ApplyDamage(excess);
            excess -= removed;
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
        rt.RecalculateBaseMaximum(true);
        return rt;
    }
}

public class DamageResult
{
    public BodyPart bodyPart;
    public List<LayerHitRecord> layerHits = new List<LayerHitRecord>();
    public List<OrganHitRecord> organHits = new List<OrganHitRecord>();
    public float conditionAfter;
}

public class LayerHitRecord
{
    public RuntimeSubPart runtimeSubPart;
    public string displayName;
    public float incomingDamage;
    public float totalDamage;
    public float hpDamageDealt;
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

[System.Serializable]
public class ColliderBodyPartMapping
{
    public Collider hitCollider;
    public BodyPart bodyPart;
}

public class HealthManager : MonoBehaviour
{
    [Header("Data Sources")]
    public AugmentDb augmentDb;
    public AugmentCatalogue augmentCatalogue;

    [Header("Collider → Body Part Mappings")]
    public List<ColliderBodyPartMapping> colliderMappings;

    [Header("References")]
    public PlayerHealth playerHealth;
    public PlayerEquipment playerEquipment; // for armour destruction callback
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private GameOverController gameOverController;

    [Header("Fire Extinguishing")]
    [SerializeField, Min(.1f), Tooltip("Seconds of sustained sprinting/sliding needed per fire severity tier.")]
    private float extinguishSecondsPerSeverity = 1.5f;
    [SerializeField, Min(.01f), Tooltip("How quickly sprinting counts down an On Fire condition.")]
    private float sprintExtinguishRate = 1f;
    [SerializeField, Min(.01f), Tooltip("How quickly sliding counts down an On Fire condition.")]
    private float slideExtinguishRate = 1f;

    private Dictionary<BodyPart, RuntimeBodyPart> _body = new();
    private Dictionary<Collider, BodyPart> _colliderMap = new();
    private readonly List<InstalledAugmentEffect> _installedAugmentEffects = new();
    private float _currentBiofluid;
    private bool _fatalState;
    private bool _vitalProneLatch;
    private readonly Dictionary<BodyPart, HashSet<BodyPart>> _neighbours = new()
    {
        [BodyPart.Head] = new() { BodyPart.Chest },
        [BodyPart.Chest] = new() { BodyPart.Head, BodyPart.Abdomen, BodyPart.LeftUpperArm, BodyPart.RightUpperArm },
        [BodyPart.Abdomen] = new() { BodyPart.Chest, BodyPart.LeftThigh, BodyPart.RightThigh },
        [BodyPart.LeftUpperArm] = new() { BodyPart.Chest, BodyPart.LeftForearm },
        [BodyPart.RightUpperArm] = new() { BodyPart.Chest, BodyPart.RightForearm },
        [BodyPart.LeftForearm] = new() { BodyPart.LeftUpperArm },
        [BodyPart.RightForearm] = new() { BodyPart.RightUpperArm },
        [BodyPart.LeftThigh] = new() { BodyPart.Abdomen, BodyPart.LeftShin },
        [BodyPart.RightThigh] = new() { BodyPart.Abdomen, BodyPart.RightShin },
        [BodyPart.LeftShin] = new() { BodyPart.LeftThigh },
        [BodyPart.RightShin] = new() { BodyPart.RightThigh }
    };

    public float CurrentBiofluid => _currentBiofluid;
    public float TotalBiofluidRequired => CalculateBiofluidRequirement();
    public bool IsFatal => _fatalState;

    /// <summary>
    /// Normalized distance from a death-rule failure. Only resources that can
    /// actually produce the game-over screen participate in this summary.
    /// </summary>
    public float GetSurvivalRatio()
    {
        if (_fatalState) return 0f;

        float survival = 1f;
        bool hasDeathCriticalResource = false;
        foreach (RuntimeBodyPart body in _body.Values)
        {
            foreach (RuntimeSubPart subPart in EnumerateSubParts(body))
            {
                bool deathCritical = subPart.pumpsBiofluid
                    || subPart.category == SubPartCategory.Brain;
                if (!deathCritical) continue;
                hasDeathCriticalResource = true;
                float maximumRatio = subPart.baseMaxHealth > 0f
                    ? subPart.maxHealth / subPart.baseMaxHealth
                    : 1f;
                float currentRatio = subPart.maxHealth > 0f
                    ? subPart.currentHealth / subPart.maxHealth
                    : 0f;
                survival = Mathf.Min(survival,
                    Mathf.Clamp01(Mathf.Min(maximumRatio, currentRatio)));
            }
        }

        bool biofluidCanKill = HasExistingBiofluidPump() || HasBiofluidDependentBrain();
        if (biofluidCanKill)
        {
            hasDeathCriticalResource = true;
            float requirement = TotalBiofluidRequired;
            float biofluidRatio = requirement > Mathf.Epsilon
                ? _currentBiofluid / requirement
                : (_currentBiofluid > 0f ? 1f : 0f);
            survival = Mathf.Min(survival, Mathf.Clamp01(biofluidRatio));
        }

        return hasDeathCriticalResource ? survival : 1f;
    }

    public IReadOnlyList<BodyPart> GetApplicableHealingTargets(ItemDefinition item)
    {
        var targets = new List<BodyPart>();
        if (item == null || !item.IsConsumable) return targets;
        foreach (var pair in _body)
            if (CanApplyHealing(item, pair.Key)) targets.Add(pair.Key);
        return targets;
    }

    public bool CanApplyHealing(ItemDefinition item, BodyPart part)
    {
        if (item == null || !_body.TryGetValue(part, out var body)) return false;
        if (item.consumableKind == ConsumableKind.Biofluid) return CurrentBiofluid < TotalBiofluidRequired;
        if (item.restoresMaxHealth && body.maxHealth < body.baseMaxHealth) return true;
        foreach (var subPart in EnumerateSubParts(body))
        {
            if (subPart.category == SubPartCategory.Armour) continue;
            if (item.healthRestore > 0f && subPart.currentHealth < subPart.maxHealth) return true;
            foreach (var condition in subPart.conditions)
            {
                bool sourceMatches = item.treatableDamageTypes.Count == 0 || item.treatableDamageTypes.Contains(condition.sourceDamageType);
                bool conditionMatches = item.removableConditions.Count == 0 || item.removableConditions.Contains(condition.type);
                if (sourceMatches && conditionMatches) return true;
            }
        }
        if (item.lifecycleEffects != null)
        {
            foreach (ConsumableLifecycleEffect effect in item.lifecycleEffects)
            {
                if (effect == null || effect.changes is not
                    (ConsumableLifecycleChange.Health or ConsumableLifecycleChange.MaximumHealth)) continue;
                switch (effect.healthTarget)
                {
                    case ConsumableHealthTarget.SelectedLimb:
                        return true;
                    case ConsumableHealthTarget.FixedBodyPart:
                        if (part == effect.targetBodyPart) return true;
                        break;
                    case ConsumableHealthTarget.SpecificOrgan:
                        if (part == effect.targetBodyPart
                            && body.organs.Exists(organ => organ.category == effect.targetOrgan)) return true;
                        break;
                    default:
                        return true;
                }
            }
        }
        return false;
    }

    public bool TryApplyHealing(ItemDefinition item, BodyPart part)
    {
        if (!CanApplyHealing(item, part) || !_body.TryGetValue(part, out var body)) return false;
        if (item.consumableKind == ConsumableKind.Biofluid)
        {
            _currentBiofluid = Mathf.Min(TotalBiofluidRequired, _currentBiofluid + item.biofluidRestore);
            return true;
        }

        if (item.restoresMaxHealth && item.healthRestore > 0f)
            body.RestoreStructuralMaxHealth(item.healthRestore);

        float healingRemaining = Mathf.Max(0f, item.healthRestore);
        foreach (RuntimeSubPart layer in body.layers)
        {
            if (layer.category == SubPartCategory.Armour || healingRemaining <= 0f) continue;
            float restored = Mathf.Min(healingRemaining,
                Mathf.Min(body.RemainingRecoverableStructuralHealth,
                    Mathf.Max(0f, layer.maxHealth - layer.currentHealth)));
            layer.currentHealth += restored;
            healingRemaining -= restored;
        }
        foreach (RuntimeSubPart organ in body.organs)
        {
            if (healingRemaining <= 0f) break;
            float restored = Mathf.Min(healingRemaining,
                Mathf.Max(0f, organ.maxHealth - organ.currentHealth));
            organ.currentHealth += restored;
            healingRemaining -= restored;
        }

        foreach (var subPart in EnumerateSubParts(body))
        {
            for (int i = subPart.conditions.Count - 1; i >= 0; i--)
            {
                var condition = subPart.conditions[i];
                bool sourceMatches = item.treatableDamageTypes.Count == 0 || item.treatableDamageTypes.Contains(condition.sourceDamageType);
                bool conditionMatches = item.removableConditions.Count == 0 || item.removableConditions.Contains(condition.type);
                if (!sourceMatches || !conditionMatches) continue;
                subPart.conditions.RemoveAt(i);
            }
        }
        playerHealth?.OnBodyRegenerated(_body, new[] { part });
        return true;
    }

    public void ApplyConsumableLifecycleEffect(ConsumableLifecycleEffect effect,
        bool hasTargetBodyPart, BodyPart targetBodyPart)
    {
        if (effect == null) return;

        if (effect.changes == ConsumableLifecycleChange.Biofluid)
        {
            _currentBiofluid = Mathf.Clamp(_currentBiofluid + effect.amount,
                0f, TotalBiofluidRequired);
            return;
        }
        if (effect.changes is not (ConsumableLifecycleChange.Health
            or ConsumableLifecycleChange.MaximumHealth)) return;

        var changedParts = new HashSet<BodyPart>();
        switch (effect.healthTarget)
        {
            case ConsumableHealthTarget.SelectedLimb:
                if (hasTargetBodyPart)
                    ApplyLifecycleToWholePart(effect, targetBodyPart, changedParts);
                break;
            case ConsumableHealthTarget.FixedBodyPart:
                ApplyLifecycleToWholePart(effect, effect.targetBodyPart, changedParts);
                break;
            case ConsumableHealthTarget.SpecificOrgan:
                if (_body.TryGetValue(effect.targetBodyPart, out RuntimeBodyPart organBody))
                    ApplyLifecycleToSubParts(effect, organBody, organBody.organs,
                        subPart => subPart.category == effect.targetOrgan, changedParts);
                break;
            case ConsumableHealthTarget.AllSkin:
                ApplyLifecycleToCategory(effect, SubPartCategory.Skin, false, changedParts);
                break;
            case ConsumableHealthTarget.AllMuscle:
                ApplyLifecycleToCategory(effect, SubPartCategory.Muscle, false, changedParts);
                break;
            case ConsumableHealthTarget.AllBone:
                ApplyLifecycleToCategory(effect, SubPartCategory.Bone, false, changedParts);
                break;
            case ConsumableHealthTarget.AllOrgans:
                foreach (RuntimeBodyPart body in _body.Values)
                    ApplyLifecycleToSubParts(effect, body, body.organs, _ => true, changedParts);
                break;
            case ConsumableHealthTarget.EntireBody:
                foreach (BodyPart part in _body.Keys)
                    ApplyLifecycleToWholePart(effect, part, changedParts);
                break;
        }

        if (changedParts.Count > 0)
        {
            playerHealth?.OnBodyRegenerated(_body, changedParts);
            EvaluateInjuryConsequences();
        }
    }

    private void ApplyLifecycleToWholePart(ConsumableLifecycleEffect effect, BodyPart part,
        HashSet<BodyPart> changedParts)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart body)) return;
        if (effect.changes == ConsumableLifecycleChange.MaximumHealth)
        {
            if (effect.amount <= 0f) return;
            body.RestoreStructuralMaxHealth(effect.amount);
            changedParts.Add(part);
            return;
        }

        if (effect.amount < 0f)
        {
            ReceiveDamageOnPart(part, new List<(DamageType type, float amount)>
            {
                (effect.negativeHealthDamageType, -effect.amount)
            });
            return;
        }
        if (effect.amount <= 0f) return;

        float healingRemaining = effect.amount;
        foreach (RuntimeSubPart layer in body.layers)
        {
            if (layer.category == SubPartCategory.Armour || healingRemaining <= 0f) continue;
            float restored = Mathf.Min(healingRemaining,
                Mathf.Min(body.RemainingRecoverableStructuralHealth,
                    Mathf.Max(0f, layer.maxHealth - layer.currentHealth)));
            layer.currentHealth += restored;
            healingRemaining -= restored;
        }
        foreach (RuntimeSubPart organ in body.organs)
        {
            if (healingRemaining <= 0f) break;
            float restored = Mathf.Min(healingRemaining,
                Mathf.Max(0f, organ.maxHealth - organ.currentHealth));
            organ.currentHealth += restored;
            healingRemaining -= restored;
        }
        changedParts.Add(part);
    }

    private void ApplyLifecycleToCategory(ConsumableLifecycleEffect effect,
        SubPartCategory category, bool organs, HashSet<BodyPart> changedParts)
    {
        foreach (RuntimeBodyPart body in _body.Values)
            ApplyLifecycleToSubParts(effect, body, organs ? body.organs : body.layers,
                subPart => subPart.category == category, changedParts);
    }

    private void ApplyLifecycleToSubParts(ConsumableLifecycleEffect effect, RuntimeBodyPart body,
        IEnumerable<RuntimeSubPart> candidates, System.Func<RuntimeSubPart, bool> predicate,
        HashSet<BodyPart> changedParts)
    {
        bool changed = false;
        float structuralMaxRestored = 0f;
        foreach (RuntimeSubPart subPart in candidates)
        {
            if (!predicate(subPart) || subPart.category == SubPartCategory.Armour) continue;
            if (effect.changes == ConsumableLifecycleChange.MaximumHealth)
            {
                float restored = Mathf.Min(Mathf.Max(0f, effect.amount),
                    Mathf.Max(0f, subPart.baseMaxHealth - subPart.maxHealth));
                subPart.maxHealth += restored;
                if (!subPart.isOrgan) structuralMaxRestored += restored;
                changed |= restored > 0f;
            }
            else if (effect.amount >= 0f)
            {
                float restored = Mathf.Min(effect.amount,
                    Mathf.Max(0f, subPart.maxHealth - subPart.currentHealth));
                subPart.currentHealth += restored;
                changed |= restored > 0f;
            }
            else
            {
                float damage = -effect.amount * subPart.GetMultiplier(effect.negativeHealthDamageType);
                changed |= subPart.ApplyDamage(damage) > 0f;
            }
        }

        if (!changed) return;
        if (structuralMaxRestored > 0f) body.RestoreMaxHealth(structuralMaxRestored);
        changedParts.Add(body.bodyPart);
    }

    private class InstalledAugmentEffect
    {
        public AugmentEntry entry;
        public RuntimeBodyPart bodyPart;
        public RuntimeSubPart subPart;
        public ItemInstance item;

        public bool IsFunctional => subPart != null
            ? subPart.IsFunctional
            : bodyPart != null && !bodyPart.IsDisabled && !bodyPart.IsDestroyed;
    }

    private void Awake()
    {
        if (playerCharacter == null)
            playerCharacter = GetComponent<PlayerCharacter>() ?? GetComponentInParent<PlayerCharacter>();
        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>() ?? GetComponentInParent<PlayerEquipment>();
        if (gameOverController == null) gameOverController = GetComponent<GameOverController>();
        InitialiseBody();
        BuildColliderMap();
    }

    private void Start()
    {
        playerHealth?.OnBodyInitialised(_body);
    }

    private void Update()
    {
        UpdateConditions(Time.deltaTime);
        EvaluateInjuryConsequences();
        if (_fatalState) return;
        UpdateBiofluid(Time.deltaTime);
        EvaluateInjuryConsequences();
    }

    private void InitialiseBody()
    {
        _body.Clear();
        foreach (var def in augmentDb.bodyParts)
            _body[def.bodyPart] = RuntimeBodyPart.FromDefinition(def);
        _currentBiofluid = CalculateBiofluidRequirement();
    }

    private void BuildColliderMap()
    {
        _colliderMap.Clear();
        foreach (var mapping in colliderMappings)
        {
            if (mapping.hitCollider == null) continue;
            _colliderMap[mapping.hitCollider] = mapping.bodyPart;
        }
    }

    public void ReceiveDamage(Collider hitCollider,
        List<(DamageType type, float amount)> damageEntries)
    {
        if (!_colliderMap.TryGetValue(hitCollider, out BodyPart part)) return;
        ReceiveDamageOnPart(part, damageEntries);
    }

    public void ReceiveDamageOnPart(BodyPart part,
        List<(DamageType type, float amount)> damageEntries)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart bodyPart)) return;

        var result = new DamageResult { bodyPart = part };
        var log = new StringBuilder();

        log.AppendLine("═══════════════════════════════");
        log.AppendLine($"[DAMAGE REPORT] {bodyPart.displayName}");
        log.AppendLine("Incoming:");
        foreach (var (type, amount) in damageEntries)
            log.AppendLine($" - {type}: {amount}");

        foreach (var (type, amount) in damageEntries)
            ProcessDamageType(bodyPart, type, amount, result, log);

        result.conditionAfter = bodyPart.ConditionRatio;

        log.AppendLine($"[FINAL CONDITION] {result.conditionAfter:F2}");
        log.AppendLine("═══════════════════════════════");
        Debug.Log(log.ToString());

        CheckInstalledAugmentDurability(result, log);
        CheckArmourDestruction(part, result, log);

        playerHealth?.OnDamageReceived(result);
        RefreshAugmentStatOverrides();
        EvaluateInjuryConsequences();
    }

    // ── Armour destruction check ──────────────────────────────────────────────

    private void CheckArmourDestruction(BodyPart part, DamageResult result, StringBuilder log)
    {
        if (playerEquipment == null) return;

        var equipped = playerEquipment.GetArmourSlot(part);
        if (equipped == null || !equipped.definition.IsArmour) return;

        // Armour is the first layer hit when equipped, so use its absorbed damage
        // as the source amount for the item's degradation multiplier.
        foreach (var hit in result.layerHits)
        {
            if (hit.runtimeSubPart == playerEquipment.GetArmourLayer(part))
            {
                bool broken = playerEquipment.ApplyArmourDamage(part, hit.hpDamageDealt);
                if (hit.destroyed || broken)
                {
                    if (hit.destroyed) equipped.Break();
                    log.AppendLine($"\n[ARMOUR] '{equipped.definition.displayName}' destroyed — auto-unequipping.");
                    StartCoroutine(DeferredUnequipArmour(part));
                }
                break;
            }
        }
    }

    private System.Collections.IEnumerator DeferredUnequipArmour(BodyPart part)
    {
        yield return null; // wait one frame
        playerEquipment.UnequipArmour(part);
    }

    // ── Core damage cascade ───────────────────────────────────────────────────

    private void ProcessDamageType(
        RuntimeBodyPart bodyPart,
        DamageType type,
        float incoming,
        DamageResult result,
        StringBuilder log)
    {
        if (incoming <= 0f) return;
        if (bodyPart.IsDestroyed) return;
        if (bodyPart.IsDisabled)
        {
            float maximumDamage = CalculateLimbMaximumDamage(bodyPart, type, incoming);
            bodyPart.ApplyMaxHealthDamage(maximumDamage);
            log.AppendLine($"[LIMB MAX HP] {bodyPart.displayName}: -{maximumDamage:F1} {type}, {bodyPart.maxHealth:F1}/{bodyPart.baseMaxHealth:F1}");
            return;
        }
        int firstHit = result.layerHits.Count;
        switch (type)
        {
            case DamageType.Slash: ProcessSlash(bodyPart, incoming, result, log); break;
            case DamageType.Pierce: ProcessPierce(bodyPart, incoming, result, log); break;
            case DamageType.Blunt: ProcessBlunt(bodyPart, incoming, result, log); break;
            case DamageType.Burn: ProcessBurn(bodyPart, incoming, result, log); break;
            case DamageType.Shock: ProcessShock(bodyPart.bodyPart, incoming, result, log); break;
        }

        // Organs sit beyond the structural cascade. They are eligible only when
        // this damage reached and breached the innermost currently functional layer.
        if (type != DamageType.Shock && result.layerHits.Count > firstHit)
        {
            LayerHitRecord finalHit = result.layerHits[result.layerHits.Count - 1];
            int finalIndex = bodyPart.layers.IndexOf(finalHit.runtimeSubPart);
            if (finalIndex >= 0 && finalHit.breached && !HasFunctionalLayerAfter(bodyPart, finalIndex))
                ProcessOrganRolls(bodyPart, type, finalHit.totalDamage, result, log);
        }
    }

    private static float CalculateLimbMaximumDamage(RuntimeBodyPart part, DamageType type, float incoming)
    {
        var structure = new List<RuntimeSubPart>();
        foreach (RuntimeSubPart layer in part.layers)
            if (layer.category != SubPartCategory.Armour) structure.Add(layer);
        if (structure.Count == 0) return incoming;

        float carried = incoming;
        float total = 0f;
        for (int i = 0; i < structure.Count; i++)
        {
            RuntimeSubPart layer = structure[i];
            float coefficient = carried * layer.GetMultiplier(type);
            bool first = i == 0;
            bool last = i == structure.Count - 1;
            switch (type)
            {
                case DamageType.Pierce:
                    total += last ? carried + coefficient : coefficient;
                    break;
                case DamageType.Slash:
                case DamageType.Blunt:
                    total += first ? carried : carried + coefficient;
                    break;
                case DamageType.Burn:
                case DamageType.Shock:
                    total += carried + coefficient;
                    break;
            }
            carried = coefficient;
        }
        return Mathf.Max(0f, total);
    }

    private void ProcessSlash(RuntimeBodyPart part, float incoming, DamageResult result, StringBuilder log)
    {
        float baseDamage = incoming;
        RuntimeSubPart strongest = null; int highestTier = 0;
        bool firstActiveLayer = true;
        for (int i = 0; i < part.layers.Count; i++)
        {
            var layer = part.layers[i]; if (!layer.IsFunctional) continue;
            float coefficientDamage = baseDamage * layer.GetMultiplier(DamageType.Slash);
            float breachScore = baseDamage + coefficientDamage;
            float hpDamage = firstActiveLayer ? baseDamage : breachScore;
            bool breached = breachScore > layer.breachThreshold || hpDamage >= layer.currentHealth;
            ApplyLayerHit(layer, incoming, breachScore, hpDamage, breached, result);
            int tier = HealthConditionRules.SeverityTier(hpDamage, layer.baseMaxHealth);
            if (tier > highestTier) { highestTier = tier; strongest = layer; }
            if (!breached) break;
            baseDamage = coefficientDamage;
            firstActiveLayer = false;
        }
        ApplySlashCondition(part, strongest, highestTier, log);
    }

    private void ProcessPierce(RuntimeBodyPart part, float incoming, DamageResult result, StringBuilder log)
    {
        float baseDamage = incoming;
        RuntimeSubPart strongest = null; int highestTier = 0;
        for (int i = 0; i < part.layers.Count; i++)
        {
            var layer = part.layers[i]; if (!layer.IsFunctional) continue;
            float coefficientDamage = baseDamage * layer.GetMultiplier(DamageType.Pierce);
            float breachScore = baseDamage + coefficientDamage;
            bool breached = breachScore > layer.breachThreshold || coefficientDamage >= layer.currentHealth;
            bool isLast = !HasFunctionalLayerAfter(part, i) || !breached;
            float hpDamage = isLast ? breachScore : coefficientDamage;
            ApplyLayerHit(layer, incoming, breachScore, hpDamage, breached, result);
            int tier = HealthConditionRules.SeverityTier(hpDamage, layer.baseMaxHealth);
            if (tier > highestTier) { highestTier = tier; strongest = layer; }
            if (isLast) break;
            baseDamage = coefficientDamage;
        }
        ApplyPierceCondition(part, strongest, highestTier, log);
    }

    private void ProcessBlunt(RuntimeBodyPart part, float incoming, DamageResult result, StringBuilder log)
    {
        float baseDamage = incoming;
        RuntimeSubPart strongest = null; int highestTier = 0;
        bool firstActiveLayer = true;
        for (int i = 0; i < part.layers.Count; i++)
        {
            var layer = part.layers[i]; if (!layer.IsFunctional) continue;
            float coefficientDamage = baseDamage * layer.GetMultiplier(DamageType.Blunt);
            float breachScore = baseDamage + coefficientDamage;
            float hpDamage = firstActiveLayer ? baseDamage : breachScore;
            bool breached = breachScore > layer.breachThreshold || hpDamage >= layer.currentHealth;
            ApplyLayerHit(layer, incoming, breachScore, hpDamage, breached, result);
            int tier = HealthConditionRules.SeverityTier(hpDamage, layer.baseMaxHealth);
            if (tier > highestTier) { highestTier = tier; strongest = layer; }
            if (!breached) break;
            baseDamage = coefficientDamage;
            firstActiveLayer = false;
        }
        if (strongest != null && highestTier > 0)
            strongest.AddOrRefreshCondition(new RuntimeHealthCondition { type = highestTier >= 4 ? HealthConditionType.Fracture : HealthConditionType.Burned, sourceDamageType = DamageType.Blunt, requiresTreatment = highestTier >= 3 });
    }

    private void ProcessBurn(RuntimeBodyPart part, float incoming, DamageResult result, StringBuilder log)
    {
        float carriedDamage = incoming;
        for (int i = 0; i < part.layers.Count; i++)
        {
            RuntimeSubPart layer = part.layers[i];
            if (!layer.IsFunctional) continue;
            float coefficientDamage = carriedDamage * layer.GetMultiplier(DamageType.Burn);
            float damage = carriedDamage + coefficientDamage;
            bool breached = damage > layer.breachThreshold || damage >= layer.currentHealth;
            ApplyLayerHit(layer, incoming, damage, damage, breached, result);
            float chance = Mathf.Clamp01(damage / Mathf.Max(1f, layer.baseMaxHealth) * layer.flammabilityModifier);
            if (layer.flammable && Random.value < chance)
                layer.AddOrRefreshCondition(CreateOnFireCondition(damage, layer.baseMaxHealth));
            if (!breached) break;
            carriedDamage = coefficientDamage;
        }
    }

    private void ProcessShock(BodyPart origin, float incoming, DamageResult result, StringBuilder log)
    {
        var queue = new Queue<(BodyPart part, int layerIndex, float energy)>();
        var visited = new HashSet<(BodyPart, int)>();
        queue.Enqueue((origin, 0, incoming));
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add((node.part, node.layerIndex)) || !_body.TryGetValue(node.part, out var body) || node.layerIndex >= body.layers.Count) continue;
            int activeIndex = FindNextFunctionalLayerIndex(body, node.layerIndex);
            if (activeIndex < 0) continue;
            if (activeIndex != node.layerIndex && !visited.Add((node.part, activeIndex))) continue;
            var layer = body.layers[activeIndex];
            float coefficientDamage = node.energy * layer.GetMultiplier(DamageType.Shock);
            float breachScore = node.energy + coefficientDamage;
            bool breached = breachScore > layer.breachThreshold || breachScore >= layer.currentHealth;
            ApplyLayerHit(layer, node.energy, breachScore, breachScore, breached, result);
            layer.AddOrRefreshCondition(new RuntimeHealthCondition { type = HealthConditionType.Malfunction, sourceDamageType = DamageType.Shock, remainingSeconds = 5f });
            var destinations = new List<(BodyPart, int)>();
            if (breached && activeIndex + 1 < body.layers.Count) destinations.Add((node.part, activeIndex + 1));
            if (_neighbours.TryGetValue(node.part, out var neighbours)) foreach (var neighbour in neighbours) destinations.Add((neighbour, activeIndex));
            if (destinations.Count == 0) continue;
            float split = coefficientDamage / destinations.Count;
            foreach (var destination in destinations) queue.Enqueue((destination.Item1, destination.Item2, split));
        }
    }

    private static void ApplyLayerHit(RuntimeSubPart layer, float incoming, float total, float damage, bool breached, DamageResult result)
    {
        float dealt = layer.ApplyDamage(damage);
        result.layerHits.Add(new LayerHitRecord { runtimeSubPart = layer, displayName = layer.displayName, incomingDamage = incoming, totalDamage = total, hpDamageDealt = dealt, healthAfter = layer.currentHealth, breached = breached, destroyed = layer.IsDepleted });
    }

    private static bool HasFunctionalLayerAfter(RuntimeBodyPart part, int index)
    {
        for (int i = index + 1; i < part.layers.Count; i++)
            if (part.layers[i].IsFunctional) return true;
        return false;
    }

    private static int FindNextFunctionalLayerIndex(RuntimeBodyPart part, int startIndex)
    {
        for (int i = Mathf.Max(0, startIndex); i < part.layers.Count; i++)
            if (part.layers[i].IsFunctional) return i;
        return -1;
    }

    private static void ApplySlashCondition(RuntimeBodyPart part, RuntimeSubPart layer, int tier, StringBuilder log)
    {
        if (layer == null || tier == 0 || !layer.requiresBiofluid) return;
        float maxPenalty = tier >= 4 ? layer.baseMaxHealth * .1f : 0f;
        if (maxPenalty > 0f)
        {
            layer.ApplyMaxHealthDamage(maxPenalty);
            part.ApplyMaxHealthDamage(maxPenalty);
        }
        layer.AddOrRefreshCondition(new RuntimeHealthCondition { type = tier >= 5 ? HealthConditionType.Severed : HealthConditionType.Bleeding, sourceDamageType = DamageType.Slash, remainingSeconds = HealthConditionRules.DurationForTier(tier) * layer.biofluidProfile.coagulationCoefficient, biofluidLossPerSecond = layer.baseMaxHealth * .1f * layer.biofluidProfile.bleedRate, requiresTreatment = tier >= 5, maxHealthPenalty = maxPenalty });
    }

    private static void ApplyPierceCondition(RuntimeBodyPart part, RuntimeSubPart layer, int tier, StringBuilder log)
    {
        if (layer == null || tier == 0 || !layer.requiresBiofluid) return;
        bool intrusive = tier >= 4 || (tier > 0 && Random.value < tier * .05f);
        float maxPenalty = intrusive ? layer.baseMaxHealth * .1f : 0f;
        if (maxPenalty > 0f)
        {
            layer.ApplyMaxHealthDamage(maxPenalty);
            part.ApplyMaxHealthDamage(maxPenalty);
        }
        layer.AddOrRefreshCondition(new RuntimeHealthCondition { type = intrusive ? HealthConditionType.IntrusiveObject : HealthConditionType.Bleeding, sourceDamageType = DamageType.Pierce, remainingSeconds = HealthConditionRules.DurationForTier(tier) * layer.biofluidProfile.coagulationCoefficient, biofluidLossPerSecond = layer.baseMaxHealth * .1f * layer.biofluidProfile.bleedRate, requiresTreatment = intrusive, maxHealthPenalty = maxPenalty });
    }

    private void ProcessOrganRolls(
        RuntimeBodyPart bodyPart,
        DamageType type,
        float incoming,
        DamageResult result,
        StringBuilder log)
    {
        log.AppendLine("\n[ORGANS]");

        foreach (var organ in bodyPart.organs)
        {
            if (!organ.IsFunctional) continue;

            float roll = Random.value;
            log.AppendLine($"\n{organ.displayName}");
            log.AppendLine($"Roll: {roll} vs {organ.organHitChance}");

            if (roll > organ.organHitChance) { log.AppendLine("Miss"); continue; }

            float mult = organ.GetMultiplier(type);
            float damage = incoming * mult;
            float dealt = organ.ApplyDamage(damage);

            log.AppendLine($"HIT → {incoming} × {mult} = {damage}");
            log.AppendLine($"HP After: {organ.currentHealth}");

            result.organHits.Add(new OrganHitRecord
            {
                displayName = organ.displayName,
                incomingDamage = incoming,
                totalDamage = damage,
                hpDamageDealt = dealt,
                healthAfter = organ.currentHealth,
                destroyed = organ.IsDepleted
            });
        }
    }

    // ── Augment install / swap ────────────────────────────────────────────────

    public bool TryInstallAugment(ItemInstance item)
    {
        if (item?.definition == null || !item.definition.IsAugment || item.IsBroken ||
            !augmentCatalogue.TryGetAugment(item.definition.itemID, out var entry))
            return false;

        return entry.isSubPartAugment
            ? InstallSubPartAugment(entry, item)
            : InstallFullAugment(entry, item);
    }

    public void InstallFullAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        InstallFullAugment(entry, null);
    }

    private bool InstallFullAugment(AugmentEntry entry, ItemInstance item)
    {
        if (entry == null || entry.isSubPartAugment) return false;
        ReturnReplacedAugments(effect => effect.entry.targetBodyPart == entry.targetBodyPart);
        RuntimeBodyPart runtimePart = RuntimeBodyPart.FromDefinition(entry.definition);
        _body[entry.targetBodyPart] = runtimePart;
        _installedAugmentEffects.Add(new InstalledAugmentEffect { entry = entry, bodyPart = runtimePart, item = item });
        RefreshAugmentStatOverrides();
        Debug.Log($"[HealthManager] Full augment installed: {entry.displayName} → {entry.targetBodyPart}");
        playerHealth?.OnBodyInitialised(_body);
        return true;
    }

    public void InstallSubPartAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        InstallSubPartAugment(entry, null);
    }

    private bool InstallSubPartAugment(AugmentEntry entry, ItemInstance item)
    {
        if (entry == null || !entry.isSubPartAugment) return false;

        if (!_body.TryGetValue(entry.targetBodyPart, out RuntimeBodyPart bodyPart))
        {
            Debug.LogWarning($"[HealthManager] Body part {entry.targetBodyPart} not found.");
            return false;
        }

        var newRuntime = RuntimeSubPart.FromDefinition(entry.subPartDefinition);

        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            if (bodyPart.layers[i].category == entry.targetSubPartCategory)
            {
                bodyPart.layers[i] = newRuntime;
                bodyPart.RecalculateBaseMaximum();
                TrackSubPartAugmentEffect(entry, bodyPart, newRuntime, item);
                RefreshAugmentStatOverrides();
                playerHealth?.OnBodyInitialised(_body);
                return true;
            }
        }
        for (int i = 0; i < bodyPart.organs.Count; i++)
        {
            if (bodyPart.organs[i].category == entry.targetSubPartCategory)
            {
                bodyPart.organs[i] = newRuntime;
                TrackSubPartAugmentEffect(entry, bodyPart, newRuntime, item);
                RefreshAugmentStatOverrides();
                playerHealth?.OnBodyInitialised(_body);
                return true;
            }
        }

        Debug.LogWarning($"[HealthManager] No matching sub-part '{entry.targetSubPartCategory}' in {entry.targetBodyPart}.");
        return false;
    }

    private void TrackSubPartAugmentEffect(AugmentEntry entry, RuntimeBodyPart bodyPart, RuntimeSubPart subPart, ItemInstance item)
    {
        ReturnReplacedAugments(effect => effect.entry.targetBodyPart == entry.targetBodyPart
            && effect.entry.isSubPartAugment
            && effect.entry.targetSubPartCategory == entry.targetSubPartCategory);
        _installedAugmentEffects.Add(new InstalledAugmentEffect
        {
            entry = entry,
            bodyPart = bodyPart,
            subPart = subPart,
            item = item
        });
    }

    private void CheckInstalledAugmentDurability(DamageResult result, StringBuilder log)
    {
        foreach (var effect in new List<InstalledAugmentEffect>(_installedAugmentEffects))
        {
            if (effect.item == null) continue;

            float sourceDamage = 0f;
            bool runtimeDestroyed = false;
            foreach (var hit in result.layerHits)
            {
                if (hit.runtimeSubPart.category == SubPartCategory.Armour) continue;

                bool isThisAugment = effect.subPart != null
                    ? hit.runtimeSubPart == effect.subPart
                    : effect.bodyPart != null && effect.bodyPart.bodyPart == result.bodyPart;
                if (!isThisAugment) continue;

                sourceDamage += hit.hpDamageDealt;
                runtimeDestroyed |= hit.destroyed;
            }

            if (sourceDamage <= 0f) continue;
            effect.item.ApplyDurabilityDamage(sourceDamage);
            if (!runtimeDestroyed && !effect.item.IsBroken) continue;

            BreakInstalledAugment(effect);
            log.AppendLine($"\n[AUGMENT] '{effect.entry.displayName}' destroyed — returned broken to inventory.");
            ReturnInstalledAugment(effect, true);
        }
    }

    private static void BreakInstalledAugment(InstalledAugmentEffect effect)
    {
        if (effect.subPart != null)
        {
            effect.subPart.currentHealth = 0f;
            return;
        }

        if (effect.bodyPart == null) return;
        foreach (var layer in effect.bodyPart.layers) layer.currentHealth = 0f;
        foreach (var organ in effect.bodyPart.organs) organ.currentHealth = 0f;
    }

    private void ReturnReplacedAugments(System.Predicate<InstalledAugmentEffect> match)
    {
        var replaced = _installedAugmentEffects.FindAll(match);
        foreach (var effect in replaced) ReturnInstalledAugment(effect, false);
    }

    private void ReturnInstalledAugment(InstalledAugmentEffect effect, bool broken)
    {
        if (!_installedAugmentEffects.Remove(effect)) return;
        if (effect.item != null)
        {
            if (broken) effect.item.Break();
            playerEquipment?.ReturnItemToInventory(effect.item);
        }
    }

    private void RefreshAugmentStatOverrides()
    {
        if (playerCharacter == null) playerCharacter = GetComponent<PlayerCharacter>();
        if (playerCharacter == null) return;

        var activeOverrides = new List<AugmentStatOverride>();
        foreach (InstalledAugmentEffect effect in _installedAugmentEffects)
            if (effect.IsFunctional && effect.entry.statOverrides != null)
                activeOverrides.AddRange(effect.entry.statOverrides);

        // Effects are collected in installation order, so later installed
        // augments deterministically override an earlier duplicate stat.
        playerCharacter.ApplyAugmentStatOverrides(activeOverrides);
    }

    private void UpdateBiofluid(float deltaTime)
    {
        float capacity = CalculateBiofluidRequirement();
        if (capacity <= 0f)
        {
            _currentBiofluid = 0f;
            return;
        }

        _currentBiofluid = Mathf.Min(capacity, _currentBiofluid + CalculateBiofluidProduction() * deltaTime);
        float pumpEfficiency = CalculatePumpEfficiency();
        if (pumpEfficiency <= 0f) return;

        var healedParts = new HashSet<BodyPart>();
        foreach (var pair in _body)
        {
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
            {
                if (subPart.category == SubPartCategory.Armour || subPart.IsDestroyed || subPart.healingRate <= 0f
                    || subPart.currentHealth >= subPart.maxHealth)
                    continue;

                float recoverable = subPart.isOrgan
                    ? subPart.maxHealth - subPart.currentHealth
                    : Mathf.Min(subPart.maxHealth - subPart.currentHealth,
                        pair.Value.RemainingRecoverableStructuralHealth);
                float healing = Mathf.Min(recoverable,
                    subPart.healingRate * pumpEfficiency
                    * (playerCharacter != null ? playerCharacter.SaturationEfficiency : 1f)
                    * deltaTime);
                if (subPart.requiresBiofluid && subPart.biofluidRequirement > 0f)
                {
                    float biofluidPerHealth = subPart.biofluidRequirement / Mathf.Max(subPart.maxHealth, Mathf.Epsilon);
                    healing = Mathf.Min(healing, _currentBiofluid / biofluidPerHealth);
                    _currentBiofluid -= healing * biofluidPerHealth;
                }

                if (healing <= 0f) continue;
                subPart.currentHealth += healing;
                healedParts.Add(pair.Key);
            }
        }

        if (healedParts.Count > 0)
            playerHealth?.OnBodyRegenerated(_body, healedParts);
    }

    private void UpdateConditions(float deltaTime)
    {
        var changedParts = new HashSet<BodyPart>();
        foreach (var pair in _body)
        {
            foreach (var subPart in EnumerateSubParts(pair.Value))
            {
                for (int i = subPart.conditions.Count - 1; i >= 0; i--)
                {
                    var condition = subPart.conditions[i];
                    if (condition.type is HealthConditionType.Bleeding or HealthConditionType.InternalBleeding)
                        _currentBiofluid = Mathf.Max(0f, _currentBiofluid - condition.biofluidLossPerSecond * deltaTime);

                    if (condition.type == HealthConditionType.OnFire)
                    {
                        Stance stance = playerCharacter != null
                            ? playerCharacter.GetState().Stance
                            : Stance.Stand;
                        bool cooling = stance is Stance.Sprint or Stance.Slide;
                        float change = deltaTime / Mathf.Max(.01f, subPart.flammabilityModifier);
                        condition.tickDamage = cooling ? Mathf.Max(0f, condition.tickDamage - change * 2f) : condition.tickDamage + change;

                        if (cooling)
                        {
                            float extinguishRate = stance == Stance.Slide
                                ? slideExtinguishRate
                                : sprintExtinguishRate;
                            condition.remainingSeconds -= deltaTime * extinguishRate;
                            if (condition.remainingSeconds <= 0f)
                            {
                                subPart.conditions.RemoveAt(i);
                                changedParts.Add(pair.Key);
                                continue;
                            }
                        }

                        float tickAmount = condition.tickDamage * deltaTime;
                        if (pair.Value.IsDisabled)
                            pair.Value.ApplyMaxHealthDamage(tickAmount);
                        else
                            subPart.ApplyDamage(tickAmount);
                        changedParts.Add(pair.Key);

                        if (subPart.IsDepleted && !pair.Value.IsDisabled)
                        {
                            RuntimeSubPart nextLayer = pair.Value.layers.Find(layer => layer.IsFunctional);
                            if (nextLayer != null && nextLayer != subPart)
                            {
                                nextLayer.AddOrRefreshCondition(new RuntimeHealthCondition
                                {
                                    type = HealthConditionType.OnFire,
                                    sourceDamageType = DamageType.Burn,
                                    severity = condition.severity,
                                    remainingSeconds = condition.remainingSeconds,
                                    requiresTreatment = true,
                                    tickDamage = condition.tickDamage
                                });
                                subPart.conditions.RemoveAt(i);
                                continue;
                            }
                        }

                        if (!cooling && Random.value < Mathf.Clamp01(condition.tickDamage / Mathf.Max(1f, subPart.baseMaxHealth) * subPart.flammabilityModifier))
                            SpreadFire(pair.Key, subPart.category, condition.tickDamage);
                    }

                    if (!condition.IsPermanent && !condition.requiresTreatment)
                    {
                        condition.remainingSeconds -= deltaTime;
                        if (condition.remainingSeconds <= 0f) subPart.conditions.RemoveAt(i);
                    }
                }
            }
        }
        if (changedParts.Count > 0) playerHealth?.OnBodyRegenerated(_body, changedParts);
    }

    private void SpreadFire(BodyPart source, SubPartCategory category, float intensity)
    {
        if (!_neighbours.TryGetValue(source, out var neighbours)) return;
        foreach (var neighbour in neighbours)
        {
            if (!_body.TryGetValue(neighbour, out var body)) continue;
            var layer = body.layers.Find(candidate => candidate.category == category && candidate.IsFunctional);
            if (layer == null || !layer.flammable) continue;
            float chance = Mathf.Clamp01(intensity / Mathf.Max(1f, layer.baseMaxHealth) * layer.flammabilityModifier);
            if (Random.value < chance)
                layer.AddOrRefreshCondition(CreateOnFireCondition(intensity, layer.baseMaxHealth));
        }
    }

    private RuntimeHealthCondition CreateOnFireCondition(float damage, float baseMaxHealth)
    {
        int severity = Mathf.Clamp(HealthConditionRules.SeverityTier(damage, baseMaxHealth), 1, 5);
        return new RuntimeHealthCondition
        {
            type = HealthConditionType.OnFire,
            sourceDamageType = DamageType.Burn,
            severity = severity,
            remainingSeconds = severity * extinguishSecondsPerSeverity,
            requiresTreatment = true
        };
    }

    private float CalculateBiofluidRequirement()
    {
        float total = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.IsFunctional && subPart.requiresBiofluid)
                    total += Mathf.Max(0f, subPart.biofluidRequirement);
        return total;
    }

    private float CalculateBiofluidProduction()
    {
        float production = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.IsFunctional && subPart.producesBiofluid)
                    production += Mathf.Max(0f, subPart.biofluidProductionRate);
        return production;
    }

    private float CalculatePumpEfficiency()
    {
        float efficiency = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.IsFunctional && subPart.pumpsBiofluid)
                    efficiency += Mathf.Max(0f, subPart.biofluidPumpEfficiency);
        return efficiency;
    }

    private static IEnumerable<RuntimeSubPart> EnumerateSubParts(RuntimeBodyPart bodyPart)
    {
        foreach (RuntimeSubPart layer in bodyPart.layers) yield return layer;
        foreach (RuntimeSubPart organ in bodyPart.organs) yield return organ;
    }

    private void EvaluateInjuryConsequences()
    {
        bool hasBiofluidDependentVital = HasExistingBiofluidPump() || HasBiofluidDependentBrain();
        string deathReason = GetDeathReason(hasBiofluidDependentVital);
        if (deathReason != null)
        {
            _fatalState = true;
            playerHealth?.NotifyDeath();
            if (gameOverController == null) gameOverController = GetComponent<GameOverController>();
            if (gameOverController == null) gameOverController = gameObject.AddComponent<GameOverController>();
            gameOverController.ShowGameOver(deathReason);
            return;
        }

        if (!_vitalProneLatch)
            _vitalProneLatch = HasIncapacitatedBiofluidPump() || HasIncapacitatedBrain();
        else if (AreVitalsAboveRecoveryThreshold())
            _vitalProneLatch = false;
        bool vitalIncapacitated = _vitalProneLatch;
        bool legOrSpineFailed = IsLimbDisabled(BodyPart.LeftThigh)
            || IsLimbDisabled(BodyPart.LeftShin)
            || IsLimbDisabled(BodyPart.RightThigh)
            || IsLimbDisabled(BodyPart.RightShin)
            // The body model has no Spine enum, so torso bone layers represent it.
            || IsSubPartDepleted(BodyPart.Chest, SubPartCategory.Bone)
            || IsSubPartDepleted(BodyPart.Abdomen, SubPartCategory.Bone);
        playerCharacter?.SetForcedCrawl(legOrSpineFailed || vitalIncapacitated);

        bool leftArmFailed = HasArmFailure(BodyPart.LeftUpperArm) || HasArmFailure(BodyPart.LeftForearm);
        bool rightArmFailed = HasArmFailure(BodyPart.RightUpperArm) || HasArmFailure(BodyPart.RightForearm);
        if (leftArmFailed)
            playerEquipment?.DisableHandAndDrop(HandSlot.Left);
        else
            playerEquipment?.EnableHand(HandSlot.Left);
        if (rightArmFailed)
            playerEquipment?.DisableHandAndDrop(HandSlot.Right);
        else
            playerEquipment?.EnableHand(HandSlot.Right);
    }

    private bool HasArmFailure(BodyPart part) => IsLimbDisabled(part);

    private string GetDeathReason(bool hasBiofluidDependentVital)
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.category == SubPartCategory.Brain && subPart.IsDestroyed)
                    return $"{subPart.displayName} destroyed: maximum health reached zero.";

        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.pumpsBiofluid && subPart.IsDestroyed)
                    return $"Biofluid pump destroyed: {subPart.displayName} maximum health reached zero.";

        if (_currentBiofluid <= 0f && hasBiofluidDependentVital)
        {
            bool pumpPresent = HasExistingBiofluidPump();
            bool dependentBrainPresent = HasBiofluidDependentBrain();
            if (pumpPresent && dependentBrainPresent)
                return "Biofluid depleted: the biofluid pump and brain can no longer sustain neural function.";
            if (dependentBrainPresent)
                return "Biofluid depleted: the biofluid-dependent brain can no longer function.";
            return "Biofluid depleted: the body's biofluid pump can no longer sustain life.";
        }

        return null;
    }

    private bool HasExistingBiofluidPump()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.pumpsBiofluid && !subPart.IsDestroyed) return true;
        return false;
    }

    private bool HasBiofluidDependentBrain()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.category == SubPartCategory.Brain && subPart.requiresBiofluid && !subPart.IsDestroyed)
                    return true;
        return false;
    }

    private bool HasIncapacitatedBiofluidPump()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.pumpsBiofluid && !subPart.IsDestroyed
                    && subPart.currentHealth <= 0f) return true;
        return false;
    }

    private bool HasIncapacitatedBrain()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.category == SubPartCategory.Brain && !subPart.IsDestroyed
                    && subPart.currentHealth <= 0f) return true;
        return false;
    }

    private bool AreVitalsAboveRecoveryThreshold()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
            {
                bool isVital = subPart.pumpsBiofluid || subPart.category == SubPartCategory.Brain;
                if (isVital && !subPart.IsDestroyed
                    && subPart.currentHealth <= subPart.maxHealth * .05f) return false;
            }
        return true;
    }

    private bool HasDestroyedBiofluidPump()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.pumpsBiofluid && subPart.IsDestroyed) return true;
        return false;
    }

    private bool IsLimbDisabled(BodyPart part) =>
        _body.TryGetValue(part, out RuntimeBodyPart bodyPart) && bodyPart.IsDisabled;

    private bool HasBoneDestroyed(BodyPart part) =>
        IsSubPartDestroyed(part, SubPartCategory.Bone);

    private bool IsSubPartDepleted(BodyPart part, SubPartCategory category)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart bodyPart)) return false;
        foreach (RuntimeSubPart subPart in EnumerateSubParts(bodyPart))
            if (subPart.category == category && subPart.IsDepleted) return true;
        return false;
    }

    private bool IsSubPartDestroyed(BodyPart part, SubPartCategory category)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart bodyPart)) return false;

        foreach (RuntimeSubPart layer in bodyPart.layers)
            if (layer.category == category && layer.IsDestroyed) return true;
        foreach (RuntimeSubPart organ in bodyPart.organs)
            if (organ.category == category && organ.IsDestroyed) return true;
        return false;
    }

    private bool AreAllSubPartsDestroyed(BodyPart part)
    {
        if (!_body.TryGetValue(part, out RuntimeBodyPart bodyPart)) return false;

        int subPartCount = 0;
        foreach (RuntimeSubPart layer in bodyPart.layers)
        {
            subPartCount++;
            if (!layer.IsDestroyed) return false;
        }
        foreach (RuntimeSubPart organ in bodyPart.organs)
        {
            subPartCount++;
            if (!organ.IsDestroyed) return false;
        }
        return subPartCount > 0;
    }

    public void RegisterCollider(Collider col, BodyPart part) => _colliderMap[col] = part;

    public RuntimeBodyPart GetBodyPart(BodyPart part)
    {
        _body.TryGetValue(part, out var bp);
        return bp;
    }

    public IReadOnlyDictionary<BodyPart, RuntimeBodyPart> GetFullBody() => _body;
}

public class GameOverController : MonoBehaviour
{
    private bool _shown;
    public string DeathReason { get; private set; }

    public void ShowGameOver(string reason)
    {
        if (_shown) return;
        _shown = true;
        DeathReason = string.IsNullOrWhiteSpace(reason) ? "Cause of death undetermined." : reason;

        Player player = GetComponent<Player>() ?? GetComponentInParent<Player>();
        player?.SetGameOverInput(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;

        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var canvasObject = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        Image background = CreateImage("Background", canvasObject.transform, new Color(0f, 0f, 0f, 0.82f));
        Stretch(background.rectTransform);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(420f, 0f);
        panel.GetComponent<Image>().color = new Color(.055f, .055f, .07f, .98f);
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateLabel("GAME OVER", panel.transform, 42, Color.red);
        CreateLabel($"CAUSE OF DEATH\n{DeathReason}", panel.transform, 20, Color.white, 74f);
        CreateButton("Restart", panel.transform, Restart);
        CreateButton("Exit", panel.transform, Exit);
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        var image = obj.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateLabel(string text, Transform parent, int size, Color color, float minimumHeight = 0f)
    {
        var obj = new GameObject(text, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        obj.transform.SetParent(parent, false);
        var label = obj.GetComponent<Text>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        obj.GetComponent<LayoutElement>().minHeight = Mathf.Max(size + 12f, minimumHeight);
        return label;
    }

    private static void CreateButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
    {
        Image image = CreateImage(label, parent, new Color(0.15f, 0.15f, 0.18f, 1f));
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        image.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        Text text = CreateLabel(label, image.transform, 20, Color.white);
        Stretch(text.rectTransform);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void Exit()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
