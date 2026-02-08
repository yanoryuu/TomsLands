using UnityEngine;

public class ConditionalHideAttribute : PropertyAttribute
{
    public string ConditionalSourceField;
    public int EnumValue;

    public ConditionalHideAttribute(string conditionalSourceField, int enumValue)
    {
        ConditionalSourceField = conditionalSourceField;
        EnumValue = enumValue;
    }
}