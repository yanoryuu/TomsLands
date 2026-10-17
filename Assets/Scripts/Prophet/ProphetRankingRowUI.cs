using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ProphetRankingRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private TMP_Text priceText;

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

    public void SetData(int rank, Sprite icon, string itemName, int price, string dialogueId = "")
    {
        rankText.text = rank.ToString();
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        itemNameText.text = itemName;
        priceText.text = $"{price:N0}G";
        this.dialogueId = dialogueId;
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(dialogueId);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}
