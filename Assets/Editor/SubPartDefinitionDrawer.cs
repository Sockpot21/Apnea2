// SubPartDefinitionDrawer.cs
// Custom property drawer for SubPartDefinition.
// Hides organ-only fields when isOrgan = false,
// and hides structural-only fields when isOrgan = true.
// Place in any folder named "Editor".

using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SubPartDefinition))]
public class SubPartDefinitionDrawer : PropertyDrawer
{
    // Foldout state per property path
    private static System.Collections.Generic.Dictionary<string, bool> _foldouts = new();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!GetFoldout(property)) return EditorGUIUtility.singleLineHeight;

        bool isOrgan = property.FindPropertyRelative("isOrgan").boolValue;
        int lines = 5; // subPartID, displayName, category, isOrgan, maxHealth

        if (isOrgan)
            lines += 1;  // organHitChance
        else
            lines += 2;  // breachThreshold + resistances list header

        // Resistances list (only shown for non-organs)
        if (!isOrgan)
        {
            var resistances = property.FindPropertyRelative("resistances");
            lines += 1; // list size field
            if (resistances.isExpanded)
                lines += resistances.arraySize * 2 + 1;
        }

        return lines * (EditorGUIUtility.singleLineHeight + 2f) + 4f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineH  = EditorGUIUtility.singleLineHeight;
        float gap    = 2f;
        float y      = position.y;

        // Foldout header
        bool expanded = GetFoldout(property);
        var foldoutRect = new Rect(position.x, y, position.width, lineH);
        expanded = EditorGUI.Foldout(foldoutRect, expanded, label, true, EditorStyles.boldLabel);
        SetFoldout(property, expanded);
        y += lineH + gap;

        if (!expanded) { EditorGUI.EndProperty(); return; }

        EditorGUI.indentLevel++;

        bool isOrgan = property.FindPropertyRelative("isOrgan").boolValue;

        // Always-shown fields
        DrawField(ref y, position, property, "subPartID",   "Sub Part ID",   lineH, gap);
        DrawField(ref y, position, property, "displayName", "Display Name",  lineH, gap);
        DrawField(ref y, position, property, "category",    "Category",      lineH, gap);
        DrawField(ref y, position, property, "isOrgan",     "Is Organ",      lineH, gap);
        DrawField(ref y, position, property, "maxHealth",   "Max Health",    lineH, gap);

        if (isOrgan)
        {
            // Organ-only
            DrawField(ref y, position, property, "organHitChance", "Organ Hit Chance", lineH, gap);
        }
        else
        {
            // Structural-only
            DrawField(ref y, position, property, "breachThreshold", "Breach Threshold", lineH, gap);

            // Resistances list
            var resistances = property.FindPropertyRelative("resistances");
            float listHeight = EditorGUI.GetPropertyHeight(resistances, true);
            var listRect = new Rect(position.x, y, position.width, listHeight);
            EditorGUI.PropertyField(listRect, resistances, new GUIContent("Resistances"), true);
            y += listHeight + gap;
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static void DrawField(ref float y, Rect pos, SerializedProperty parent,
        string propName, string label, float lineH, float gap)
    {
        var prop = parent.FindPropertyRelative(propName);
        var rect = new Rect(pos.x, y, pos.width, lineH);
        EditorGUI.PropertyField(rect, prop, new GUIContent(label));
        y += lineH + gap;
    }

    private static bool GetFoldout(SerializedProperty p)
    {
        if (!_foldouts.TryGetValue(p.propertyPath, out bool v)) v = false;
        return v;
    }

    private static void SetFoldout(SerializedProperty p, bool v) =>
        _foldouts[p.propertyPath] = v;
}
