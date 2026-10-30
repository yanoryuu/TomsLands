using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 勇者の画面の「品物の棚」のカード 1 枚（武器・防具 1 品、または「なし（外す）」）。
/// 見た目を出すだけで、持ち上げる・置くは HeroPanelView が決める（クリック・決定は Button.onClick、ドラッグはここから通知）。
/// 縦に動かし始めたドラッグは棚のスクロールに回す（カードを掴んでも棚を上下に送れるように）。
/// カードはプレハブの CardTemplate（非表示）を View が複製して使い回す。
/// </summary>
public sealed class HeroEquipmentCard : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    [SerializeField] private Button button;
    [Tooltip("持ち上げるときに複製してゴーストにする部分（枠とアイコン）")]
    [SerializeField] private RectTransform iconBox;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("在庫・値段")]
    [SerializeField] private TextMeshProUGUI infoText;
    [Tooltip("攻撃+10% / 防御+15% HP+5%")]
    [SerializeField] private TextMeshProUGUI bonusText;
    [SerializeField] private GameObject attributeRoot;
    [SerializeField] private Image attributeIcon;
    [SerializeField] private TextMeshProUGUI attributeText;
    [Tooltip("弱点に合う品の赤い札と赤い縁")]
    [SerializeField] private GameObject weakTag;
    [SerializeField] private GameObject weakFrame;
    [Tooltip("いま付けている品の札")]
    [SerializeField] private GameObject equippedTag;
    [Tooltip("在庫 0・持ち上げ中に上に重ねる生成りの幕（CanvasGroup は棚の並ぶ演出が alpha を使うので触らない）")]
    [SerializeField] private Image dimOverlay;

    /// <summary>在庫 0 の品に重ねる幕の濃さ（選べるが目立たせない。今のプルダウンの淡い色に合わせる）。</summary>
    private const float NoStockDim = 0.5f;
    private const float CarriedDim = 0.62f;

    public Button Button => button;
    public RectTransform IconBox => iconBox;
    public string ItemId { get; private set; } = string.Empty;
    public bool IsWeapon { get; private set; }
    /// <summary>付けた後の戦う強さ（Presenter が計算したもの）。</summary>
    public ClearProbabilityCalculator.HeroStats After { get; private set; }

    public event Action<HeroEquipmentCard, PointerEventData> DragStarted;
    public event Action<HeroEquipmentCard, PointerEventData> Dragging;
    public event Action<HeroEquipmentCard, PointerEventData> DragEnded;

    private bool outOfStock;
    private bool carried;
    private bool passToScroll;
    private ScrollRect parentScroll;

    /// <summary>品のカードにする。</summary>
    public void Bind(HeroEquipmentOption option, bool isWeapon, bool equipped, Sprite attrSprite)
    {
        ItemId = option.ItemId ?? string.Empty;
        IsWeapon = isWeapon;
        After = option.After;
        gameObject.name = $"Card_{ItemId}";

        SetIcon(option.Icon);
        if (nameText != null) nameText.text = string.IsNullOrEmpty(option.Name) ? option.Label : option.Name;
        if (infoText != null)
            infoText.text = option.NotOwned ? "未所持" : $"在庫{option.Stock}・{option.Price:N0}G";
        if (bonusText != null) bonusText.text = option.BonusText ?? string.Empty;
        if (attributeRoot != null) attributeRoot.SetActive(!string.IsNullOrEmpty(option.AttributeLabel));
        if (attributeText != null) attributeText.text = option.AttributeLabel ?? string.Empty;
        if (attributeIcon != null)
        {
            attributeIcon.sprite = attrSprite;
            attributeIcon.enabled = attrSprite != null;
        }
        if (weakTag != null) weakTag.SetActive(option.IsWeakness);
        if (weakFrame != null) weakFrame.SetActive(option.IsWeakness);
        if (equippedTag != null) equippedTag.SetActive(equipped);
        outOfStock = option.NotOwned || option.Stock <= 0;
        carried = false;
        ApplyAlpha();
    }

    /// <summary>「なし（外す）」のカードにする。after は外した後の戦う強さ。</summary>
    public void BindNone(bool isWeapon, bool equipped, ClearProbabilityCalculator.HeroStats after)
    {
        ItemId = string.Empty;
        IsWeapon = isWeapon;
        After = after;
        gameObject.name = "Card_None";

        SetIcon(null);
        if (nameText != null) nameText.text = "なし";
        if (infoText != null) infoText.text = isWeapon ? "武器を外す" : "防具を外す";
        if (bonusText != null) bonusText.text = string.Empty;
        if (attributeRoot != null) attributeRoot.SetActive(false);
        if (weakTag != null) weakTag.SetActive(false);
        if (weakFrame != null) weakFrame.SetActive(false);
        if (equippedTag != null) equippedTag.SetActive(equipped);
        outOfStock = false;
        carried = false;
        ApplyAlpha();
    }

    /// <summary>持ち上げ中（元のカードを薄くする）。</summary>
    public void SetCarried(bool on)
    {
        carried = on;
        ApplyAlpha();
    }

    private void ApplyAlpha()
    {
        if (dimOverlay == null) return;
        float a = carried ? CarriedDim : outOfStock ? NoStockDim : 0f;
        dimOverlay.gameObject.SetActive(a > 0f);
        var c = dimOverlay.color;
        c.a = a;
        dimOverlay.color = c;
    }

    private void SetIcon(Sprite sprite)
    {
        if (icon == null) return;
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        icon.preserveAspect = true;
    }

    // ───────── ドラッグ（縦の動き出しは棚のスクロールへ回す） ─────────

    private ScrollRect Scroll => parentScroll != null ? parentScroll : parentScroll = GetComponentInParent<ScrollRect>();

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (Scroll != null) Scroll.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        var d = eventData.delta;
        passToScroll = Scroll != null && Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.5f;
        if (passToScroll)
        {
            Scroll.OnBeginDrag(eventData);
            return;
        }
        DragStarted?.Invoke(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (passToScroll)
        {
            if (Scroll != null) Scroll.OnDrag(eventData);
            return;
        }
        Dragging?.Invoke(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (passToScroll)
        {
            passToScroll = false;
            if (Scroll != null) Scroll.OnEndDrag(eventData);
            return;
        }
        DragEnded?.Invoke(this, eventData);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (Scroll != null) Scroll.OnScroll(eventData);
    }
}
