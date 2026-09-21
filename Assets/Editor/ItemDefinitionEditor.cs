// ItemDefinitionEditor.cs
// Custom Inspector for ItemDefinition.
// Place in any folder named "Editor".

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ItemDefinition))]
public class ItemDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var item = (ItemDefinition)target;

        // ── Shared ────────────────────────────────────────────────────────────
        EditorGUILayout.LabelField(item.itemType == ItemType.Augment ? "Augment Identity" : "Identity", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemID"));
        if (item.itemType != ItemType.Augment)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The item ID must exactly match an AugmentCatalogue augment ID. " +
                "The Modification Station gets the augment's name and description from that catalogue entry.",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemType"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Durability", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxDurability"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("degradationRate"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Stacking", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("isStackable"));
        if (item.isStackable)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxStackSize"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("slotColor"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("World Representation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("worldPrefab"));

        // ── Type-specific ─────────────────────────────────────────────────────
        EditorGUILayout.Space(8);

        switch (item.itemType)
        {
            case ItemType.Weapon: DrawWeaponFields(item); break;
            case ItemType.Armour: DrawArmourFields(); break;
            case ItemType.Consumable: DrawConsumableFields(); break;
            case ItemType.Augment: DrawAugmentFields(); break;
            case ItemType.Bag: DrawBagFields(); break;
            case ItemType.Misc:
            case ItemType.Quest:
                EditorGUILayout.HelpBox(
                    $"{item.itemType} items have no additional fields.",
                    MessageType.None);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawWeaponFields(ItemDefinition item)
    {
        EditorGUILayout.LabelField("Weapon Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("handedness"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponDamage"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponDamageType"));

        if (item.weaponType == WeaponType.Ranged)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Ranged Settings", EditorStyles.boldLabel);
            DrawRangedProperty("ammoType", "Ammo Type",
                "Ammunition accepted by this weapon. It determines which inventory stacks can reload it and which projectile prefab is spawned.");
            DrawRangedProperty("magazineCapacity", "Magazine Capacity",
                "Rounds held inside the weapon when fully loaded. Reserve rounds in inventory are not included.");

            SerializedProperty action = serializedObject.FindProperty("actionType");
            EditorGUILayout.PropertyField(action, new GUIContent("Action Type",
                "The mechanism that prepares the next round. Self Loading supports conventional fire modes; bolt, pump, and lever actions cycle automatically after every shot."));
            WeaponActionType actionType = (WeaponActionType)action.enumValueIndex;
            EditorGUILayout.HelpBox(ActionTypeDescription(actionType), MessageType.None);

            if (actionType == WeaponActionType.SelfLoading)
            {
                SerializedProperty mode = serializedObject.FindProperty("fireMode");
                EditorGUILayout.PropertyField(mode, new GUIContent("Fire Mode",
                    "Semi Automatic fires once per press, Burst Fire fires a fixed group, and Automatic continues while held."));
                FireMode fireMode = (FireMode)mode.enumValueIndex;
                EditorGUILayout.HelpBox(FireModeDescription(fireMode), MessageType.None);
                if (fireMode == FireMode.BurstFire)
                    DrawRangedProperty("burstCount", "Burst Count",
                        "Number of rounds fired by each trigger press in Burst Fire mode.");
                if (fireMode == FireMode.SemiAutomatic)
                    DrawRangedProperty("semiAutomaticShotDelay", "Semi-Auto Shot Delay",
                        "Minimum seconds between accepted trigger presses. This represents trigger reset and mechanical delay rather than automatic cyclic rate.");
                else
                    DrawRangedProperty("cyclicRateRPM", "Cyclic Rate (RPM)",
                        "Mechanical cadence in rounds per minute. Used for automatic fire and spacing shots within a burst.");
            }
            else
            {
                DrawRangedProperty("actionCycleTime", "Action Cycle Time",
                    "Seconds spent automatically cycling the bolt, pump, or lever after every shot. A fresh trigger press is required after cycling finishes.");
            }

            SerializedProperty reloadStyle = serializedObject.FindProperty("reloadStyle");
            EditorGUILayout.PropertyField(reloadStyle, new GUIContent("Reload Style",
                "Detachable Magazine completes one full reload. Per Round inserts ammunition individually and may be interrupted by firing after a round is loaded."));
            ReloadStyle selectedReload = (ReloadStyle)reloadStyle.enumValueIndex;
            EditorGUILayout.HelpBox(ReloadStyleDescription(selectedReload), MessageType.None);
            if (selectedReload == ReloadStyle.DetachableMagazine)
                DrawRangedProperty("reloadTime", "Reload Time",
                    "Seconds until a detachable-magazine reload completes. Ammunition leaves inventory only at completion.");
            else
                DrawRangedProperty("perRoundReloadTime", "Per-Round Reload Time",
                    "Seconds required to insert each individual round. Each round leaves inventory when its insertion completes.");

            DrawRangedProperty("muzzleVelocityMetersPerSecond", "Muzzle Velocity (m/s)",
                "Projectile speed at the muzzle in metres per second, assuming one Unity unit equals one metre.");
            DrawRangedProperty("maximumProjectileRangeMeters", "Maximum Range (m)",
                "Distance travelled before the projectile despawns. Gravity comes from the project's global Physics gravity and is not configured per weapon.");
            DrawRangedProperty("accuracyMOA", "Accuracy (MOA)",
                "Base dispersion in minutes of angle. Lower is more accurate; 1 MOA is approximately a 29 mm group at 100 metres. Zero has no dispersion.");
            DrawRangedProperty("verticalRecoilDegrees", "Vertical Recoil (degrees)",
                "Upward camera kick added by every shot. This affects weapon feel but does not alter the projectile after it has spawned.");
            DrawRangedProperty("horizontalRecoilDegrees", "Horizontal Recoil (degrees)",
                "Maximum random camera kick to either side for every shot. Zero keeps recoil entirely vertical.");
            DrawRangedProperty("recoilRecoverySeconds", "Recoil Recovery (seconds)",
                "Approximate time for the recoil offset to settle back to the player's intended aim.");
            DrawRangedProperty("aimFOV", "Aim FOV",
                "Camera field of view while aiming down sights. Lower values appear more magnified.");
            EditorGUILayout.HelpBox(
                "Projectile drop uses global Physics.gravity. Damage remains authored separately because velocity alone does not account for projectile mass or construction.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Melee attacks apply Weapon Damage to the first combat damage receiver hit.",
                MessageType.Info);
        }
    }

    private void DrawArmourFields()
    {
        EditorGUILayout.LabelField("Armour Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyPart"));
        EditorGUILayout.HelpBox(
            "Set layerStats.category to SubPartCategory.Armour. " +
            "This layer is inserted at index 0 (outermost) in the damage cascade " +
            "for the target body part when equipped.",
            MessageType.Info);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("layerStats"), true);
    }

    private void DrawConsumableFields()
    {
        EditorGUILayout.LabelField("Consumable Treatment", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("consumableKind"));
        var item = (ItemDefinition)target;
        if (item.consumableKind == ConsumableKind.Ammo)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ammoType"));
            EditorGUILayout.HelpBox(
                "Ammo is consumed from inventory when a matching weapon finishes reloading. " +
                "Make ammo items stackable and assign the same Ammo Type used by the weapon.",
                MessageType.Info);
            return;
        }

        if (item.consumableKind == ConsumableKind.Edible)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Food", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("saturationRestore"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isIngredient"));
            EditorGUILayout.HelpBox(
                "Edibles are consumed immediately, restore saturation, and do not open the limb selector.",
                MessageType.Info);
        }
        else if (item.consumableKind is ConsumableKind.Healing or ConsumableKind.Biofluid)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("healthRestore"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("biofluidRestore"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("restoresMaxHealth"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("treatableDamageTypes"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("removableConditions"), true);
            EditorGUILayout.HelpBox(
                "Healing consumables query the HealthManager for valid body-part targets. " +
                "A blank damage-type or condition list means the item can treat any matching condition.",
                MessageType.Info);
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Timed Player Stat Modifiers", EditorStyles.boldLabel);
        SerializedProperty timedShared = serializedObject.FindProperty("timedEffectsShareTimer");
        EditorGUILayout.PropertyField(timedShared, new GUIContent("Share Timer"));
        if (timedShared.boolValue)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("effectDuration"),
                new GUIContent("Shared Duration"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("timedStatEffects"), true);
        EditorGUILayout.HelpBox(
            "These modifiers use the same player-stat list as augments. Disable Share Timer to set a duration on each entry. " +
            "Using the same consumable again refreshes its timer instead of stacking it.",
            MessageType.None);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Lifecycle Effects", EditorStyles.boldLabel);
        SerializedProperty lifecycleShared = serializedObject.FindProperty("lifecycleEffectsShareTimer");
        EditorGUILayout.PropertyField(lifecycleShared, new GUIContent("Share Timer"));
        if (lifecycleShared.boolValue)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lifecycleEffectDuration"),
                new GUIContent("Shared Duration"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("lifecycleEffects"), true);
        EditorGUILayout.HelpBox(
            "After Use fires after successful consumption. After Timed Effect Expires fires once the last timed modifier ends. " +
            "Use Changes to expose only the relevant value and targeting controls.",
            MessageType.None);
    }

    private void DrawAugmentFields()
    {
        EditorGUILayout.LabelField("Augment Data", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
                    "No separate augment ID is needed. The Modification Station resolves this item's Item ID " +
                    "against its Augment Catalogue. Augment items remain in the player inventory for now.",
                    MessageType.Info);
    }

    private void DrawRangedProperty(string propertyName, string label, string tooltip)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName),
            new GUIContent(label, tooltip));
    }

    private static string ActionTypeDescription(WeaponActionType actionType) => actionType switch
    {
        WeaponActionType.SelfLoading => "Self Loading: the weapon prepares the next round automatically and may use semi, burst, or automatic fire.",
        WeaponActionType.BoltAction => "Bolt Action: a bolt cycle starts automatically after each shot; firing is locked until it completes.",
        WeaponActionType.PumpAction => "Pump Action: a pump cycle starts automatically after each shot; firing is locked until it completes.",
        WeaponActionType.LeverAction => "Lever Action: a lever cycle starts automatically after each shot; firing is locked until it completes.",
        _ => string.Empty
    };

    private static string FireModeDescription(FireMode fireMode) => fireMode switch
    {
        FireMode.SemiAutomatic => "Semi Automatic: one shot per fresh trigger press, limited by Semi-Auto Shot Delay.",
        FireMode.BurstFire => "Burst Fire: one trigger press fires Burst Count rounds at the configured Cyclic Rate.",
        FireMode.Automatic => "Automatic: continues firing while the trigger is held at the configured Cyclic Rate.",
        _ => string.Empty
    };

    private static string ReloadStyleDescription(ReloadStyle reloadStyle) => reloadStyle switch
    {
        ReloadStyle.DetachableMagazine => "Detachable Magazine: tops up all needed rounds when the single reload timer finishes.",
        ReloadStyle.PerRound => "Per Round: inserts one round per timer and can be interrupted by firing once at least one round is available.",
        _ => string.Empty
    };

    private void DrawBagFields()
    {
        EditorGUILayout.LabelField("Bag Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("bagSlotCapacity"));
        EditorGUILayout.HelpBox(
            "Adds bagSlotCapacity extra inventory slots while equipped.",
            MessageType.Info);
    }
}
