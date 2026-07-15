using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AugmentEntry))]
public class AugmentEntryDrawer : PropertyDrawer
{
    private static readonly Dictionary<string, bool> Foldouts = new();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!GetFoldout(property)) return EditorGUIUtility.singleLineHeight;

        const float gap = 2f;
        float height = EditorGUIUtility.singleLineHeight + gap;
        bool isSub = property.FindPropertyRelative("isSubPartAugment").boolValue;

        string[] alwaysShown =
        {
            "augmentID", "displayName", "description", "category", "targetBodyPart", "isSubPartAugment"
        };
        foreach (string name in alwaysShown)
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), true) + gap;

        if (isSub)
        {
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("targetSubPartCategory"), true) + gap;
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("subPartDefinition"), true) + gap;
        }
        else
        {
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("definition"), true) + gap;
        }

        height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("statOverrides"), true) + gap;
        return height + 2f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineHeight = EditorGUIUtility.singleLineHeight;
        const float gap = 2f;
        float y = position.y;

        bool expanded = EditorGUI.Foldout(new Rect(position.x, y, position.width, lineHeight),
            GetFoldout(property), label, true, EditorStyles.boldLabel);
        SetFoldout(property, expanded);
        y += lineHeight + gap;

        if (!expanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;

        DrawField(ref y, position, property, "augmentID", "Augment ID", gap);
        DrawField(ref y, position, property, "displayName", "Display Name", gap);
        DrawField(ref y, position, property, "description", "Description", gap);
        DrawField(ref y, position, property, "category", "Category", gap);
        DrawField(ref y, position, property, "targetBodyPart", "Target Body Part", gap);
        DrawField(ref y, position, property, "isSubPartAugment", "Is Sub-Part Augment", gap);

        if (property.FindPropertyRelative("isSubPartAugment").boolValue)
        {
            DrawField(ref y, position, property, "targetSubPartCategory", "Target Sub-Part Category", gap);
            DrawField(ref y, position, property, "subPartDefinition", "Sub-Part Definition", gap);
        }
        else
        {
            DrawField(ref y, position, property, "definition", "Body Part Definition", gap);
        }

        DrawField(ref y, position, property, "statOverrides", "Player Stat Overrides", gap);

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static void DrawField(ref float y, Rect position, SerializedProperty parent,
        string propertyName, string label, float gap)
    {
        SerializedProperty child = parent.FindPropertyRelative(propertyName);
        float height = EditorGUI.GetPropertyHeight(child, true);
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, height),
            child, new GUIContent(label), true);
        y += height + gap;
    }

    private static bool GetFoldout(SerializedProperty property) =>
        Foldouts.TryGetValue(property.propertyPath, out bool expanded) && expanded;

    private static void SetFoldout(SerializedProperty property, bool expanded) =>
        Foldouts[property.propertyPath] = expanded;
}
