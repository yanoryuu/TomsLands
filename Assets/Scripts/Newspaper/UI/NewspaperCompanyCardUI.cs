using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 左ペインの新聞社カード1枚（Docs/News_Spec.md §10.2）。
/// 紋章・社名・料金/契約ターン・信頼/速報/網羅・購読中リボン or 購読するボタン。
/// <b>購読中カードのクリックが社の切替を兼ねる</b>。
/// </summary>
public class NewspaperCompanyCardUI : MonoBehaviour
{
    public struct Data
    {
        public string companyId;
        public string companyName;
        public Sprite emblem;
        public int price;
        public int contractTurns;
        public int trust;
        public int speed;
        public int coverage;
        public float measuredAccuracy;  // -1 = 確認実績なし
        public bool subscribed;
        public bool selected;
        public bool canSubscribe;
    }

    [SerializeField] private Image background;
    [SerializeField] private Sprite normalSprite;      // 2−0社カード（未購読）
    [SerializeField] private Sprite subscribedSprite;  // 2−1社カード（購読中）
    [SerializeField] private Image emblemImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private NewsRatingBarUI trustBar;
    [SerializeField] private NewsRatingBarUI speedBar;
    [SerializeField] private NewsRatingBarUI coverageBar;
    [Tooltip("信頼バーの下に重ねる実測バー（任意）。fillAmount = 実測的中率")]
    [SerializeField] private Image measuredFill;
    [SerializeField] private GameObject subscribedRibbon;  // 5購読中リボン
    [SerializeField] private Button subscribeButton;       // 4購読する
    [SerializeField] private GameObject selectedMark;      // 読んでいる社の強調（任意）
    [SerializeField] private Button rootButton;
    [SerializeField] private CanvasGroup canvasGroup;      // 未購読カードを少し沈める（任意）
    [Tooltip("購読状態で色を切り替える文字（社名・ラベル）。未購読の暗いカードでは明るい色にする")]
    [SerializeField] private TextMeshProUGUI[] stateTexts;
    [SerializeField] private Color unsubscribedTextColor = new Color32(0xF7, 0xEB, 0xD2, 0xFF);
    [SerializeField] private Color subscribedTextColor = new Color32(0x4A, 0x2E, 0x1E, 0xFF);

    public string CompanyId { get; private set; }
    private bool wasSelected;
    public event Action<string> OnSelectClicked;
    public event Action<string> OnSubscribeClicked;

    private void Awake()
    {
        if (rootButton != null)
            rootButton.onClick.AddListener(() => OnSelectClicked?.Invoke(CompanyId));
        if (subscribeButton != null)
            subscribeButton.onClick.AddListener(() => OnSubscribeClicked?.Invoke(CompanyId));
    }

    public void SetData(Data d)
    {
        CompanyId = d.companyId;

        if (background != null)
        {
            var sprite = d.subscribed ? subscribedSprite : normalSprite;
            if (sprite != null) background.sprite = sprite;
        }
        if (emblemImage != null)
        {
            emblemImage.sprite = d.emblem;
            emblemImage.enabled = d.emblem != null;
        }
        if (nameText != null) nameText.text = d.companyName;
        if (priceText != null) priceText.text = $"{d.price:N0}G / {d.contractTurns}ターン";

        trustBar?.SetValue(d.trust);
        speedBar?.SetValue(d.speed);
        coverageBar?.SetValue(d.coverage);

        if (measuredFill != null)
        {
            bool has = d.measuredAccuracy >= 0f;
            measuredFill.gameObject.SetActive(has);
            if (has) measuredFill.fillAmount = d.measuredAccuracy;
        }

        if (subscribedRibbon != null) subscribedRibbon.SetActive(d.subscribed);
        if (subscribeButton != null)
        {
            subscribeButton.gameObject.SetActive(!d.subscribed);
            subscribeButton.interactable = d.canSubscribe;
        }
        if (selectedMark != null)
        {
            selectedMark.SetActive(d.selected);
            if (d.selected && !wasSelected) UIFx.Pop(selectedMark.transform, 1.08f, 0.2f);
        }
        wasSelected = d.selected;
        if (rootButton != null) rootButton.interactable = d.subscribed;
        if (canvasGroup != null) canvasGroup.alpha = d.subscribed ? 1f : 0.85f;
        if (stateTexts != null)
            foreach (var t in stateTexts)
                if (t != null) t.color = d.subscribed ? subscribedTextColor : unsubscribedTextColor;
    }
}
