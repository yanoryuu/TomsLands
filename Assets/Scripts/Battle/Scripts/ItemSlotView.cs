﻿using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System;
using UnityEngine.EventSystems;

public class ItemSlotView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] private Image itemIcon;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text stockText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Behavior")]
    [SerializeField] private bool isSellSlot = false; // Inspectorで切り替え
    [SerializeField] private Image arrowImage; // 売り場ホバー時に動かす矢印
    [SerializeField] private TMP_Text priceTargetText; // 在庫ホバー時に表示する下四角のTMP

    [Header("配信価格（常に表示。未設定なら在庫数の文字を複製して作る）")]
    [Tooltip("いまの配信価格と、配信開始時からの上げ下げ（↑橙 / ↓青灰）。店の相場とは別の値段")]
    [SerializeField] private TMP_Text livePriceText;
    [SerializeField] private Vector2 livePriceOffset = new(0f, 50f);
    [SerializeField] private float livePriceFontSize = 30f;

    private const string PriceUpColor = "#FFA640";   // ui-rules の状態色（需要70%以上のオレンジ）に寄せる
    private const string PriceDownColor = "#A9BCD0"; // 同（需要40%未満の青灰）
    private string _shownPriceItemId;
    private int _shownPrice = -1;

    // ドラッグ＆ドロップを全体に通知
    public static event Action<ItemSlotView, ItemSlotView> OnItemDropped;

    // アイテムアイコンクリックを全体に通知（価格グラフ表示用）
    public static event Action<ItemSlotView> OnItemClicked;

    private static ItemSlotView draggedItem;

    private static Image ghostIcon;
    private static Canvas parentCanvas;

    // #13 置ける枠を光らせるために、表示中のスロットを覚えておく
    private static readonly System.Collections.Generic.List<ItemSlotView> LiveSlots = new();
    private bool _glowing;

    // 現在このスロットに割り当てられているRuntimeItemData（外部参照用に公開）
    public RuntimeItemData CurrentItem { get; private set; }

    // isSellSlot を外部参照可能にするプロパティ
    public bool IsSellSlot => isSellSlot;

    // DOTween の tweener を保持して確実に停止できるようにする
    private Tweener arrowTweener;

    // 元のローカル位置を保存（矢印の累積移動を防ぐ）
    private Vector3? arrowOriginalLocalPos;

    private void Awake()
    {
        if (parentCanvas == null)
            parentCanvas = GetComponentInParent<Canvas>();

        // パッド/キー: 選択でホバー表示（矢印・価格）、A でクリック（価格グラフ・補充）を転送する。
        // マウス操作中は何もしない（PointerEventBridge 側で判定）
        if (!TryGetComponent<PointerEventBridge>(out _)) gameObject.AddComponent<PointerEventBridge>();

        if (arrowImage != null)
        {
            arrowImage.gameObject.SetActive(false);
            arrowOriginalLocalPos = arrowImage.rectTransform.localPosition;
        }
        if (priceTargetText != null)
            priceTargetText.gameObject.SetActive(false);
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
                itemIcon.preserveAspect = true;
                // 色も確実にリセット
                var color = itemIcon.color;
                color.a = 1.0f;
                itemIcon.color = color;
            }
            if (backgroundImage != null)
            {
                if (item.ItemBackground != null)
                {
                    backgroundImage.sprite = item.ItemBackground;
                    backgroundImage.enabled = true;
                }
                else
                {
                    backgroundImage.enabled = false;
                }
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
            if (backgroundImage != null) backgroundImage.enabled = false;
            if (stockText != null) stockText.gameObject.SetActive(false);
        }
        UpdateLivePrice(item);

        if (canvasGroup != null)
            canvasGroup.alpha = 1.0f;
    }

    /// <summary>
    /// 配信価格を常に出す（SUMMARY E-5）。配信開始時（BattlePriceHistory の先頭）より上なら ↑、下なら ↓。
    /// 同じ品の値段が変わったフレームは数字を跳ねさせる。
    /// </summary>
    private void UpdateLivePrice(RuntimeItemData item)
    {
        var label = EnsureLivePriceText();
        if (label == null) return;
        if (item == null)
        {
            label.gameObject.SetActive(false);
            _shownPriceItemId = null;
            _shownPrice = -1;
            return;
        }

        int price = item.CurrentPrice.Value;
        var history = item.BattlePriceHistory;
        int start = history != null && history.Count > 0 ? history[0] : price;
        string arrow = price > start ? $"<color={PriceUpColor}>↑</color>"
                     : price < start ? $"<color={PriceDownColor}>↓</color>"
                     : string.Empty;
        label.text = $"{price:N0}{arrow}";
        label.gameObject.SetActive(true);

        bool changed = _shownPriceItemId == item.ItemId && _shownPrice >= 0 && _shownPrice != price;
        int direction = changed ? (price > _shownPrice ? 1 : -1) : 0;
        _shownPriceItemId = item.ItemId;
        _shownPrice = price;
        // #12 値段の変化（共通部品）: 上がれば緑に光って膨らみ▲、下がれば赤く沈んで▼。従来の UIFx.Pop の置き換え。
        // 色・大きさ・位置は部品が必ず元に戻す。↑↓（配信開始時との比較）の文字はそのまま
        if (changed) PriceChangeFx.Play(label, direction);
    }

    private TMP_Text EnsureLivePriceText()
    {
        if (livePriceText != null) return livePriceText;
        if (stockText == null) return null;
        // 在庫数の文字（フォント・縁取り）をそのまま複製して上側に置く
        livePriceText = Instantiate(stockText, transform);
        livePriceText.name = "LivePrice";
        livePriceText.raycastTarget = false;
        livePriceText.enableAutoSizing = false;
        livePriceText.fontSize = livePriceFontSize;
        livePriceText.alignment = TextAlignmentOptions.Center;
        livePriceText.textWrappingMode = TextWrappingModes.NoWrap;
        livePriceText.overflowMode = TextOverflowModes.Overflow;
        var rt = livePriceText.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(140f, 30f);
        rt.anchoredPosition = livePriceOffset;
        return livePriceText;
    }

    public void UpdateStock(int stock)
    {
        if (stockText == null) return;
        stockText.text = stock.ToString();
    }

    public void PlaySoldAnimation()
    {
        if (canvasGroup == null) return;

        // 既存のTweenをすべてキャンセルして初期状態に戻す
        canvasGroup.DOKill();
        canvasGroup.alpha = 1f;
        transform.DOKill();
        transform.localScale = Vector3.one;
        if (itemIcon != null)
        {
            itemIcon.DOKill();
            itemIcon.color = Color.white;
        }

        Sequence seq = DOTween.Sequence();

        // 1. 大きくポップアップ（より派手に）
        seq.Append(transform.DOScale(1.4f, 0.1f).SetEase(Ease.OutQuad));

        // 2. アイコンを金色にフラッシュ
        if (itemIcon != null)
            seq.Join(itemIcon.DOColor(new Color(1f, 0.88f, 0.1f), 0.1f));

        // 3. 半透明にフェード
        seq.Join(canvasGroup.DOFade(0.3f, 0.1f));

        // 4. バウンスしながら元のサイズに戻す
        seq.Append(transform.DOScale(1f, 0.3f).SetEase(Ease.OutBounce));

        // 5. アイコン色と透明度を元に戻す
        if (itemIcon != null)
            seq.Join(itemIcon.DOColor(Color.white, 0.25f));
        seq.Join(canvasGroup.DOFade(1f, 0.2f));
        seq.SetLink(gameObject);
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
        FitGhost();
        ghostIcon.gameObject.SetActive(true);
        // #13 持ち上げる（ゴーストが 1.12 倍に膨らんで傾き、影が付く）＋置き先の枠を光らせる。ドラッグのイベントには触らない
        DragFeel.Lift(ghostIcon.rectTransform);
        GlowTargetsFor(this);

        // 元のアイコンを半透明にする（非表示にしない）
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0.5f;
        }
        else
        {
            // canvasGroup がない場合は itemIcon の色を調整
            var color = itemIcon.color;
            color.a = 0.5f;
            itemIcon.color = color;
        }

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
            // 隠した後なので持ち上げの拡縮・傾き・影を即座に元へ戻すだけ（つぶれる演出は置いたスロット側で出す）
            DragFeel.Drop(ghostIcon.rectTransform);
        }
        ClearAllGlow();

        // 元のアイコンを完全に表示に戻す
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
        }
        else
        {
            var color = itemIcon.color;
            color.a = 1.0f;
            itemIcon.color = color;
        }

        draggedItem = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        // UnityEngine.Object に ?. は使わない
        var dragObj = eventData.pointerDrag;
        var fromSlot = dragObj != null ? dragObj.GetComponent<ItemSlotView>() : null;

        if (fromSlot == null || fromSlot == this) return;
        OnItemDropped?.Invoke(fromSlot, this);
        PlayPlaced();
    }

    // ─────────────────────────────────────────
    //  #13 つかんで置く（見た目だけ。入れ替えは OnItemDropped の購読側が決める）
    // ─────────────────────────────────────────

    private void OnEnable()
    {
        if (!LiveSlots.Contains(this)) LiveSlots.Add(this);
    }

    /// <summary>置いたスロットのアイコンをつぶして戻し、ほこりを散らす（何か置かれたときだけ）。</summary>
    private void PlayPlaced()
    {
        if (CurrentItem == null || itemIcon == null || !itemIcon.enabled) return;
        DragFeel.Drop(itemIcon.rectTransform, placed: true);
    }

    /// <summary>
    /// つかんだスロットに応じて置き先を光らせる。在庫からつかんだら売り場の全スロット（主な置き先）、
    /// 売り場からつかんだら空いている在庫スロット。どこへ置いても入れ替わるので、ほかの枠は指したときだけ光る。
    /// </summary>
    private static void GlowTargetsFor(ItemSlotView from)
    {
        ClearAllGlow();
        if (from == null) return;
        foreach (var slot in LiveSlots)
        {
            if (slot == null || slot == from || !slot.isActiveAndEnabled) continue;
            if (slot.IsMainTargetOf(from)) slot.SetGlow(true);
        }
    }

    private bool IsMainTargetOf(ItemSlotView from)
        => from != null && from != this && (from.isSellSlot ? (!isSellSlot && CurrentItem == null) : isSellSlot);

    private static void ClearAllGlow()
    {
        for (int i = LiveSlots.Count - 1; i >= 0; i--)
        {
            var slot = LiveSlots[i];
            if (slot == null) { LiveSlots.RemoveAt(i); continue; }
            slot.SetGlow(false);
        }
    }

    private void SetGlow(bool on)
    {
        if (_glowing == on) return;
        _glowing = on;
        DragFeel.SetSlotGlow(transform as RectTransform, on);
    }

    /// <summary>持ち上げ中（ドラッグ・パッド）にこのスロットを指したら光らせる。</summary>
    private void GlowIfHovered()
    {
        var held = draggedItem != null ? draggedItem : padHeld;
        if (held != null && held != this) SetGlow(true);
    }

    // ホバー処理（簡潔に、かつドラッグ中の抑制を含む）
    public void OnPointerEnter(PointerEventData eventData)
    {
        GlowIfHovered();

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
            arrowTweener = rt.DOLocalMoveY(targetY, 0.45f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(gameObject);
        }

        // CurrentItem が null なら価格表示をスキップ
        if (CurrentItem == null) return;

        // ドラッグ中で、ドラッグ元が売り場なら在庫の価格表示を抑制する
        if (draggedItem != null && draggedItem.IsSellSlot) return;

        // priceTargetText が設定されていない場合もスキップ
        if (priceTargetText == null) return;

        priceTargetText.text = $"{CurrentItem.CurrentPrice.Value} G";
        priceTargetText.gameObject.SetActive(true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // パッドで「持ち上げ」中なら、ここを置き先にする（ドラッグ＆ドロップと同じ通知）
        if (padHeld != null)
        {
            var from = padHeld;
            CancelPadHold();
            if (from != this)
            {
                OnItemDropped?.Invoke(from, this);
                PlayPlaced();
            }
            return;
        }

        // ドラッグ操作中や空スロットはスキップ
        if (draggedItem != null) return;
        if (CurrentItem == null) return;
        OnItemClicked?.Invoke(this);
    }

    // ─────────────────────────────────────────
    //  パッド操作: X で持ち上げ → 置き先を選んで A（ドラッグ＆ドロップの代替）
    // ─────────────────────────────────────────

    private static ItemSlotView padHeld;
    private const float PadGhostLift = 0.35f;   // 置き先スロットの高さに対する浮かせ量
    private const float PadGhostBob = 6f;       // 上下のゆれ幅（px）

    /// <summary>パッドでアイテムを持ち上げ中か。</summary>
    public static bool IsPadHolding => padHeld != null;

    /// <summary>
    /// このスロットのアイテムを持ち上げる（ドラッグ開始と同じ見た目: 元を半透明・ゴーストを表示）。
    /// 空スロットなら何もしない。
    /// </summary>
    public bool PadPickUp()
    {
        if (CurrentItem == null || itemIcon == null || !itemIcon.enabled) return false;
        if (draggedItem != null) return false;
        if (padHeld != null) CancelPadHold();
        if (parentCanvas == null) parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) return false;

        if (ghostIcon == null)
        {
            var ghostObj = new GameObject("GhostIcon");
            ghostObj.transform.SetParent(parentCanvas.transform, false);
            ghostIcon = ghostObj.AddComponent<Image>();
            ghostIcon.raycastTarget = false;
            ghostIcon.preserveAspect = true;
            if (ghostObj.GetComponent<CanvasGroup>() == null) ghostObj.AddComponent<CanvasGroup>();
        }
        ghostIcon.transform.SetAsLastSibling();
        ghostIcon.sprite = itemIcon.sprite;
        FitGhost();
        ghostIcon.gameObject.SetActive(true);
        SetHeldLook(true);

        padHeld = this;
        UpdatePadGhost();
        // #13 マウスのドラッグと同じ持ち上げ（従来の UIFx.Pop の置き換え）と置き先の光
        DragFeel.Lift(ghostIcon.rectTransform);
        GlowTargetsFor(this);
        return true;
    }

    /// <summary>
    /// このスロットのアイテムを target に置く（パッドの「X で並べる」）。
    /// ドラッグ＆ドロップ（OnDrop）と同じ通知を出すので、入替・在庫数・売り場の扱いは D&amp;D と同一。
    /// </summary>
    public bool PadPlaceOnto(ItemSlotView target)
    {
        if (target == null || target == this || CurrentItem == null) return false;
        if (draggedItem != null) return false;
        if (padHeld != null) CancelPadHold();
        OnItemDropped?.Invoke(this, target);
        target.PlayPlaced();
        return true;
    }

    /// <summary>
    /// ゴーストの大きさ・向きをそろえる。どのスロット（在庫は 0.65 倍に縮めて並べている）・どの品物からつかんでも同じ大きさで持ち上がるよう、
    /// スロットの拡縮（売れたときのポップ・在庫の縮小）は数えず、アイコンの枠の大きさ（売り場の見た目の大きさ）にする。
    /// 前の持ち上げの拡縮・傾きが残っていても戻してから持ち上げる。
    /// </summary>
    private void FitGhost()
    {
        if (ghostIcon == null) return;
        var grt = ghostIcon.rectTransform;
        var src = itemIcon != null ? itemIcon.rectTransform : (RectTransform)transform;
        Vector2 size = src.rect.size;
        var from = transform.parent;
        var to = grt.parent;
        if (from != null && to != null)
        {
            Vector3 a = from.lossyScale, b = to.lossyScale;
            if (Mathf.Abs(b.x) > 1e-5f && Mathf.Abs(b.y) > 1e-5f)
                size = new Vector2(size.x * Mathf.Abs(a.x / b.x), size.y * Mathf.Abs(a.y / b.y));
        }
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
        grt.sizeDelta = size;
        DOTween.Kill(grt.gameObject); // 前の持ち上げの途中なら止めて、大きさ・向きを元に戻す
        grt.localScale = Vector3.one;
        grt.localRotation = Quaternion.identity;
    }

    /// <summary>持ち上げを取り消す（B・マウスへの切替・パネル非表示など）。</summary>
    public static void CancelPadHold()
    {
        var held = padHeld;
        padHeld = null;
        if (ghostIcon != null)
        {
            ghostIcon.gameObject.SetActive(false);
            DragFeel.Drop(ghostIcon.rectTransform); // 隠した後なので拡縮・傾き・影を即座に戻すだけ
        }
        if (held != null) held.SetHeldLook(false);
        if (held != null || draggedItem == null) ClearAllGlow();
    }

    private void SetHeldLook(bool held)
    {
        float a = held ? 0.5f : 1f;
        if (canvasGroup != null) canvasGroup.alpha = a;
        else if (itemIcon != null)
        {
            var c = itemIcon.color;
            c.a = a;
            itemIcon.color = c;
        }
    }

    private void LateUpdate()
    {
        if (padHeld == this) UpdatePadGhost();
    }

    /// <summary>ゴーストを「いま選んでいるスロット」の少し上に浮かせる。</summary>
    private void UpdatePadGhost()
    {
        if (ghostIcon == null || parentCanvas == null) return;
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        var target = sel != null ? sel.GetComponent<ItemSlotView>() : null;
        var rt = (target != null ? target.transform : transform) as RectTransform;
        if (rt == null) return;

        var srcCanvas = rt.GetComponentInParent<Canvas>();
        var srcCam = CanvasCamera(srcCanvas);
        var dstCam = CanvasCamera(parentCanvas);
        var top = rt.TransformPoint(new Vector3(rt.rect.center.x, rt.rect.yMax, 0f));
        var screen = RectTransformUtility.WorldToScreenPoint(srcCam, top);
        float scale = parentCanvas.rootCanvas.scaleFactor;
        screen.y += (rt.rect.height * PadGhostLift + Mathf.Sin(Time.unscaledTime * 5f) * PadGhostBob) * scale;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentCanvas.transform as RectTransform, screen, dstCam, out var world))
            ghostIcon.transform.position = world;
    }

    private static Camera CanvasCamera(Canvas canvas)
    {
        if (canvas == null) return null;
        var root = canvas.rootCanvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }

    private void OnDisable()
    {
        if (padHeld == this) CancelPadHold();
        LiveSlots.Remove(this);
        SetGlow(false);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 持ち上げ中に指していただけの枠は光を消す（主な置き先として光らせている枠はそのまま）
        var held = draggedItem != null ? draggedItem : padHeld;
        if (held != null && _glowing && !IsMainTargetOf(held)) SetGlow(false);

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
