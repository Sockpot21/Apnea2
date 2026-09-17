using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ItemStackDefinition))]
public class ItemStackDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty itemProperty = serializedObject.FindProperty("item");
        SerializedProperty sizeProperty = serializedObject.FindProperty("stackSize");
        SerializedProperty prefabProperty = serializedObject.FindProperty("worldPrefab");

        var definitions = new List<ItemDefinition>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition"))
        {
            ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null && definition.isStackable) definitions.Add(definition);
        }
        definitions.Sort((left, right) => string.Compare(left.displayName, right.displayName,
            System.StringComparison.OrdinalIgnoreCase));

        var labels = new string[definitions.Count + 1];
        labels[0] = "<Select stackable item>";
        int selected = 0;
        for (int i = 0; i < definitions.Count; i++)
        {
            labels[i + 1] = string.IsNullOrWhiteSpace(definitions[i].displayName)
                ? definitions[i].name
                : definitions[i].displayName;
            if (itemProperty.objectReferenceValue == definitions[i]) selected = i + 1;
        }

        EditorGUILayout.LabelField("Stack Contents", EditorStyles.boldLabel);
        int next = EditorGUILayout.Popup("Stackable Item", selected, labels);
        itemProperty.objectReferenceValue = next > 0 ? definitions[next - 1] : null;

        ItemDefinition chosen = itemProperty.objectReferenceValue as ItemDefinition;
        int maximum = chosen != null ? Mathf.Max(1, chosen.maxStackSize) : 1;
        sizeProperty.intValue = EditorGUILayout.IntSlider("Stack Size", Mathf.Clamp(sizeProperty.intValue, 1, maximum), 1, maximum);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("World Representation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(prefabProperty, new GUIContent("Pickup Prefab"));

        if (definitions.Count == 0)
            EditorGUILayout.HelpBox("No stackable ItemDefinition assets exist yet.", MessageType.Warning);
        else if (chosen == null)
            EditorGUILayout.HelpBox("Choose the underlying item this pickup stack should grant.", MessageType.Info);
        else
            EditorGUILayout.HelpBox($"Picking this up grants {sizeProperty.intValue} × {chosen.displayName}.", MessageType.None);

        serializedObject.ApplyModifiedProperties();
    }
}
