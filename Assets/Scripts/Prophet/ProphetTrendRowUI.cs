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

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(dialogueId);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}
