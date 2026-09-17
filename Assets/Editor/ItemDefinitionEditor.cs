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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ammoType"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("clipSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("fireMode"));
            if (item.fireMode == FireMode.BurstFire)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("burstCount"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("roundsPerMinute"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("reloadTime"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletDrop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletLifetime"));
            EditorGUILayout.HelpBox("Lifetime: 0 = bullet never despawns.", MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aimFOV"));
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

    private void DrawBagFields()
    {
        EditorGUILayout.LabelField("Bag Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("bagSlotCapacity"));
        EditorGUILayout.HelpBox(
            "Adds bagSlotCapacity extra inventory slots while equipped.",
            MessageType.Info);
    }
}
