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
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Classification", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("itemType"));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Physical", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("weight"));

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

        if (item.weaponType == WeaponType.Ranged)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Ranged Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletPrefab"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletSpeed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletDrop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bulletLifetime"));
            EditorGUILayout.HelpBox("Lifetime: 0 = bullet never despawns.", MessageType.None);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("aimFOV"));
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Melee weapon — no additional fields yet. " +
                "Attack logic will be added when the melee system is built out.",
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
        EditorGUILayout.HelpBox(
            "Consumable — no additional fields yet. " +
            "Effect data will be added when the consumable system is built out.",
            MessageType.Info);
    }
}
