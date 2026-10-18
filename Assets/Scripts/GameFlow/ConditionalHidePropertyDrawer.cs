#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ConditionalHideAttribute))]
public class ConditionalHidePropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var condHAtt = (ConditionalHideAttribute)attribute;
        if (GetConditionalHideAttributeResult(condHAtt, property))
        {
            EditorGUI.PropertyField(position, property, label, true);
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var condHAtt = (ConditionalHideAttribute)attribute;
        return GetConditionalHideAttributeResult(condHAtt, property)
            ? EditorGUI.GetPropertyHeight(property, label)
            : -EditorGUIUtility.standardVerticalSpacing;
    }

    private static bool GetConditionalHideAttributeResult(ConditionalHideAttribute condHAtt, SerializedProperty property)
    {
        var sourcePropertyValue = property.serializedObject.FindProperty(property.propertyPath.Replace(property.name, condHAtt.ConditionalSourceField));
        return sourcePropertyValue == null || sourcePropertyValue.enumValueIndex == condHAtt.EnumValue;
    }
}
#endif
