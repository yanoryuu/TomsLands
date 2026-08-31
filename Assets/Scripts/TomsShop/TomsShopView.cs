using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TomsShopView : MonoBehaviour
{

    [SerializeField] private Button BlaskSmithButton;
    [SerializeField] private Button HeroButton;
    [SerializeField] private Button SetItemButton;
    [SerializeField] private Button InfoButton;
    [SerializeField] private Button ToolButton;
    [SerializeField] private Button StartShopButton;
    [SerializeField] private Button MapButton;
    [SerializeField] private Button DungeonLevelUpButton;
    [SerializeField] private Button AdvertisementButton;
    [SerializeField] private Button ProphetButton;
    [SerializeField] private Button ShopUpgradeButton;
    [SerializeField] private Button MachineShopButton;
    [SerializeField] private TurnAnnounceView turnAnnounceView;
    [SerializeField] private BuzzAnnounceView buzzAnnounceView;

    [Header("バズ中演出")]
    [SerializeField] private BuzzModeOverlayView buzzModeOverlayView;

    [Header("机の陳列")]
    [SerializeField] private ShopDeskDisplay shopDeskDisplay;

    [Header("設置マシンの表示 ※未配線でも動作する")]
    [SerializeField] private ShopMachineDisplay shopMachineDisplay;

    [Header("税金情報")]
    [SerializeField] private TextMeshProUGUI nextDebtText;
    [SerializeField] private Button debtPaymentButton;

    [Header("朝レポート ※未配線でも動作する")]
    [SerializeField] private GameObject morningReportPanel;
    [SerializeField] private TextMeshProUGUI morningReportText;
    [SerializeField] private Button morningReportCloseButton;

    [Header("レリック獲得3択 ※未配線でも動作する")]
    [SerializeField] private GameObject relicChoicePanel;
    [SerializeField] private Button[] relicChoiceButtons;
    [SerializeField] private TextMeshProUGUI[] relicChoiceNameTexts;
    [SerializeField] private TextMeshProUGUI[] relicChoiceDescTexts;
    [SerializeField] private Button relicChoiceSkipButton;

    [Header("所持レリックバー ※未配線でも動作する")]
    [SerializeField] private TextMeshProUGUI relicBarText;
    
    //鍛冶屋を開く
    public Subject<Unit> OnBlacksmithClicked { get; } = new();
    public Subject<Unit> OnHeroClicked { get; } = new();
    //商品を陳列
    public Subject<Unit> OnSetItemClicked { get; } = new();
    //情報屋を開く
    public Subject<Unit> OnInfoClicked { get; } = new();
    //道具屋を開く
    public Subject<Unit> OnToolClicked { get; } = new();
    //営業開始
    public Subject<Unit> OnStartShopClicked { get; } = new();
    //マップ画面を開く
    public Subject<Unit> OnMapClicked { get; } = new();
    //ダンジョンレベルアップ画面を開く
    public Subject<Unit> OnDungeonLevelUpClicked { get; } = new();
    //広告購入画面を開く
    public Subject<Unit> OnAdvertisementClicked { get; } = new();
    //預言者画面を開く
    public Subject<Unit> OnProphetClicked { get; } = new();

    public Subject<Unit> OnShopUpgradeClicked { get; } = new();
    //マシンショップ（店カスタマイズ）画面を開く
    public Subject<Unit> OnMachineShopClicked { get; } = new();
    //借金返済パネルを開く
    public Subject<Unit> OnDebtPaymentClicked { get; } = new();
    //レリック3択の選択（index）とスキップ
    public Subject<int> OnRelicChoiceSelected { get; } = new();
    public Subject<Unit> OnRelicChoiceSkipped { get; } = new();

    /// <summary>
    /// レリック獲得3択を表示する。パネル未配線なら false を返す
    /// （呼び出し側が自動獲得にフォールバック）。
    /// </summary>
    public bool ShowRelicChoices(List<(string name, string description)> choices)
    {
        if (relicChoicePanel == null || relicChoiceButtons == null || relicChoiceButtons.Length == 0)
            return false;

        for (int i = 0; i < relicChoiceButtons.Length; i++)
        {
            bool hasChoice = i < choices.Count;
            if (relicChoiceButtons[i] != null)
                relicChoiceButtons[i].gameObject.SetActive(hasChoice);
            if (!hasChoice) continue;

            if (relicChoiceNameTexts != null && i < relicChoiceNameTexts.Length && relicChoiceNameTexts[i] != null)
                relicChoiceNameTexts[i].text = choices[i].name;
            if (relicChoiceDescTexts != null && i < relicChoiceDescTexts.Length && relicChoiceDescTexts[i] != null)
                relicChoiceDescTexts[i].text = choices[i].description;
        }

        relicChoicePanel.SetActive(true);
        return true;
    }

    public void HideRelicChoices()
    {
        if (relicChoicePanel != null)
            relicChoicePanel.SetActive(false);
    }

    /// <summary>所持レリックバーの表示を更新する。未配線なら何もしない。</summary>
    public void UpdateRelicBar(IReadOnlyList<string> relicNames)
    {
        if (relicBarText == null) return;
        relicBarText.text = relicNames == null || relicNames.Count == 0
            ? ""
            : $"レリック: {string.Join(" / ", relicNames)}";
    }

    /// <summary>設置済みマシンの見た目を更新する。未配線なら何もしない。</summary>
    public void RefreshMachineDisplay(ShopMachineModel machineModel)
    {
        if (shopMachineDisplay != null)
            shopMachineDisplay.RefreshDisplay(machineModel);
    }

    /// <summary>
    /// 朝レポートを表示する。パネル未配線なら false を返す（呼び出し側がログにフォールバック）。
    /// </summary>
    public bool ShowMorningReport(string text)
    {
        if (morningReportPanel == null || morningReportText == null) return false;
        morningReportText.text = text;
        morningReportPanel.SetActive(true);
        return true;
    }

    public void Awake()
    {
        BlaskSmithButton.onClick.AddListener(() => OnBlacksmithClicked.OnNext(Unit.Default));
        if (HeroButton != null)
            HeroButton.onClick.AddListener(() => OnHeroClicked.OnNext(Unit.Default));
        SetItemButton.onClick.AddListener(() => OnSetItemClicked.OnNext(Unit.Default));
        InfoButton.onClick.AddListener(() => OnInfoClicked.OnNext(Unit.Default));
        ToolButton.onClick.AddListener(() => OnToolClicked.OnNext(Unit.Default));
        StartShopButton.onClick.AddListener(() => OnStartShopClicked.OnNext(Unit.Default));
        MapButton.onClick.AddListener(() => OnMapClicked.OnNext(Unit.Default));
        DungeonLevelUpButton.onClick.AddListener(() => OnDungeonLevelUpClicked.OnNext(Unit.Default));
        if (AdvertisementButton != null)
            AdvertisementButton.onClick.AddListener(() => OnAdvertisementClicked.OnNext(Unit.Default));
        if (ProphetButton != null)
            ProphetButton.onClick.AddListener(() => OnProphetClicked.OnNext(Unit.Default));
        if (ShopUpgradeButton != null)
            ShopUpgradeButton.onClick.AddListener(() => OnShopUpgradeClicked.OnNext(Unit.Default));
        if (MachineShopButton != null)
            MachineShopButton.onClick.AddListener(() => OnMachineShopClicked.OnNext(Unit.Default));
        if (morningReportCloseButton != null && morningReportPanel != null)
            morningReportCloseButton.onClick.AddListener(() => morningReportPanel.SetActive(false));

        if (relicChoiceButtons != null)
        {
            for (int i = 0; i < relicChoiceButtons.Length; i++)
            {
                int index = i;
                if (relicChoiceButtons[i] != null)
                    relicChoiceButtons[i].onClick.AddListener(() => OnRelicChoiceSelected.OnNext(index));
            }
        }
        if (relicChoiceSkipButton != null)
            relicChoiceSkipButton.onClick.AddListener(() => OnRelicChoiceSkipped.OnNext(Unit.Default));
        if (debtPaymentButton != null)
            debtPaymentButton.onClick.AddListener(() => OnDebtPaymentClicked.OnNext(Unit.Default));
    }

    public void Initialize()
    {

    }

    /// <summary>営業開始ボタンの押下可否を切り替える（演出中の連打防止用）。</summary>
    public void SetStartShopInteractable(bool interactable)
    {
        if (StartShopButton != null)
            StartShopButton.interactable = interactable;
    }

    public void RefreshDeskDisplay(List<RuntimeItemData> runtimeItems)
    {
        shopDeskDisplay?.RefreshDisplay(runtimeItems);
    }


    /// <summary>
    /// ターン切り替え演出を再生する（左→右にスライド）
    /// </summary>
    public void ShowTurnAnnounce(int turn)
    {
        turnAnnounceView.Show(turn);
    }

    /// <summary>
    /// 次回税金の納付額と残りターン数をショップ画面内に表示する
    /// </summary>
    public void UpdateNextDebt(int amount, int remainingTurns)
    {
        if (nextDebtText == null) return;
        nextDebtText.text = $"次回納税\n{amount:#,0}G\nあと{remainingTurns}ターン";
    }

    /// <summary>
    /// バズ発生演出を再生する。
    /// ターン開始時にバズが発生した場合に呼び出される。
    /// </summary>
    /// <param name="buzzType">発生したバズの種類</param>
    public void ShowBuzzAnnounce(BuzzType buzzType)
    {
        if (buzzAnnounceView != null)
            buzzAnnounceView.ShowBuzzOccurred(buzzType);
    }

    /// <summary>
    /// バズ終了演出を再生する。
    /// ターン開始時にバズが終了した場合に呼び出される。
    /// </summary>
    /// <param name="endedBuzzType">終了したバズの種類</param>
    public void ShowBuzzEndedAnnounce(BuzzType endedBuzzType)
    {
        if (buzzAnnounceView != null)
            buzzAnnounceView.ShowBuzzEnded(endedBuzzType);
    }

    /// <summary>
    /// バズ中の常時演出（バズモードオーバーレイ）の表示/非表示を切り替える。
    /// バズ発生中はフレーム＋バナーを表示し続ける。
    /// </summary>
    public void SetBuzzModeActive(bool isActive, BuzzType buzzType)
    {
        if (buzzModeOverlayView == null) return;

        if (isActive)
            buzzModeOverlayView.Show(buzzType);
        else
            buzzModeOverlayView.Hide();
    }

    /// <summary>
    /// バズの残りターン数表示を更新する。
    /// </summary>
    public void UpdateBuzzRemainingTurns(int remainingTurns)
    {
        buzzModeOverlayView?.UpdateRemainingTurns(remainingTurns);
    }
}
