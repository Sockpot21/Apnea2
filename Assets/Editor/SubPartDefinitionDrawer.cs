using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SubPartDefinition))]
public class SubPartDefinitionDrawer : PropertyDrawer
{
    private static readonly Dictionary<string, bool> Foldouts = new();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!GetFoldout(property)) return EditorGUIUtility.singleLineHeight;

        const float gap = 2f;
        float height = EditorGUIUtility.singleLineHeight + gap;
        string[] alwaysShown = { "subPartID", "displayName", "category", "isOrgan", "maxHealth" };
        foreach (string name in alwaysShown)
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(name), true) + gap;

        bool isOrgan = property.FindPropertyRelative("isOrgan").boolValue;
        if (isOrgan)
        {
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("organHitChance"), true) + gap;
        }
        else
        {
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("breachThreshold"), true) + gap;
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("resistances"), true) + gap;
        }

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
        DrawField(ref y, position, property, "subPartID", "Sub Part ID", gap);
        DrawField(ref y, position, property, "displayName", "Display Name", gap);
        DrawField(ref y, position, property, "category", "Category", gap);
        DrawField(ref y, position, property, "isOrgan", "Is Organ", gap);
        DrawField(ref y, position, property, "maxHealth", "Max Health", gap);

        if (property.FindPropertyRelative("isOrgan").boolValue)
            DrawField(ref y, position, property, "organHitChance", "Organ Hit Chance", gap);
        else
        {
            DrawField(ref y, position, property, "breachThreshold", "Breach Threshold", gap);
            DrawField(ref y, position, property, "resistances", "Resistances", gap);
        }

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
