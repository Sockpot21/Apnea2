// HealthManager.cs
// DEBUG VERSION: Consolidated combat logging into single report per hit.
// Supports armour layer insertion/removal via PlayerEquipment.
// Auto-notifies PlayerEquipment when an armour layer is destroyed.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
    public bool requiresBiofluid;
    public float biofluidRequirement;
    public bool producesBiofluid;
    public float biofluidProductionRate;
    public bool pumpsBiofluid;
    public float biofluidPumpEfficiency;
    public float healingRate;

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
            breachThreshold = def.breachThreshold,
            requiresBiofluid = def.requiresBiofluid,
            biofluidRequirement = def.biofluidRequirement,
            producesBiofluid = def.producesBiofluid,
            biofluidProductionRate = def.biofluidProductionRate,
            pumpsBiofluid = def.pumpsBiofluid,
            biofluidPumpEfficiency = def.biofluidPumpEfficiency,
            healingRate = def.healingRate
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
    [SerializeField] private PlayerCharacter playerCharacter;
    [SerializeField] private GameOverController gameOverController;

    private Dictionary<BodyPart, RuntimeBodyPart> _body = new();
    private Dictionary<Collider, BodyPart> _colliderMap = new();
    private readonly List<InstalledAugmentEffect> _installedAugmentEffects = new();
    private float _currentBiofluid;

    public float CurrentBiofluid => _currentBiofluid;
    public float TotalBiofluidRequired => CalculateBiofluidRequirement();

    private class InstalledAugmentEffect
    {
        public AugmentEntry entry;
        public RuntimeBodyPart bodyPart;
        public RuntimeSubPart subPart;

        public bool IsFunctional => subPart != null
            ? !subPart.IsDestroyed
            : bodyPart != null && bodyPart.ConditionRatio > 0f;
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

    private void Update() => UpdateBiofluid(Time.deltaTime);

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

        // Write armour layer HP back to its definition and auto-unequip if destroyed
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

        // Find the hit record for the armour layer (it's always the first layer hit if present)
        foreach (var hit in result.layerHits)
        {
            if (hit.displayName == equipped.definition.layerStats.displayName)
            {
                if (hit.destroyed)
                {
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
        RuntimeBodyPart runtimePart = RuntimeBodyPart.FromDefinition(entry.definition);
        _body[entry.targetBodyPart] = runtimePart;
        _installedAugmentEffects.RemoveAll(effect => effect.entry.targetBodyPart == entry.targetBodyPart);
        _installedAugmentEffects.Add(new InstalledAugmentEffect { entry = entry, bodyPart = runtimePart });
        RefreshAugmentStatOverrides();
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
                TrackSubPartAugmentEffect(entry, bodyPart, newRuntime);
                RefreshAugmentStatOverrides();
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }
        for (int i = 0; i < bodyPart.organs.Count; i++)
        {
            if (bodyPart.organs[i].category == entry.targetSubPartCategory)
            {
                bodyPart.organs[i] = newRuntime;
                TrackSubPartAugmentEffect(entry, bodyPart, newRuntime);
                RefreshAugmentStatOverrides();
                playerHealth?.OnBodyInitialised(_body);
                return;
            }
        }

        Debug.LogWarning($"[HealthManager] No matching sub-part '{entry.targetSubPartCategory}' in {entry.targetBodyPart}.");
    }

    private void TrackSubPartAugmentEffect(AugmentEntry entry, RuntimeBodyPart bodyPart, RuntimeSubPart subPart)
    {
        _installedAugmentEffects.RemoveAll(effect => effect.entry.targetBodyPart == entry.targetBodyPart
            && effect.entry.isSubPartAugment
            && effect.entry.targetSubPartCategory == entry.targetSubPartCategory);
        _installedAugmentEffects.Add(new InstalledAugmentEffect
        {
            entry = entry,
            bodyPart = bodyPart,
            subPart = subPart
        });
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
                if (subPart.IsDestroyed || subPart.healingRate <= 0f
                    || subPart.currentHealth >= subPart.maxHealth)
                    continue;

                float healing = Mathf.Min(subPart.maxHealth - subPart.currentHealth,
                    subPart.healingRate * pumpEfficiency * deltaTime);
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

    private float CalculateBiofluidRequirement()
    {
        float total = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (!subPart.IsDestroyed && subPart.requiresBiofluid)
                    total += Mathf.Max(0f, subPart.biofluidRequirement);
        return total;
    }

    private float CalculateBiofluidProduction()
    {
        float production = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (!subPart.IsDestroyed && subPart.producesBiofluid)
                    production += Mathf.Max(0f, subPart.biofluidProductionRate);
        return production;
    }

    private float CalculatePumpEfficiency()
    {
        float efficiency = 0f;
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (!subPart.IsDestroyed && subPart.pumpsBiofluid)
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
        if (HasDestroyedBiofluidPump()
            || IsSubPartDestroyed(BodyPart.Head, SubPartCategory.Brain)
            || HasZeroCondition(BodyPart.Head))
        {
            if (gameOverController == null) gameOverController = GetComponent<GameOverController>();
            if (gameOverController == null) gameOverController = gameObject.AddComponent<GameOverController>();
            gameOverController.ShowGameOver();
            return;
        }

        bool legOrSpineFailed = HasZeroCondition(BodyPart.LeftThigh)
            || HasZeroCondition(BodyPart.LeftShin)
            || HasZeroCondition(BodyPart.RightThigh)
            || HasZeroCondition(BodyPart.RightShin)
            // The body model has no Spine enum, so torso bone layers represent it.
            || HasBoneDestroyed(BodyPart.Chest)
            || HasBoneDestroyed(BodyPart.Abdomen);
        playerCharacter?.SetForcedCrawl(legOrSpineFailed);

        if (HasArmFailure(BodyPart.LeftUpperArm) || HasArmFailure(BodyPart.LeftForearm))
            playerEquipment?.DisableHandAndDrop(HandSlot.Left);
        if (HasArmFailure(BodyPart.RightUpperArm) || HasArmFailure(BodyPart.RightForearm))
            playerEquipment?.DisableHandAndDrop(HandSlot.Right);
    }

    private bool HasArmFailure(BodyPart part) => HasZeroCondition(part);

    private bool HasDestroyedBiofluidPump()
    {
        foreach (var pair in _body)
            foreach (RuntimeSubPart subPart in EnumerateSubParts(pair.Value))
                if (subPart.pumpsBiofluid && subPart.IsDestroyed) return true;
        return false;
    }

    private bool HasZeroCondition(BodyPart part) =>
        _body.TryGetValue(part, out RuntimeBodyPart bodyPart) && bodyPart.ConditionRatio <= 0f;

    private bool HasBoneDestroyed(BodyPart part) =>
        IsSubPartDestroyed(part, SubPartCategory.Bone);

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

    public void ShowGameOver()
    {
        if (_shown) return;
        _shown = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        var canvasObject = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        Image background = CreateImage("Background", canvasObject.transform, new Color(0f, 0f, 0f, 0.82f));
        Stretch(background.rectTransform);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(420f, 0f);
        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateLabel("GAME OVER", panel.transform, 42, Color.red);
        CreateLabel("Neural function lost.", panel.transform, 20, Color.white);
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

    private static void CreateLabel(string text, Transform parent, int size, Color color)
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
        obj.GetComponent<LayoutElement>().minHeight = size + 12f;
    }

    private static void CreateButton(string label, Transform parent, UnityEngine.Events.UnityAction action)
    {
        Image image = CreateImage(label, parent, new Color(0.15f, 0.15f, 0.18f, 1f));
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        image.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
        CreateLabel(label, image.transform, 20, Color.white);
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
