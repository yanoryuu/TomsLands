using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using TMPro;

public class AvailableItemSlot : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private TextMeshProUGUI attributeText;
    [Tooltip("今の値段（未配線なら出さない）")]
    [SerializeField] private TextMeshProUGUI priceText;

    public string ItemId { get; private set; }

    /// <summary>属性の1文字（有利・不利のタグを後ろに付けるために覚えておく）。</summary>
    private string attributeLabel = string.Empty;

    public event Action<string> OnItemSelected;

    private void Awake()
    {
        // パッド/キーで選べるようにする（A で左クリックと同じ OnPointerClick を転送。マウス時は何もしない）
        if (!TryGetComponent<PointerEventBridge>(out _)) gameObject.AddComponent<PointerEventBridge>();
        // 選択枠はカードの見た目（BackGround）に合わせる（ルートの矩形はレイアウト用で見た目とずれている）
        var background = transform.Find("BackGround") as RectTransform;
        if (background != null && GetComponent<UIFocusHighlightOverride>() == null)
            gameObject.AddComponent<UIFocusHighlightOverride>().target = background;
    }

    /// <summary>アイテムID、アイコン、表示名、在庫数、属性を設定。</summary>
    public void Initialize(string itemId, Sprite sprite, string displayName, int stock, ItemTypeData.ItemAttribute attribute)
    {
        ItemId = itemId;
        icon.sprite = sprite;
        if (nameText != null) nameText.text = displayName;
        if (stockText != null) stockText.text = $"在庫: {stock}";
        attributeLabel = ItemAttributeText.ToJapanese(attribute);
        if (attributeText != null) attributeText.text = attributeLabel;
    }

    /// <summary>
    /// 読みの材料: 今の値段と、行き先の敵に対する有利・不利。
    /// 配信中の値動きは敵の属性ごとに「有利な品は上がる・不利な品は下がる」ので、その向きを先に見せる。
    /// 情報を買っていないときは「？」（情報屋で買う価値を見せる）。
    /// </summary>
    public void SetReadInfo(int price, AttributeMatchup matchup)
    {
        if (priceText != null) priceText.text = $"{price:N0}G";

        string tag = MatchupTag(matchup);
        if (attributeText != null)
            attributeText.text = string.IsNullOrEmpty(tag) ? attributeLabel : $"{attributeLabel} {tag}";
    }

    private static string MatchupTag(AttributeMatchup matchup) => matchup switch
    {
        AttributeMatchup.Advantage    => "<color=#E8A33D>有利↑</color>",
        AttributeMatchup.Disadvantage => "<color=#8FB7D9>不利↓</color>",
        AttributeMatchup.Unknown      => "？",
        _                             => "",
    };

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            OnItemSelected?.Invoke(ItemId);
    }
}

/// <summary>品出しの品と、行き先の敵の属性の相性（StreamingSettingPresenter が判定する）。</summary>
public enum AttributeMatchup
{
    /// <summary>行き先の情報を買っていない。</summary>
    Unknown = 0,
    /// <summary>有利な敵と不利な敵が同じ数（または相性の無い品）。</summary>
    Neutral = 1,
    /// <summary>有利な敵のほうが多い（倒すたびに値上がりしやすい）。</summary>
    Advantage = 2,
    /// <summary>不利な敵のほうが多い（倒すたびに値下がりしやすい）。</summary>
    Disadvantage = 3,
}