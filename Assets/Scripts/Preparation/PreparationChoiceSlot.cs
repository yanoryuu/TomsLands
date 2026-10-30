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
                if (string.IsNullOrEmpty(Id)) return;
                UIFx.Pop(transform);
                OnSelected.OnNext(Id);
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
        if (nameText != null)
        {
            PreparationView.FitText(nameText);
            nameText.text = displayName;
        }
        if (infoText != null)
        {
            // 効果説明はスロット枠からはみ出しやすいため折り返し＋自動縮小を強制する
            PreparationView.FitText(infoText);
            infoText.text = info ?? string.Empty;
            infoText.gameObject.SetActive(!string.IsNullOrEmpty(info));
        }
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
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
        // #8 選んでいる札のアイコンだけゆっくり上下させる（アイコンは札の直下でレイアウトに入っていない）
        if (iconImage != null && Application.isPlaying)
        {
            if (on && iconImage.enabled) { if (!iconImage.TryGetComponent<IdleBob>(out _)) IdleBob.Attach(iconImage.rectTransform, 5f, 2.2f); }
            else IdleBob.Detach(iconImage.rectTransform);
        }
    }

    /// <summary>アイコンの RectTransform（光・アイドルを付ける先）。未配線なら null。</summary>
    public RectTransform IconRect => iconImage != null ? iconImage.rectTransform : null;

    /// <summary>
    /// #11 めくりの裏面: 絵と文字を隠す／戻す（札の枠だけ残る）。表示・押せる状態は変えず、描画だけ切り替える。
    /// 戻すときは Setup で決めた見え方（アイコンは絵がある時だけ）に戻す。
    /// </summary>
    public void SetFaceVisible(bool visible)
    {
        if (iconImage != null) iconImage.enabled = visible && iconImage.sprite != null;
        if (nameText != null) nameText.enabled = visible;
        if (infoText != null) infoText.enabled = visible;
        if (countText != null) countText.enabled = visible;
    }
}
