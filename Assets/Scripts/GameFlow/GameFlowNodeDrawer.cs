#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(GameFlowNode))]
public class GameFlowNodeDrawer : PropertyDrawer
{
    private const float Padding = 3f;
    private const float StripeWidth = 4f;

    // EventType の順番（enum定義順）に対応
    private static readonly Color[] BgColors =
    {
        new Color(0.40f, 0.85f, 0.50f, 0.25f), // Start  - 緑
        new Color(1.00f, 0.35f, 0.35f, 0.25f), // Battle - 赤
        new Color(1.00f, 0.85f, 0.20f, 0.25f), // Shop   - 黄
        new Color(0.55f, 0.45f, 1.00f, 0.25f), // Event  - 紫
        new Color(0.55f, 0.55f, 0.55f, 0.25f), // End    - グレー
    };

    private static readonly Color[] StripeColors =
    {
        new Color(0.20f, 0.75f, 0.30f, 1f), // Start
        new Color(0.90f, 0.20f, 0.20f, 1f), // Battle
        new Color(0.90f, 0.75f, 0.00f, 1f), // Shop
        new Color(0.50f, 0.30f, 1.00f, 1f), // Event
        new Color(0.45f, 0.45f, 0.45f, 1f), // End
    };

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var eventTypeProp = property.FindPropertyRelative("EventType");
        int idx = eventTypeProp.enumValueIndex;

        // 背景
        Color bg = idx >= 0 && idx < BgColors.Length ? BgColors[idx] : Color.clear;
        EditorGUI.DrawRect(position, bg);

        // 左帯
        Color stripe = idx >= 0 && idx < StripeColors.Length ? StripeColors[idx] : Color.gray;
        EditorGUI.DrawRect(new Rect(position.x, position.y, StripeWidth, position.height), stripe);

        EditorGUI.BeginProperty(position, label, property);

        float x = position.x + StripeWidth + 4f;
        float w = position.width - StripeWidth - 4f;
        float lh = EditorGUIUtility.singleLineHeight;
        float sp = EditorGUIUtility.standardVerticalSpacing;
        float y = position.y + Padding;

        // ラベルをイベント名＋補足情報で上書き
        string labelText = BuildLabel(property, eventTypeProp);
        EditorGUI.LabelField(new Rect(x, y, w, lh), labelText, EditorStyles.boldLabel);
        y += lh + sp;

        // EventType
        EditorGUI.PropertyField(new Rect(x, y, w, lh), eventTypeProp);
        y += lh + sp;

        // 条件フィールド
        if (idx == (int)GameEvent.Battle)
        {
            var prop = property.FindPropertyRelative("BattleDungeon");
            EditorGUI.PropertyField(new Rect(x, y, w, lh), prop);
        }
        else if (idx == (int)GameEvent.Event)
        {
            var prop = property.FindPropertyRelative("EventData");
            EditorGUI.PropertyField(new Rect(x, y, w, lh), prop);
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var eventTypeProp = property.FindPropertyRelative("EventType");
        int idx = eventTypeProp.enumValueIndex;

        float lh = EditorGUIUtility.singleLineHeight;
        float sp = EditorGUIUtility.standardVerticalSpacing;

        // ラベル行 + EventType行
        float h = (lh + sp) * 2;

        // 条件フィールド行
        if (idx == (int)GameEvent.Battle || idx == (int)GameEvent.Event)
            h += lh + sp;

        return h + Padding * 2;
    }

    private static string BuildLabel(SerializedProperty property, SerializedProperty eventTypeProp)
    {
        int idx = eventTypeProp.enumValueIndex;
        string eventName = idx >= 0 && idx < eventTypeProp.enumNames.Length
            ? eventTypeProp.enumNames[idx]
            : "Unknown";

        if (idx == (int)GameEvent.Battle)
        {
            var dungeon = property.FindPropertyRelative("BattleDungeon");
            return $"Battle  ▶  {dungeon.enumNames[dungeon.enumValueIndex]}";
        }
        if (idx == (int)GameEvent.Event)
        {
            var data = property.FindPropertyRelative("EventData");
            string d = string.IsNullOrEmpty(data.stringValue) ? "（未設定）" : data.stringValue;
            return $"Event  ▶  {d}";
        }
        return eventName;
    }
}
#endif
