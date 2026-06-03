// HealthManager.cs
// DEBUG VERSION: Consolidated combat logging into single report per hit.
// Supports armour layer insertion/removal via PlayerEquipment.
// Auto-notifies PlayerEquipment when an armour layer is destroyed.

using UnityEngine;
using System.Collections.Generic;
using System.Text;

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

    public Dictionary<DamageType, float> resistanceMap = new Dictionary<DamageType, float>();

    public bool IsDestroyed => currentHealth <= 0f;

    public float GetMultiplier(DamageType type)
    {
        return resistanceMap.TryGetValue(type, out float m) ? m : 1f;
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
            maxHealth = def.maxHealth,
            currentHealth = def.maxHealth,
            breachThreshold = def.breachThreshold
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

public class DamageResult
{
    public BodyPart bodyPart;
    public List<LayerHitRecord> layerHits = new List<LayerHitRecord>();
    public List<OrganHitRecord> organHits = new List<OrganHitRecord>();
    public float conditionAfter;
}

public class LayerHitRecord
{
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

    private Dictionary<BodyPart, RuntimeBodyPart> _body = new();
    private Dictionary<Collider, BodyPart> _colliderMap = new();

    private void Awake()
    {
        InitialiseBody();
        BuildColliderMap();
    }

    private void Start()
    {
        playerHealth?.OnBodyInitialised(_body);
    }

    private void InitialiseBody()
    {
        _body.Clear();
        foreach (var def in augmentDb.bodyParts)
            _body[def.bodyPart] = RuntimeBodyPart.FromDefinition(def);
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

        // Write armour layer HP back to its definition and auto-unequip if destroyed
        CheckArmourDestruction(part, result, log);

        playerHealth?.OnDamageReceived(result);
    }

    // ── Armour destruction check ──────────────────────────────────────────────

    private void CheckArmourDestruction(BodyPart part, DamageResult result, StringBuilder log)
    {
        if (playerEquipment == null) return;

        var equipped = playerEquipment.GetArmourSlot(part);
        if (equipped == null || !equipped.IsArmour) return;

        // Find the hit record for the armour layer (it's always the first layer hit if present)
        foreach (var hit in result.layerHits)
        {
            if (hit.displayName == equipped.layerStats.displayName)
            {
                if (hit.destroyed)
                {
                    log.AppendLine($"\n[ARMOUR] '{equipped.displayName}' destroyed — auto-unequipping.");
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
        log.AppendLine($"\n[CASCADE] {bodyPart.displayName} | {type}");
        log.AppendLine($"Incoming: {incoming}");

        bool innermostLayerBreached = false;
        float spillover = incoming;
        int lastLayerIndex = bodyPart.layers.Count - 1;

        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            var layer = bodyPart.layers[i];
            bool isLast = (i == lastLayerIndex);

            log.AppendLine($"\n-- LAYER {i}: {layer.displayName} --");

            if (layer.IsDestroyed)
            {
                log.AppendLine("Already destroyed — damage passes through.");
                if (isLast) innermostLayerBreached = true;
                continue;
            }

            float hpBefore = layer.currentHealth;
            float mult = layer.GetMultiplier(type);
            float totalDamage = spillover * mult;

            log.AppendLine($"HP: {hpBefore}/{layer.maxHealth}");
            log.AppendLine($"Multiplier: {mult}");
            log.AppendLine($"Damage: {spillover} × {mult} = {totalDamage}");

            bool breached = totalDamage > layer.breachThreshold;
            float hpDamage;

            if (breached && !isLast)
            {
                hpDamage = spillover;
                spillover = totalDamage;
                log.AppendLine("BREACH → spillover continues");
            }
            else
            {
                hpDamage = totalDamage;
                spillover = 0f;

                if (breached && isLast)
                {
                    innermostLayerBreached = true;
                    log.AppendLine("BREACH of innermost layer → organs at risk");
                }
                else
                {
                    log.AppendLine("Stopped cascade — organs safe");
                }
            }

            layer.currentHealth = Mathf.Max(0f, layer.currentHealth - hpDamage);
            log.AppendLine($"HP After: {layer.currentHealth}");

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

            if (!breached) break;
        }

        if (innermostLayerBreached && bodyPart.organs.Count > 0)
            ProcessOrganRolls(bodyPart, type, spillover > 0f ? spillover : incoming, result, log);
        else if (bodyPart.organs.Count > 0)
            log.AppendLine("\n[ORGANS] Skipped — innermost layer held.");
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
            if (organ.IsDestroyed) continue;

            float roll = Random.value;
            log.AppendLine($"\n{organ.displayName}");
            log.AppendLine($"Roll: {roll} vs {organ.organHitChance}");

            if (roll > organ.organHitChance) { log.AppendLine("Miss"); continue; }

            float mult = organ.GetMultiplier(type);
            float damage = incoming * mult;
            organ.currentHealth = Mathf.Max(0f, organ.currentHealth - damage);

            log.AppendLine($"HIT → {incoming} × {mult} = {damage}");
            log.AppendLine($"HP After: {organ.currentHealth}");

            result.organHits.Add(new OrganHitRecord
            {
                displayName = organ.displayName,
                incomingDamage = incoming,
                totalDamage = damage,
                hpDamageDealt = damage,
                healthAfter = organ.currentHealth,
                destroyed = organ.IsDestroyed
            });
        }
    }

    // ── Augment install / swap ────────────────────────────────────────────────

    public void InstallFullAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        if (entry == null || entry.isSubPartAugment) return;
        _body[entry.targetBodyPart] = RuntimeBodyPart.FromDefinition(entry.definition);
        Debug.Log($"[HealthManager] Full augment installed: {entry.displayName} → {entry.targetBodyPart}");
        playerHealth?.OnBodyInitialised(_body);
    }

    public void InstallSubPartAugment(string augmentID)
    {
        var entry = augmentCatalogue?.GetAugment(augmentID);
        if (entry == null || !entry.isSubPartAugment) return;

        if (!_body.TryGetValue(entry.targetBodyPart, out RuntimeBodyPart bodyPart))
        {
            Debug.LogWarning($"[HealthManager] Body part {entry.targetBodyPart} not found.");
            return;
        }

        var newRuntime = RuntimeSubPart.FromDefinition(entry.subPartDefinition);

        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            if (bodyPart.layers[i].category == entry.targetSubPartCategory)
            {
                bodyPart.layers[i] = newRuntime;
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }
        for (int i = 0; i < bodyPart.organs.Count; i++)
        {
            if (bodyPart.organs[i].category == entry.targetSubPartCategory)
            {
                bodyPart.organs[i] = newRuntime;
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }

        Debug.LogWarning($"[HealthManager] No matching sub-part '{entry.targetSubPartCategory}' in {entry.targetBodyPart}.");
    }

    public void RegisterCollider(Collider col, BodyPart part) => _colliderMap[col] = part;

    public RuntimeBodyPart GetBodyPart(BodyPart part)
    {
        _body.TryGetValue(part, out var bp);
        return bp;
    }

    public IReadOnlyDictionary<BodyPart, RuntimeBodyPart> GetFullBody() => _body;
}
