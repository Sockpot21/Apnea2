// AugmentEntryDrawer.cs
// Custom property drawer for AugmentEntry.
// Shows only full-body-part fields when isSubPartAugment = false,
// and only sub-part fields when isSubPartAugment = true.
// Place in any folder named "Editor".

using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AugmentEntry))]
public class AugmentEntryDrawer : PropertyDrawer
{
    private static System.Collections.Generic.Dictionary<string, bool> _foldouts = new();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!GetFoldout(property)) return EditorGUIUtility.singleLineHeight;

        float lineH = EditorGUIUtility.singleLineHeight + 2f;
        bool isSub = property.FindPropertyRelative("isSubPartAugment").boolValue;

        // Always: augmentID, displayName, description, category, targetBodyPart, isSubPartAugment
        float height = lineH * 6f;

        if (isSub)
        {
            // targetSubPartCategory + subPartDefinition (uses SubPartDefinitionDrawer)
            height += lineH;
            var subDef = property.FindPropertyRelative("subPartDefinition");
            height += EditorGUI.GetPropertyHeight(subDef, true) + 2f;
        }
        else
        {
            // definition (BodyPartDefinition — default drawer)
            var def = property.FindPropertyRelative("definition");
            height += EditorGUI.GetPropertyHeight(def, true) + 2f;
        }

        return height + 4f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineH = EditorGUIUtility.singleLineHeight;
        float gap = 2f;
        float y = position.y;

        // Foldout header
        bool expanded = GetFoldout(property);
        expanded = EditorGUI.Foldout(
            new Rect(position.x, y, position.width, lineH),
            expanded, label, true, EditorStyles.boldLabel);
        SetFoldout(property, expanded);
        y += lineH + gap;

        if (!expanded) { EditorGUI.EndProperty(); return; }

        EditorGUI.indentLevel++;

        bool isSub = property.FindPropertyRelative("isSubPartAugment").boolValue;

        // Always-shown
        DrawField(ref y, position, property, "augmentID", "Augment ID", lineH, gap);
        DrawField(ref y, position, property, "displayName", "Display Name", lineH, gap);
        DrawField(ref y, position, property, "description", "Description", lineH, gap);
        DrawField(ref y, position, property, "category", "Category", lineH, gap);
        DrawField(ref y, position, property, "targetBodyPart", "Target Body Part", lineH, gap);
        DrawField(ref y, position, property, "isSubPartAugment", "Is Sub-Part Augment", lineH, gap);

        if (isSub)
        {
            // Sub-part replacement fields
            DrawField(ref y, position, property, "targetSubPartCategory",
                "Target Sub-Part Category", lineH, gap);

            var subDef = property.FindPropertyRelative("subPartDefinition");
            float defHeight = EditorGUI.GetPropertyHeight(subDef, true);
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, defHeight),
                subDef, new GUIContent("Sub-Part Definition"), true);
            y += defHeight + gap;
        }
        else
        {
            // Full body part replacement
            var def = property.FindPropertyRelative("definition");
            float defHeight = EditorGUI.GetPropertyHeight(def, true);
            EditorGUI.PropertyField(
                new Rect(position.x, y, position.width, defHeight),
                def, new GUIContent("Body Part Definition"), true);
            y += defHeight + gap;
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static void DrawField(ref float y, Rect pos, SerializedProperty parent,
        string propName, string label, float lineH, float gap)
    {
        var prop = parent.FindPropertyRelative(propName);
        float h = EditorGUI.GetPropertyHeight(prop, true);
        EditorGUI.PropertyField(new Rect(pos.x, y, pos.width, h), prop, new GUIContent(label), true);
        y += h + gap;
    }

    private static bool GetFoldout(SerializedProperty p)
    {
        if (!_foldouts.TryGetValue(p.propertyPath, out bool v)) v = false;
        return v;
    }
    private static void SetFoldout(SerializedProperty p, bool v) =>
        _foldouts[p.propertyPath] = v;
}
