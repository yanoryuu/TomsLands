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
    [SerializeField] private TurnAnnounceView turnAnnounceView;
    [SerializeField] private BuzzAnnounceView buzzAnnounceView;

    [Header("バズ中演出")]
    [SerializeField] private BuzzModeOverlayView buzzModeOverlayView;

    [Header("机の陳列")]
    [SerializeField] private ShopDeskDisplay shopDeskDisplay;

    [Header("借金情報")]
    [SerializeField] private TextMeshProUGUI nextDebtText;
    [SerializeField] private Button debtPaymentButton;
    
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
    //借金返済パネルを開く
    public Subject<Unit> OnDebtPaymentClicked { get; } = new();

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
        if (debtPaymentButton != null)
            debtPaymentButton.onClick.AddListener(() => OnDebtPaymentClicked.OnNext(Unit.Default));
    }

    public void Initialize()
    {

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
    /// 次回借金返済額と残りターン数をショップ画面内に表示する
    /// </summary>
    public void UpdateNextDebt(int amount, int remainingTurns)
    {
        if (nextDebtText == null) return;
        nextDebtText.text = $"次回返済\n{amount:#,0}G\nあと{remainingTurns}ターン";
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
