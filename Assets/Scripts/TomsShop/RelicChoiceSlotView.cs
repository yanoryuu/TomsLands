using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// レリック獲得3択のカード1枚（アイコン・名前・効果説明）。
/// 押下の通知は親（TomsShopView）が Button を直接購読する。ここは見た目だけを持つ。
/// ホバー/パッド選択中は金枠を出して少し拡大する。参照は未配線（null）でも動作する。
/// </summary>
public class RelicChoiceSlotView : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Image iconImage;
    [Tooltip("アイコン背景。レア度色をうっすら乗せる")]
    [SerializeField] private Image iconBackImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [Tooltip("ホバー/選択中だけ表示する金枠")]
    [SerializeField] private GameObject highlight;

    [Tooltip("ホバー/選択中の拡大率")]
    [SerializeField] private float hoverScale = 1.04f;

    // 名前の基本色（コモン）。カード地（ベージュ）上で読める焦げ茶
    private static readonly Color BaseNameColor = new(0.353f, 0.227f, 0.141f);
    private static readonly Color IconBackBaseColor = Color.white;

    public TextMeshProUGUI NameText => nameText;
    public TextMeshProUGUI DescText => descText;

    /// <summary>カードの中身を差し替える。rarityColor は null でコモン扱い（色を乗せない）。</summary>
    public void Setup(string displayName, string description, Sprite icon, Color? rarityColor)
    {
        if (nameText != null)
        {
            nameText.text = displayName ?? string.Empty;
            // レア度はさりげなく: 名前を焦げ茶とレア度色の中間にする
            nameText.color = rarityColor.HasValue
                ? Color.Lerp(BaseNameColor, rarityColor.Value, 0.65f)
                : BaseNameColor;
        }
        if (descText != null)
        {
            // 説明文の枠超え防止: 折り返し＋自動縮小、それでも溢れたら省略
            descText.textWrappingMode = TextWrappingModes.Normal;
            descText.overflowMode = TextOverflowModes.Ellipsis;
            descText.enableAutoSizing = true;
            descText.text = description ?? string.Empty;
        }
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        if (iconBackImage != null)
        {
            iconBackImage.color = rarityColor.HasValue
                ? Color.Lerp(IconBackBaseColor, rarityColor.Value, 0.3f)
                : IconBackBaseColor;
        }
        SetHovered(false);
    }

    public void OnPointerEnter(PointerEventData eventData) => SetHovered(true);
    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);
    public void OnSelect(BaseEventData eventData) => SetHovered(true);
    public void OnDeselect(BaseEventData eventData) => SetHovered(false);

    private void OnDisable()
    {
        // 非表示の間にホバー状態が残らないよう戻す（次回表示時に枠が出っぱなしにならない）
        transform.DOKill();
        transform.localScale = Vector3.one;
        if (highlight != null) highlight.SetActive(false);
    }

    private void SetHovered(bool on)
    {
        if (highlight != null) highlight.SetActive(on);
        if (!gameObject.activeInHierarchy) return;
        transform.DOKill();
        transform.DOScale(on ? hoverScale : 1f, 0.12f).SetEase(Ease.OutCubic).SetLink(gameObject);
    }
}
