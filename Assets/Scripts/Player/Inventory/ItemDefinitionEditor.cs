// ItemDefinitionEditor.cs
// Custom Inspector for ItemDefinition.
// Place this file in any folder named "Editor" in your project.
// Only draws fields relevant to the selected itemType.

using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ItemDefinition))]
public class ItemDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var item = (ItemDefinition)target;

        // ── Shared fields ─────────────────────────────────────────────────────
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

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("World Representation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("worldPrefab"));

        // ── Type-specific fields ──────────────────────────────────────────────
        EditorGUILayout.Space(8);

        switch (item.itemType)
        {
            case ItemType.Weapon:
                DrawWeaponFields();
                break;

            case ItemType.Armour:
                DrawArmourFields();
                break;

            case ItemType.Consumable:
                DrawConsumableFields();
                break;

            case ItemType.Misc:
            case ItemType.Quest:
                EditorGUILayout.HelpBox(
                    $"{item.itemType} items have no additional fields.",
                    MessageType.None);
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawWeaponFields()
    {
        EditorGUILayout.LabelField("Weapon Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponType"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("handedness"));
    }

    private void DrawArmourFields()
    {
        EditorGUILayout.LabelField("Armour Data", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBodyPart"));
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
