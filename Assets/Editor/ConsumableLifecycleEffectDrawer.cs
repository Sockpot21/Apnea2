using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ConsumableLifecycleEffect))]
public class ConsumableLifecycleEffectDrawer : PropertyDrawer
{
    private const float Gap = 2f;
    private static readonly SubPartCategory[] OrganCategories =
    {
        SubPartCategory.Heart, SubPartCategory.Lung, SubPartCategory.Stomach,
        SubPartCategory.Intestines, SubPartCategory.Eye, SubPartCategory.Brain
    };
    private static readonly string[] OrganNames =
    {
        "Heart", "Lung", "Stomach", "Intestines", "Eye", "Brain"
    };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return line;

        float height = line * 3f + Gap * 2f; // foldout, timing, changes
        ConsumableLifecycleChange change = (ConsumableLifecycleChange)
            property.FindPropertyRelative("changes").enumValueIndex;
        if (change == ConsumableLifecycleChange.PlayerStat)
        {
            height += EditorGUI.GetPropertyHeight(
                property.FindPropertyRelative("playerStatModifier"), true) + Gap;
            return height;
        }

        height += line + Gap; // amount
        if (change is not (ConsumableLifecycleChange.Health or ConsumableLifecycleChange.MaximumHealth))
            return height;

        height += line + Gap; // target
        ConsumableHealthTarget target = (ConsumableHealthTarget)
            property.FindPropertyRelative("healthTarget").enumValueIndex;
        if (target == ConsumableHealthTarget.FixedBodyPart) height += line + Gap;
        else if (target == ConsumableHealthTarget.SpecificOrgan) height += (line + Gap) * 2f;
        if (change == ConsumableLifecycleChange.Health
            && property.FindPropertyRelative("amount").floatValue < 0f)
            height += line + Gap;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        float line = EditorGUIUtility.singleLineHeight;
        Rect row = new(position.x, position.y, position.width, line);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        row.y += line + Gap;
        EditorGUI.PropertyField(row, property.FindPropertyRelative("timing"));
        row.y += line + Gap;
        SerializedProperty changes = property.FindPropertyRelative("changes");
        EditorGUI.PropertyField(row, changes, new GUIContent("Changes"));
        row.y += line + Gap;

        ConsumableLifecycleChange change = (ConsumableLifecycleChange)changes.enumValueIndex;
        if (change == ConsumableLifecycleChange.PlayerStat)
        {
            SerializedProperty modifier = property.FindPropertyRelative("playerStatModifier");
            float modifierHeight = EditorGUI.GetPropertyHeight(modifier, true);
            EditorGUI.PropertyField(new Rect(row.x, row.y, row.width, modifierHeight),
                modifier, new GUIContent("Player Stat"), true);
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.PropertyField(row, property.FindPropertyRelative("amount"),
            new GUIContent(ChangeAmountLabel(change)));
        row.y += line + Gap;

        if (change is ConsumableLifecycleChange.Health or ConsumableLifecycleChange.MaximumHealth)
        {
            SerializedProperty target = property.FindPropertyRelative("healthTarget");
            EditorGUI.PropertyField(row, target, new GUIContent("Target"));
            row.y += line + Gap;
            ConsumableHealthTarget targetValue = (ConsumableHealthTarget)target.enumValueIndex;
            if (targetValue is ConsumableHealthTarget.FixedBodyPart or ConsumableHealthTarget.SpecificOrgan)
            {
                EditorGUI.PropertyField(row, property.FindPropertyRelative("targetBodyPart"),
                    new GUIContent("Body Part"));
                row.y += line + Gap;
            }
            if (targetValue == ConsumableHealthTarget.SpecificOrgan)
            {
                SerializedProperty organ = property.FindPropertyRelative("targetOrgan");
                int selected = System.Array.IndexOf(OrganCategories,
                    (SubPartCategory)organ.enumValueIndex);
                selected = EditorGUI.Popup(row, "Organ", Mathf.Max(0, selected), OrganNames);
                organ.enumValueIndex = (int)OrganCategories[selected];
                row.y += line + Gap;
            }
            if (change == ConsumableLifecycleChange.Health
                && property.FindPropertyRelative("amount").floatValue < 0f)
                EditorGUI.PropertyField(row, property.FindPropertyRelative("negativeHealthDamageType"),
                    new GUIContent("Damage Type"));
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    private static string ChangeAmountLabel(ConsumableLifecycleChange change) => change switch
    {
        ConsumableLifecycleChange.Saturation => "Saturation Change",
        ConsumableLifecycleChange.Stamina => "Stamina Change",
        ConsumableLifecycleChange.Biofluid => "Biofluid Change",
        ConsumableLifecycleChange.Health => "Health Change",
        ConsumableLifecycleChange.MaximumHealth => "Max Health Restore",
        _ => "Amount"
    };
}
