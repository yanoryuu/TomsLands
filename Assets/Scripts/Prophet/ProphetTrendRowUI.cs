using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ProphetTrendRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text trendText;
    [SerializeField] private TMP_Text demandText;

    [SerializeField] private Image rowBackground;

    private string dialogueId;

    public event Action<string> OnHoverEnter;
    public event Action OnHoverExit;

    private void Awake()
    {
        // パッド/キーの選択枠を行の背景に合わせる（行の RectTransform はアイコン幅しかない）
        if (rowBackground != null && GetComponent<UIFocusHighlightOverride>() == null)
            gameObject.AddComponent<UIFocusHighlightOverride>().target = rowBackground.rectTransform;
    }

    /// <summary>
    /// 1行分を描画する。
    /// 第3引数は以前 Trend（流行度＝需要が向かう均衡値）だったが、これは「この先どう動くか」
    /// という未来の情報で、無料で並べると「次に上がる銘柄の答え」そのものになってしまう。
    /// 現在の値動きから計算できる Heat（相場の荒れ具合）に差し替えた。
    /// 詳細は Docs/News_Spec.md §2 C5。
    /// </summary>
    public void SetData(Sprite icon, string itemName, float heat, float demand, string dialogueId = "")
    {
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        itemNameText.text = itemName;
        trendText.text = $"相場: {MarketHeat.Describe(heat)}";
        trendText.color = MarketHeat.ToColor(heat);
        demandText.text = $"需要: {demand * 100f:F0}%";
        this.dialogueId = dialogueId;
    }

    /// <summary>占いの結果の1行（I-11）。「↑ 上がる」「↓ 下がる」と、大きく動くかどうか。</summary>
    public void SetFortune(Sprite icon, string itemName, int direction, bool strong, string dialogueId = "")
    {
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        bool up = direction > 0;
        // 向きは品名の前に矢印で出す（右側の小さな欄は狭く、行によって切れるため）
        string arrow = up ? "<color=#C8731E>↑</color>" : "<color=#4F6F8F>↓</color>";
        string size = strong ? (up ? "大きく上がる" : "大きく下がる") : (up ? "上がる" : "下がる");
        itemNameText.text = $"{arrow} {itemName} <size=60%>{size}</size>";
        trendText.text = string.Empty;
        demandText.text = string.Empty;
        this.dialogueId = dialogueId;
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(dialogueId);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}
