using R3;
using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

public class StreamingSettingPresenter : IDisposable
{
    private readonly StreamingSettingModel _model;
    private readonly StreamingSettingView  _view;
    private readonly ItemModel             _itemModel;
    private readonly BattleInputData       _battleInputData;
    private readonly IDungeonCatalog       _dungeonCatalog;
    private readonly CompositeDisposable   _d = new CompositeDisposable();

    /// <summary>
    /// 確定ボタンが押された時に完了する UniTaskCompletionSource。
    /// BattleSceneStarter から await して品出し確定を待つ。
    /// </summary>
    private UniTaskCompletionSource _confirmTcs;

    public StreamingSettingPresenter(
        StreamingSettingModel model,
        StreamingSettingView  view,
        ItemModel             itemModel,
        BattleInputData       battleInputData,
        IDungeonCatalog       dungeonCatalog)
    {
        _model           = model;
        _view            = view;
        _itemModel       = itemModel;
        _battleInputData = battleInputData;
        _dungeonCatalog  = dungeonCatalog;
    }

    /// <summary>
    /// 品出し設定画面を表示し、確定ボタンが押されるまで待機する。
    /// パネルの表示/非表示は BattlePanelManager が管理するため、ここでは制御しない。
    /// 戻り値: 選択されたアイテムの辞書 (itemId → quantity)
    /// </summary>
    public async UniTask<IReadOnlyDictionary<string, int>> RunAsync()
    {
        _confirmTcs = new UniTaskCompletionSource();

        Bind();
        Entry();

        // 確定ボタンが押されるまで待機
        await _confirmTcs.Task;


        return _model.Selected;
    }

    private void Bind()
    {
        _d.Clear();

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

        // 6) 確定ボタン → UniTaskCompletionSource を完了
        _view.OnConfirmClicked
            .Subscribe(_ => HandleConfirm())
            .AddTo(_d);

        // ④ 前回と同じで確定（保存済み選択をそのまま確定）
        _view.OnQuickConfirmClicked
            .Subscribe(_ => HandleConfirm())
            .AddTo(_d);

        _view.OnAutoSelectClicked
            .Subscribe(_ => HandleAutoSelect())
            .AddTo(_d);

        // 前回構成の件数をボタンラベルに反映
        _view.SetQuickConfirmLabel(_model.Selected.Count);
    }

    private void Entry()
    {
        // StreamingSetting画面に遷移した時にデータを再ロードして表示を更新
        _model.LoadData();
        _model.CleanupUnavailableItems(_itemModel);
        _view.PopulateSelected(_model.Selected, _itemModel);
        _view.PopulateAvailable(_itemModel.RuntimeItems);
    }

    /// <summary>
    /// 確定ボタン押下時の処理。
    /// 選択データを保存し、UniTaskCompletionSource を完了させる。
    /// </summary>
    private void HandleConfirm()
    {
        _model.SaveData();
        _itemModel.SaveData();
        _confirmTcs?.TrySetResult();
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

    private void HandleAutoSelect()
    {
        var selected = BuildAutoSelection();
        _model.ReplaceSelection(selected);
        _view.PopulateSelected(_model.Selected, _itemModel);
        _model.SaveData();
        _view.SetQuickConfirmLabel(_model.Selected.Count);
    }

    private List<KeyValuePair<string, int>> BuildAutoSelection()
    {
        var dungeonInfoAvailable = TryGetPurchasedDungeonInfo(out var dungeon);
        var enemyElements = dungeonInfoAvailable
            ? GetDungeonEnemyElements(dungeon, _battleInputData.DungeonLevel)
            : new List<ElementType>();

        return _itemModel.RuntimeItems
            .Where(item => item != null && item.Stock.Value > 0)
            .Select(item => new
            {
                Item = item,
                Score = CalculateAutoSelectScore(item, dungeonInfoAvailable, dungeon, enemyElements)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.Stock.Value)
            .Take(8)
            .Select(x => new KeyValuePair<string, int>(x.Item.ItemId, x.Item.Stock.Value))
            .ToList();
    }

    private float CalculateAutoSelectScore(
        RuntimeItemData item,
        bool dungeonInfoAvailable,
        DungeonInfoScriptableObj dungeon,
        List<ElementType> enemyElements)
    {
        float score = 0f;

        score += item.Demand.Value * 100f;
        score += item.SalesRate * 30f;
        score += Mathf.Log10(Mathf.Max(1, item.CurrentPrice.Value)) * 12f;
        score += Mathf.Min(item.Stock.Value, 20) * 1.5f;

        if (!dungeonInfoAvailable || dungeon == null)
        {
            return score;
        }

        if (item.ItemAttribute == dungeon.requiredAttribute)
        {
            score += 20f;
        }

        foreach (var element in enemyElements)
        {
            if (element == ElementType.None) continue;

            if (ElementAttributeMapper.IsEffective(item.ItemAttribute, element))
            {
                score += 35f;
            }
            else if (ElementAttributeMapper.IsWeak(item.ItemAttribute, element))
            {
                score -= 25f;
            }
        }

        return score;
    }

    private bool TryGetPurchasedDungeonInfo(out DungeonInfoScriptableObj dungeon)
    {
        dungeon = null;
        if (_battleInputData == null || _dungeonCatalog == null)
        {
            return false;
        }

        dungeon = _dungeonCatalog.GetDungeon(_battleInputData.DungeonKey);
        if (dungeon == null)
        {
            return false;
        }

        if (!SaveSystem.TryLoad(out var save) || save?.dungeons == null)
        {
            return false;
        }

        foreach (var savedDungeon in save.dungeons)
        {
            if (savedDungeon == null) continue;

            bool sameDungeon =
                savedDungeon.dungeonKey == _battleInputData.DungeonKey.ToString()
                || savedDungeon.dungeonKey == dungeon.dungeonName;

            if (sameDungeon)
            {
                return savedDungeon.isShowedInfo;
            }
        }

        return false;
    }

    private static List<ElementType> GetDungeonEnemyElements(DungeonInfoScriptableObj dungeon, int dungeonLevel)
    {
        var levelData = dungeon?.GetLevelData(dungeonLevel);
        if (levelData?.monsters == null)
        {
            return new List<ElementType>();
        }

        return levelData.monsters
            .Where(enemy => enemy != null)
            .Select(enemy => enemy.elementType)
            .Where(element => element != ElementType.None)
            .ToList();
    }

    public void Show() => _view.Show();
    public void Hide() { _model.SaveData(); _view.Hide(); }
    public void Dispose() => _d.Dispose();
}
