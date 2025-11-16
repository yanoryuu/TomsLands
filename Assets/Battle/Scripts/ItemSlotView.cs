using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System;
using UnityEngine.EventSystems;

public class ItemSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private TMP_Text stockText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Behavior")]
    [SerializeField] private bool isSellSlot = false; // Inspectorで切り替え
    [SerializeField] private Image arrowImage; // 売り場ホバー時に動かす矢印
    [SerializeField] private TMP_Text priceTargetText; // 在庫ホバー時に表示する下四角のTMP

    // ドラッグ＆ドロップを全体に通知
    public static event Action<ItemSlotView, ItemSlotView> OnItemDropped;

    private static ItemSlotView draggedItem;

    private static Image ghostIcon;
    private static Canvas parentCanvas;

    // 現在このスロットに割り当てられているRuntimeItemData（外部参照用に公開）
    public RuntimeItemData CurrentItem { get; private set; }

    // isSellSlot を外部参照可能にするプロパティ
    public bool IsSellSlot => isSellSlot;

    // DOTween の tweener を保持して確実に停止できるようにする
    private Tweener arrowTweener;

    // 元のローカル位置を保存（矢印の累積移動を防ぐ）
    private Vector3? arrowOriginalLocalPos = null;

    void Awake()
    {
        if (parentCanvas == null)
        {
            parentCanvas = GetComponentInParent<Canvas>();
        }

        if (arrowImage != null)
        {
            arrowImage.gameObject.SetActive(false);
            arrowOriginalLocalPos = arrowImage.rectTransform.localPosition;
        }
        if (priceTargetText != null)
        {
            priceTargetText.gameObject.SetActive(false);
        }
    }

    public void SetItem(RuntimeItemData item)
    {
        CurrentItem = item;

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
        if (itemIcon == null || itemIcon.enabled == false) return;

        if (ghostIcon == null)
        {
            var ghostObj = new GameObject("GhostIcon");
            ghostObj.transform.SetParent(parentCanvas.transform, false);
            ghostObj.transform.SetAsLastSibling();
            ghostIcon = ghostObj.AddComponent<Image>();
            ghostIcon.raycastTarget = false;
            ghostIcon.preserveAspect = true;
            if (ghostObj.GetComponent<CanvasGroup>() == null) ghostObj.AddComponent<CanvasGroup>();
        }

        ghostIcon.sprite = this.itemIcon.sprite;
        ghostIcon.rectTransform.sizeDelta = this.GetComponent<RectTransform>().sizeDelta;
        ghostIcon.gameObject.SetActive(true);

        if (canvasGroup != null) canvasGroup.alpha = 0.5f;
        draggedItem = this;
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
        if (canvasGroup != null) canvasGroup.alpha = 1.0f;
        draggedItem = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        var fromSlot = eventData.pointerDrag?.GetComponent<ItemSlotView>();

        if (fromSlot == null || fromSlot == this) return;
        OnItemDropped?.Invoke(fromSlot, this);
    }

    // ホバー処理（簡潔に、かつドラッグ中の抑制を含む）
    public void OnPointerEnter(PointerEventData eventData)
    {
        // 矢印が割り当てられている場合に矢印処理を行う
        if (arrowImage != null)
        {
            arrowImage.gameObject.SetActive(true);
            arrowTweener?.Kill();

            var rt = arrowImage.rectTransform;
            if (arrowOriginalLocalPos.HasValue)
            {
                rt.localPosition = arrowOriginalLocalPos.Value;
            }

            float offset = 8f;
            float targetY = (arrowOriginalLocalPos?.y ?? rt.localPosition.y) + offset;
            arrowTweener = rt.DOLocalMoveY(targetY, 0.45f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
        }

        // ドラッグ中で、ドラッグ元が売り場なら在庫の価格表示を抑制する
        if (draggedItem != null && draggedItem.IsSellSlot) return;

        // それ以外（在庫側など）は価格表示
        // 直接参照で価格を取得（まずは CurrentPrice.Value を優先、その次に BasePrice）
        string priceStr = null;
        priceStr = $"{CurrentItem.CurrentPrice.Value} G";

        if (string.IsNullOrEmpty(priceStr))
        {
            Debug.LogWarning($"[ItemSlotView] Price not found for item {CurrentItem?.ItemId ?? "<unknown>"} on {gameObject.name}");
            priceTargetText.text = "—";
        }
        else
        {
            priceTargetText.text = priceStr;
        }
        priceTargetText.gameObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (arrowImage != null)
        {
            arrowTweener?.Kill();
            arrowTweener = null;
            if (arrowOriginalLocalPos.HasValue)
                arrowImage.rectTransform.localPosition = arrowOriginalLocalPos.Value;
            if (arrowImage != null) arrowImage.gameObject.SetActive(false);
        }

        if (priceTargetText != null)
        {
            priceTargetText.text = "";
            priceTargetText.gameObject.SetActive(false);
        }
    }
}
