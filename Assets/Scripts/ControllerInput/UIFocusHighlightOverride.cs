using UnityEngine;

/// <summary>
/// 選択枠の出し方を要素ごとに調整する（任意）。Selectable と同じ GameObject に付ける。
/// ・hide: この要素では枠を出さない（独自の選択演出がある場合など）
/// ・target: 枠を合わせる矩形（Selectable の矩形が大きすぎる/小さすぎる場合）
/// ・padding: 枠の外側への余白（px, 1920x1080 基準）。負で内側。
/// </summary>
[DisallowMultipleComponent]
public class UIFocusHighlightOverride : MonoBehaviour
{
    public bool hide;
    public RectTransform target;
    public Vector2 padding = new(6f, 6f);
}
