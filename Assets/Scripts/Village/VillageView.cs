using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 村シーンのView（歩ける村のHUD・投資パネル・帰還収支ポップ）。
/// 参照は全てnull-safe。departButton が未配線の間は IsInteractiveReady=false となり、
/// Presenter が PreparationScene へ素通りする（既存規約）。
/// </summary>
public class VillageView : MonoBehaviour
{
    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI villageFundsText;
    [SerializeField] private TextMeshProUGUI metaCurrencyText;
    [SerializeField] private TextMeshProUGUI villageLevelText;
    [SerializeField] private Button departButton;     // 出撃準備へ（必須。未配線なら素通り）
    [SerializeField] private Button titleButton;      // タイトルへ
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("施設区画（シーン手置き）")]
    [SerializeField] private FacilityPlot[] plots;

    [Header("投資パネル")]
    [SerializeField] private GameObject investPanel;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TextMeshProUGUI detailNameText;
    [SerializeField] private TextMeshProUGUI detailLevelText;
    [SerializeField] private TextMeshProUGUI currentEffectText;
    [SerializeField] private TextMeshProUGUI nextEffectText;
    [SerializeField] private TextMeshProUGUI detailCostText;
    [SerializeField] private Button investButton;
    [SerializeField] private TextMeshProUGUI investButtonLabel;
    [SerializeField] private Button investCloseButton;

    [Header("帰還収支ポップ")]
    [SerializeField] private GameObject conversionPopup;
    [SerializeField] private TextMeshProUGUI conversionTitleText;
    [SerializeField] private TextMeshProUGUI conversionEarnedText;
    [SerializeField] private TextMeshProUGUI conversionConvertedText;
    [SerializeField] private Button conversionCloseButton;

    public Subject<Unit> OnDepart { get; } = new();
    public Subject<Unit> OnGoTitle { get; } = new();
    public Subject<Unit> OnInvest { get; } = new();
    public Subject<Unit> OnPanelClosed { get; } = new();
    public Subject<Unit> OnConversionClosed { get; } = new();

    public IReadOnlyList<FacilityPlot> Plots => plots ?? System.Array.Empty<FacilityPlot>();

    /// <summary>出撃ボタンが配線済みか（falseならPresenterが準備シーンへ素通りする）。</summary>
    public bool IsInteractiveReady => departButton != null;

    private void Awake()
    {
        if (departButton != null)
            departButton.onClick.AddListener(() => OnDepart.OnNext(Unit.Default));
        if (titleButton != null)
            titleButton.onClick.AddListener(() => OnGoTitle.OnNext(Unit.Default));
        if (investButton != null)
            investButton.onClick.AddListener(() => OnInvest.OnNext(Unit.Default));
        if (investCloseButton != null)
            investCloseButton.onClick.AddListener(() =>
            {
                HideInvestPanel();
                OnPanelClosed.OnNext(Unit.Default);
            });
        if (conversionCloseButton != null)
            conversionCloseButton.onClick.AddListener(() =>
            {
                if (conversionPopup != null) conversionPopup.SetActive(false);
                OnConversionClosed.OnNext(Unit.Default);
            });

        if (investPanel != null) investPanel.SetActive(false);
        if (conversionPopup != null) conversionPopup.SetActive(false);
    }

    public void UpdateHud(int villageFunds, int metaCurrency, int villageLevel)
    {
        if (villageFundsText != null) villageFundsText.text = $"村資金 {villageFunds:N0}G";
        if (metaCurrencyText != null) metaCurrencyText.text = $"信用 {metaCurrency:N0}";
        if (villageLevelText != null) villageLevelText.text = $"トムの村（総合Lv{villageLevel}）";
    }

    public void ShowMessage(string message)
    {
        if (messageText != null) messageText.text = message;
    }

    /// <summary>投資パネルを表示する。</summary>
    public void ShowInvestPanel(VillageFacilityData facility, int level, string currentEffect, string nextEffect,
        int cost, bool canInvest, string blockedReason)
    {
        if (investPanel == null) return;
        investPanel.SetActive(true);

        if (detailIcon != null)
        {
            detailIcon.sprite = facility.icon;
            detailIcon.enabled = facility.icon != null;
        }
        if (detailNameText != null) detailNameText.text = facility.facilityName;
        if (detailLevelText != null)
        {
            detailLevelText.text = level >= facility.MaxLevel
                ? $"Lv{level}（MAX）"
                : (level == 0 ? "未建設" : $"Lv{level} → Lv{level + 1}");
        }
        if (currentEffectText != null)
            currentEffectText.text = string.IsNullOrEmpty(currentEffect) ? "-" : currentEffect;
        if (nextEffectText != null)
            nextEffectText.text = string.IsNullOrEmpty(nextEffect) ? "これ以上の拡張はない" : nextEffect;

        if (detailCostText != null)
        {
            if (cost < 0) detailCostText.text = "-";
            else detailCostText.text = $"{cost:N0}G";
        }

        if (investButton != null) investButton.interactable = canInvest;
        if (investButtonLabel != null)
        {
            if (cost < 0) investButtonLabel.text = "最大レベル";
            else investButtonLabel.text = canInvest ? (level == 0 ? "建設する" : "拡張する") : blockedReason;
        }
    }

    public void HideInvestPanel()
    {
        if (investPanel != null) investPanel.SetActive(false);
    }

    public bool IsInvestPanelOpen => investPanel != null && investPanel.activeSelf;

    /// <summary>帰還収支ポップを表示する（村資金のカウントアップ演出付き）。</summary>
    public void ShowConversionPopup(bool cleared, int earned, int converted)
    {
        if (conversionPopup == null) return;
        conversionPopup.SetActive(true);

        if (conversionTitleText != null)
            conversionTitleText.text = cleared ? "ランクリア！ 村への還元" : "破産…… それでも村は待っている";
        if (conversionEarnedText != null)
            conversionEarnedText.text = cleared
                ? $"今回の稼ぎ {earned:N0}G"
                : $"手元に残った {earned:N0}G";

        if (conversionConvertedText != null)
        {
            int shown = 0;
            conversionConvertedText.text = "村へ +0G";
            DOTween.To(() => shown, x =>
                {
                    shown = x;
                    conversionConvertedText.text = $"村へ +{x:N0}G";
                }, converted, 0.8f)
                .SetEase(Ease.OutCubic)
                .SetLink(conversionConvertedText.gameObject);
        }
    }
}
