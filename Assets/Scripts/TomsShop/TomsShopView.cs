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
    [SerializeField] private TurnAnnounceView turnAnnounceView;
    
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
}
