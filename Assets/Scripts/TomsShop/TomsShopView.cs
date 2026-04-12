using R3;
using UnityEngine;
using UnityEngine.UI;

public class TomsShopView : MonoBehaviour
{
    
    [SerializeField] private Button BlaskSmithButton;
    [SerializeField] private Button SetItemButton;
    [SerializeField] private Button InfoButton;
    [SerializeField] private Button ToolButton;
    [SerializeField] private Button StartShopButton;
    [SerializeField] private Button MapButton;
    [SerializeField] private Button NextTurnButton;
    [SerializeField] private Button DungeonLevelUpButton;
    [SerializeField] private Button AdvertisementButton;
    [SerializeField] private TurnAnnounceView turnAnnounceView;
    [SerializeField] private BuzzAnnounceView buzzAnnounceView;
    
    //鍛冶屋を開く
    public Subject<Unit> OnBlacksmithClicked { get; } = new();
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
    //次のターンに進む
    public Subject<Unit> OnNextTurnClicked { get; } = new();

    public void Awake()
    {
        BlaskSmithButton.onClick.AddListener(() => OnBlacksmithClicked.OnNext(Unit.Default));
        SetItemButton.onClick.AddListener(() => OnSetItemClicked.OnNext(Unit.Default));
        InfoButton.onClick.AddListener(() => OnInfoClicked.OnNext(Unit.Default));
        ToolButton.onClick.AddListener(() => OnToolClicked.OnNext(Unit.Default));
        StartShopButton.onClick.AddListener(() => OnStartShopClicked.OnNext(Unit.Default));
        MapButton.onClick.AddListener(() => OnMapClicked.OnNext(Unit.Default));
        DungeonLevelUpButton.onClick.AddListener(() => OnDungeonLevelUpClicked.OnNext(Unit.Default));
        if (AdvertisementButton != null)
            AdvertisementButton.onClick.AddListener(() => OnAdvertisementClicked.OnNext(Unit.Default));
        NextTurnButton.onClick.AddListener(() => OnNextTurnClicked.OnNext(Unit.Default));
    }

    public void Initialize()
    {
        
    }


    /// <summary>
    /// ターン切り替え演出を再生する（左→右にスライド）
    /// </summary>
    public void ShowTurnAnnounce(int turn)
    {
        turnAnnounceView.Show(turn);
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
}
