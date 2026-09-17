using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AugmentStatOverride))]
public class AugmentStatOverrideDrawer : PropertyDrawer
{
    private const float Gap = 2f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        bool isToggle = IsToggle((PlayerAugmentStat)property.FindPropertyRelative("stat").enumValueIndex);
        int lines = (isToggle ? 2 : 3) + (ShowDuration(property) ? 1 : 0);
        return lines * EditorGUIUtility.singleLineHeight + (lines - 1) * Gap;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        float line = EditorGUIUtility.singleLineHeight;
        Rect row = new(position.x, position.y, position.width, line);

        SerializedProperty stat = property.FindPropertyRelative("stat");
        EditorGUI.PropertyField(row, stat, label);
        row.y += line + Gap;

        if (IsToggle((PlayerAugmentStat)stat.enumValueIndex))
        {
            EditorGUI.PropertyField(row, property.FindPropertyRelative("boolValue"),
                new GUIContent("Override Value"));
        }
        else
        {
            SerializedProperty mode = property.FindPropertyRelative("mode");
            EditorGUI.PropertyField(row, mode, new GUIContent("Mode"));
            row.y += line + Gap;
            string valueLabel = (PlayerStatModifierMode)mode.enumValueIndex == PlayerStatModifierMode.Override
                ? "Override Value"
                : "Percentage";
            EditorGUI.PropertyField(row, property.FindPropertyRelative("value"),
                new GUIContent(valueLabel));
        }

        if (ShowDuration(property))
        {
            row.y += line + Gap;
            EditorGUI.PropertyField(row, property.FindPropertyRelative("duration"),
                new GUIContent("Duration"));
        }

        EditorGUI.EndProperty();
    }

    private static bool IsToggle(PlayerAugmentStat stat) =>
        stat is PlayerAugmentStat.SprintEnabled
            or PlayerAugmentStat.DoubleJumpEnabled
            or PlayerAugmentStat.WallRunEnabled;

    private static bool ShowDuration(SerializedProperty property)
    {
        if (property.propertyPath.Contains("timedStatEffects"))
        {
            SerializedProperty shared = property.serializedObject.FindProperty("timedEffectsShareTimer");
            return shared != null && !shared.boolValue;
        }
        if (property.propertyPath.Contains("lifecycleEffects"))
        {
            SerializedProperty shared = property.serializedObject.FindProperty("lifecycleEffectsShareTimer");
            return shared != null && !shared.boolValue;
        }
        return false;
    }
}
