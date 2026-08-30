using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 準備シーンの汎用選択スロット（持ち込みアイテム / スターターレリック共用）。
/// 持ち込み: +/- で個数を変える。レリック: 本体クリックで単一選択（highlight表示）。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class PreparationChoiceSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI infoText;   // 効果・属性・価格などの説明行
    [SerializeField] private Button selectButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private GameObject highlight;

    /// <summary>本体クリック（持ち込み: +1 / レリック: 選択）。</summary>
    public Subject<string> OnSelected { get; } = new();
    public Subject<string> OnMinus { get; } = new();

    public string Id { get; private set; }

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(Id)) OnSelected.OnNext(Id);
            });
        if (minusButton != null)
            minusButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(Id)) OnMinus.OnNext(Id);
            });
    }

    public void Setup(string id, string displayName, Sprite icon, bool showMinus, string info = null)
    {
        Id = id;
        if (nameText != null) nameText.text = displayName;
        if (infoText != null)
        {
            infoText.text = info ?? string.Empty;
            infoText.gameObject.SetActive(!string.IsNullOrEmpty(info));
        }
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        if (minusButton != null) minusButton.gameObject.SetActive(showMinus);
        SetCount(0);
        SetHighlighted(false);
    }

    public void SetCount(int count)
    {
        if (countText != null)
        {
            countText.gameObject.SetActive(count > 0);
            countText.text = $"×{count}";
        }
    }

    public void SetHighlighted(bool on)
    {
        if (highlight != null) highlight.SetActive(on);
    }
}
