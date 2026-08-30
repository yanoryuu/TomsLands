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

    [Header("所持レリックバー（アイコン列） ※未配線でも動作する")]
    [Tooltip("レリックアイコンの親（HorizontalLayoutGroup推奨）。クリックで説明ポップアップ")]
    [SerializeField] private Transform relicBarParent;
    [Tooltip("アイコン1個のサイズ(px)")]
    [SerializeField] private float relicIconSize = 56f;


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
    //レリックバーのアイコンクリック（relicId）
    public Subject<string> OnRelicIconClicked { get; } = new();

    /// <summary>
    /// レリック獲得3択を表示する。パネル未配線なら false を返す
    /// （呼び出し側が自動獲得にフォールバック）。
    /// </summary>
    public bool ShowRelicChoices(List<(string name, string description)> choices, string skipLabel = null)
    {
        if (relicChoicePanel == null || relicChoiceButtons == null || relicChoiceButtons.Length == 0)
            return false;

        // 辞退ボタンの文言（「辞退して◯◯Gもらう」等）を差し替える
        if (!string.IsNullOrEmpty(skipLabel) && relicChoiceSkipButton != null)
        {
            var label = relicChoiceSkipButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = skipLabel;
        }

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

    /// <summary>
    /// 所持レリックバー（アイコン列）を再構築する。未配線なら何もしない。
    /// アイコン未設定のレリックはレア度色の丸+頭文字でフォールバック表示する。
    /// クリックで OnRelicIconClicked（説明ポップアップは Presenter 側）。
    /// </summary>
    public void UpdateRelicBar(IReadOnlyList<RelicDefinition> relics)
    {
        if (relicBarParent == null) return;

        for (int i = relicBarParent.childCount - 1; i >= 0; i--)
        {
            Destroy(relicBarParent.GetChild(i).gameObject);
        }
        if (relics == null) return;

        foreach (var relic in relics)
        {
            if (relic == null) continue;
            var iconObj = new GameObject($"Relic_{relic.relicId}", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = iconObj.GetComponent<RectTransform>();
            rt.SetParent(relicBarParent, false);
            rt.sizeDelta = new Vector2(relicIconSize, relicIconSize);

            var img = iconObj.GetComponent<Image>();
            if (relic.icon != null)
            {
                img.sprite = relic.icon;
                img.color = Color.white;
            }
            else
            {
                // アイコン未設定: レア度色の板 + 頭文字（本番アートで置き換わる前提のフォールバック）
                img.color = RarityColor(relic.rarity, relic.isCurse);

                var label = new GameObject("Label", typeof(RectTransform));
                var lrt = label.GetComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
                lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
                var tmp = label.AddComponent<TextMeshProUGUI>();
                tmp.text = string.IsNullOrEmpty(relic.relicName) ? "?" : relic.relicName.Substring(0, 1);
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 10f; tmp.fontSizeMax = relicIconSize * 0.6f;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
            }

            string capturedId = relic.relicId;
            iconObj.GetComponent<Button>().onClick.AddListener(() => OnRelicIconClicked.OnNext(capturedId));
        }
    }

    private static Color RarityColor(RelicRarity rarity, bool isCurse)
    {
        if (isCurse) return new Color(0.45f, 0.15f, 0.35f);
        return rarity switch
        {
            RelicRarity.Epic => new Color(0.55f, 0.35f, 0.75f),
            RelicRarity.Rare => new Color(0.25f, 0.5f, 0.8f),
            _ => new Color(0.5f, 0.55f, 0.6f),
        };
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
