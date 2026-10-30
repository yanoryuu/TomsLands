using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>占い師の画面の品のカード 1 枚に出す中身（Presenter が作る）。</summary>
public struct ProphetItemInfo
{
    public string ItemId;
    public string Name;
    public Sprite Icon;
    public ItemTypeData.ItemType Type;
    public int Price;
    /// <summary>前日比（0.05 = 5% 上がった）。</summary>
    public float Change1d;
    public int Stock;
    /// <summary>
    /// 新聞の向き: プレイヤーが新聞のスクラップに書いた「予想メモ」（この品が上がる）に当てはまれば +1、無ければ 0。
    /// 記事から品へのリンクはゲームが作らない（Docs/News_Spec.md §9.3）ので、プレイヤー自身の予想だけを出す。
    /// </summary>
    public int NewsDirection;
    /// <summary>枠に入っているか（入っていれば向き。0 = 入っていない）。</summary>
    public int InSlot;
}

/// <summary>
/// 品のカード（棚の 1 枚）。ドラッグで上げる枠・下げる枠へ入れる。押すと選ぶ（もう一度押すとやめる）→ 枠を押すと入る。
/// 棚のスクロールはホイールとスクロールバーで行う（カードのドラッグは上の枠へ運ぶ動きに使うため、スクロールには回さない）。
/// カードは非表示の Template を ProphetView が複製して使い回す。
/// </summary>
public sealed class ProphetItemCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    [SerializeField] private Button button;
    [Tooltip("持ち上げるときに複製してゴーストにする部分（カードの面）")]
    [SerializeField] private RectTransform face;
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("新聞の向き（新聞 ▲ / ―）")]
    [SerializeField] private TextMeshProUGUI newsText;
    [Tooltip("値段・在庫")]
    [SerializeField] private TextMeshProUGUI infoText;
    [Tooltip("前日比（▲5% など）")]
    [SerializeField] private TextMeshProUGUI changeText;
    [Tooltip("枠に入っている品の「効果中」の札")]
    [SerializeField] private GameObject inUseTag;
    [Tooltip("枠に入っている・持ち上げ中に重ねる幕")]
    [SerializeField] private Image dimOverlay;
    [Tooltip("選んでいる品の金の縁")]
    [SerializeField] private GameObject selectedFrame;

    public static readonly Color UpColor = new(0.31f, 0.62f, 0.24f, 1f);
    public static readonly Color DownColor = new(0.78f, 0.31f, 0.23f, 1f);
    private static readonly Color Muted = new(0.48f, 0.36f, 0.27f, 1f);

    public Button Button => button;
    public RectTransform Face => face != null ? face : (RectTransform)transform;
    public string ItemId { get; private set; } = string.Empty;
    public bool InUse { get; private set; }
    public TextMeshProUGUI InfoText => infoText;

    public event Action<ProphetItemCard, PointerEventData> DragStarted;
    public event Action<ProphetItemCard, PointerEventData> Dragging;
    public event Action<ProphetItemCard, PointerEventData> DragEnded;

    private bool selected;
    private bool carried;
    private ScrollRect parentScroll;

    public void Bind(in ProphetItemInfo info)
    {
        ItemId = info.ItemId ?? string.Empty;
        InUse = info.InSlot != 0;
        gameObject.name = $"Item_{ItemId}";
        if (icon != null)
        {
            icon.sprite = info.Icon;
            icon.enabled = info.Icon != null;
            icon.preserveAspect = true;
        }
        if (nameText != null) nameText.text = info.Name;
        if (newsText != null)
        {
            newsText.text = info.NewsDirection > 0 ? "新聞 ▲" : info.NewsDirection < 0 ? "新聞 ▼" : "新聞 ―";
            newsText.color = info.NewsDirection > 0 ? UpColor : info.NewsDirection < 0 ? DownColor : Muted;
        }
        if (infoText != null) infoText.text = $"{info.Price:N0}G・在庫{info.Stock}";
        if (changeText != null)
        {
            int pct = Mathf.RoundToInt(info.Change1d * 100f);
            changeText.text = pct > 0 ? $"前日 ▲{pct}%" : pct < 0 ? $"前日 ▼{-pct}%" : "前日 ―";
            changeText.color = pct > 0 ? UpColor : pct < 0 ? DownColor : Muted;
        }
        if (inUseTag != null) inUseTag.SetActive(InUse);
        selected = false;
        carried = false;
        Apply();
    }

    public void SetSelected(bool on)
    {
        selected = on;
        Apply();
    }

    public void SetCarried(bool on)
    {
        carried = on;
        Apply();
    }

    private void Apply()
    {
        if (selectedFrame != null) selectedFrame.SetActive(selected);
        if (face != null) face.anchoredPosition = new Vector2(face.anchoredPosition.x, selected ? 10f : 0f);
        if (dimOverlay == null) return;
        float a = InUse ? 0.55f : carried ? 0.45f : 0f;
        dimOverlay.gameObject.SetActive(a > 0f);
        var c = dimOverlay.color;
        c.a = a;
        dimOverlay.color = c;
    }

    public void OnBeginDrag(PointerEventData eventData) => DragStarted?.Invoke(this, eventData);
    public void OnDrag(PointerEventData eventData) => Dragging?.Invoke(this, eventData);
    public void OnEndDrag(PointerEventData eventData) => DragEnded?.Invoke(this, eventData);

    private ScrollRect Scroll => parentScroll != null ? parentScroll : parentScroll = GetComponentInParent<ScrollRect>();
    public void OnScroll(PointerEventData eventData) { if (Scroll != null) Scroll.OnScroll(eventData); }
}
