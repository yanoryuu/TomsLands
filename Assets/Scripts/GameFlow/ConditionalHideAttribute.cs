using UnityEngine;

public class ConditionalHideAttribute : PropertyAttribute
{
    public readonly string ConditionalSourceField;
    public readonly int EnumValue;

    public ConditionalHideAttribute(string conditionalSourceField, int enumValue)
    {
        ConditionalSourceField = conditionalSourceField;
        EnumValue = enumValue;
    }
}
