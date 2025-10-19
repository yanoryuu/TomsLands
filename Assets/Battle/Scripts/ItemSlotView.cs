using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System;
using UnityEngine.EventSystems;

public class ItemSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text stockText;
    [SerializeField] private CanvasGroup canvasGroup;

    // ドラッグ＆ドロップを全体に通知
    public static event Action<ItemSlotView, ItemSlotView> OnItemDropped;

    private static ItemSlotView draggedItem;

    private static Image ghostIcon;
    private static Canvas parentCanvas;

    public void SetItem(RuntimeItemData item)
    {
        if (item != null)
        {
            if (itemIcon != null)
            {
                itemIcon.enabled = true;
                itemIcon.sprite = item.ItemIcon;
            }
            if (stockText != null)
            {
                stockText.gameObject.SetActive(true);
                UpdateStock(item.Stock.Value);
            }
        }
        else
        {
            if (itemIcon != null) itemIcon.enabled = false;
            if (stockText != null) stockText.gameObject.SetActive(false);
        }
    }

    public void UpdateStock(int stock)
    {
        if (stockText == null) return;
        stockText.text = stock.ToString();
    }

    public void PlaySoldAnimation()
    {
        // シンプルな拡大縮小とフェードのアニメーション
        if (canvasGroup == null) return;
        canvasGroup.DOKill();
        canvasGroup.alpha = 1f;
        transform.DOKill();
        transform.localScale = Vector3.one;
        Sequence sequence = DOTween.Sequence();
        sequence.Append(transform.DOScale(1.2f, 0.15f));
        sequence.Join(canvasGroup.DOFade(0.5f, 0.15f));
        sequence.Append(transform.DOScale(1.0f, 0.2f));
        sequence.Join(canvasGroup.DOFade(1.0f, 0.2f));
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (itemIcon.enabled == false) return;

        // ドラッグ中のスロットを設定
        if (ghostIcon == null)
        {
            // 新しいGameObjectを作り、Imageコンポーネントを追加
            var ghostObj = new GameObject("GhostIcon");
            ghostObj.transform.SetParent(parentCanvas.transform, true); 
            ghostObj.transform.SetAsLastSibling();
            ghostIcon = ghostObj.AddComponent<Image>();
            ghostIcon.raycastTarget = false;
            ghostIcon.preserveAspect = true;
        }

        ghostIcon.sprite = this.itemIcon.sprite;
        ghostIcon.rectTransform.sizeDelta = this.GetComponent<RectTransform>().sizeDelta;
        ghostIcon.gameObject.SetActive(true);

        canvasGroup.alpha = 0.5f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (ghostIcon == null) return;
        ghostIcon.transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (ghostIcon != null)
        {
            ghostIcon.gameObject.SetActive(false);
        }
        canvasGroup.alpha = 1.0f;
    }

    public void OnDrop(PointerEventData eventData)
    {
        // eventDataから、ドラッグ元のスロットを取得
        var fromSlot = eventData.pointerDrag.GetComponent<ItemSlotView>();

        if (fromSlot == null || fromSlot == this) return;
        OnItemDropped?.Invoke(fromSlot, this);
    }
}