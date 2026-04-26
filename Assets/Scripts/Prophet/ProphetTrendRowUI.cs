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

    public void SetData(Sprite icon, string itemName, float trend, float demand, string dialogueId = "")
    {
        if (itemIcon != null && icon != null) itemIcon.sprite = icon;
        itemNameText.text = itemName;
        trendText.text = trend >= 0f ? $"トレンド: ▲{trend:F2}" : $"トレンド: ▼{Mathf.Abs(trend):F2}";
        demandText.text = $"需要: {demand * 100f:F0}%";
        this.dialogueId = dialogueId;
    }

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(dialogueId);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}
