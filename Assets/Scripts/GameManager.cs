using R3;
using System.Linq;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public ReactiveProperty<GamePhase> CurrentPhase { get; private set; }

    [Header("References")]
    [SerializeField] private ItemShopView itemShopView;
    [SerializeField] private PreparationView preparationView;
    [SerializeField] private BattleView battleView;
    [SerializeField] private EndPhaseView endPhaseView;
    [SerializeField] private TomsShopView tomsShopView;
    [SerializeField] private ItemSelectionView itemSelectionView;

    private ItemModel itemModel;
    private ItemPresenter itemPresenter;
    private GamePhasePresenter gamePhasePresenter;
    private TomShopPresenter tomShopPresenter;

    private void Awake()
    {
        // ReactiveProperty初期化
        CurrentPhase = new ReactiveProperty<GamePhase>();

        // ItemModel初期化（マスターItemData読み込み例）
        ItemData[] masterItems = Resources.LoadAll<ItemData>("ItemData");
        Debug.Log(masterItems.Length);
        itemModel = new ItemModel(masterItems.ToList());
        
        //データロード
        itemModel.LoadData();
        
        //初期表示
        itemShopView.PopulateItemList(itemModel.RuntimeItems);
        itemSelectionView.PopulateItemList(itemModel.RuntimeItems);

        // ItemPresenter初期化（必要ならView渡す）
        itemPresenter = new ItemPresenter(itemModel, itemShopView ,tomsShopView);
        
        // PresenterとItemSelectionViewをバインド
        itemPresenter.BindItemSelectionView(itemSelectionView);

        // GamePhasePresenterをnewで生成
        gamePhasePresenter = new GamePhasePresenter(
            this,
            itemPresenter,
            itemShopView,
            preparationView,
            battleView,
            endPhaseView,
            tomsShopView
        );
        
        tomShopPresenter = new TomShopPresenter(
            tomsShopView,
            itemShopView,
            itemSelectionView,
            itemModel
        );

        CurrentPhase.Value = GamePhase.TomsShop;
    }

    public void ProceedToNextPhase()
    {
        switch (CurrentPhase.Value)
        {
            case GamePhase.Preparation:
                CurrentPhase.Value = GamePhase.Preparation;
                break;
            case GamePhase.Battle:
                CurrentPhase.Value = GamePhase.Battle;
                break;
            case GamePhase.End:
                CurrentPhase.Value = GamePhase.End;
                break;
            case GamePhase.TomsShop:
                CurrentPhase.Value = GamePhase.TomsShop;
                break;
            default:
                Debug.LogWarning("不正なフェーズ遷移");
                break;
        }
        Debug.Log(CurrentPhase.Value);
    }

    private void OnDestroy()
    {
        gamePhasePresenter?.Dispose();
    }
}
