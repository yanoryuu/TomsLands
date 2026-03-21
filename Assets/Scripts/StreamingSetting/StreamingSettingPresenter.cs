using R3;
using System;
using System.Collections.Generic;
using System.Linq;
using VContainer.Unity;

public class StreamingSettingPresenter : IDisposable,IPresenter,IStartable
{
    private readonly StreamingSettingModel _model;
    private readonly StreamingSettingView  _view;
    private readonly ItemModel             _itemModel;
    private readonly HeroModel             _heroModel;
    private readonly GameFlowManager       _gameFlowManager;
    private readonly BattleInputData       _battleInputData;
    private readonly SceneTransitionService _sceneTransition;
    private readonly StateManager          _stateManager;
    private readonly CompositeDisposable   _d = new CompositeDisposable();

    public StreamingSettingPresenter(
        StreamingSettingModel model,
        StreamingSettingView  view,
        ItemModel             itemModel,
        HeroModel             heroModel,
        GameFlowManager       gameFlowManager,
        BattleInputData       battleInputData,
        SceneTransitionService sceneTransition,
        StateManager          stateManager)
    {
        _model           = model;
        _view            = view;
        _itemModel       = itemModel;
        _heroModel       = heroModel;
        _gameFlowManager = gameFlowManager;
        _battleInputData = battleInputData;
        _sceneTransition = sceneTransition;
        _stateManager    = stateManager;
    }

    private void Bind()
    {
        // 1) ロードして在庫切れアイテムを除外し、左パネルに再表示
        _model.LoadData();
        _model.CleanupUnavailableItems(_itemModel);
        _view.PopulateSelected(_model.Selected, _itemModel);

        // 2) 利用可能アイテム一覧（所持数0のアイテムはView側でフィルタ）
        _view.PopulateAvailable(_itemModel.RuntimeItems);

        // 3) 選択
        _view.OnItemSelected
            .Subscribe(id => HandleSelected(id))
            .AddTo(_d);

        // 4) 解除
        _view.OnItemDeselected
            .Subscribe(id => HandleDeselected(id))
            .AddTo(_d);

        // 5) 数量変更
        _view.OnQuantityChanged
            .Subscribe(tuple => { _model.SetQuantity(tuple.id, tuple.qty); _model.SaveData(); })
            .AddTo(_d);

        // 6) 確定ボタン → BattleInputData に書き込み → FightScene へ遷移
        _view.OnConfirmClicked
            .Subscribe(_ => HandleConfirm())
            .AddTo(_d);
    }

    public void Entry()
    {
        // StreamingSetting画面に遷移した時にデータを再ロードして表示を更新
        _model.LoadData();
        _model.CleanupUnavailableItems(_itemModel);
        _view.PopulateSelected(_model.Selected, _itemModel);
        _view.PopulateAvailable(_itemModel.RuntimeItems);
    }

    /// <summary>
    /// 確定ボタン押下時の処理。
    /// 選択アイテム・勇者装備・ダンジョン情報を BattleInputData に書き込み、FightScene へ遷移する。
    /// </summary>
    private void HandleConfirm()
    {
        _model.SaveData();

        // FightScene側のItemModelが最新の在庫データをロードできるよう、遷移前にセーブ
        _itemModel.SaveData();

        // 選択アイテムを BattleInputItem に変換
        var selectedItems = new List<BattleInputItem>();
        foreach (var kv in _model.Selected)
        {
            var runtime = _itemModel.GetRuntimeItem(kv.Key);
            selectedItems.Add(new BattleInputItem
            {
                ItemId = kv.Key,
                Quantity = kv.Value,
                Price = runtime != null ? runtime.CurrentPrice.Value : 0
            });
        }

        // GameFlowManager から現在のダンジョン情報を取得（GameFlowNodeに設定済み）
        var dungeonKey = _battleInputData.DungeonKey; // GameFlowManager.NextTurn() で既にセット済み

        // BattleInputData に書き込み（GameFlowIndex も保存してシーン復帰時に復元する）
        _battleInputData.Setup(
            dungeonKey,
            new List<string>(_heroModel.EquippedItemIds),
            selectedItems,
            _gameFlowManager.CurrentIndex
        );

        // FightScene へ遷移
        _sceneTransition.GoToBattle();
    }
    
    private void HandleSelected(string id)
    {
        // 在庫が0のアイテムは選択不可
        var runtime = _itemModel.GetRuntimeItem(id);
        if (runtime == null || runtime.Stock.Value <= 0) return;

        if (!_model.TryAdd(id)) return;

        // 左パネルを全件再描画のみ（AddSelectedItemとの二重追加を防止）
        _view.PopulateSelected(_model.Selected, _itemModel);

        _model.SaveData();
    }


    private void HandleDeselected(string id)
    {
        _model.Remove(id);
        _view.PopulateSelected(_model.Selected, _itemModel);
        _model.SaveData();
    }

    public void Show() => _view.Show();
    public void Hide() { _model.SaveData(); _view.Hide(); }
    public void Dispose() => _d.Dispose();
    public void Start()
    {
        _stateManager.RegisterOnEnter(StreamingGamePhase.StreamingSetting, Entry);
        Bind();
    }
}