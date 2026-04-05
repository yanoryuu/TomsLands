using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// ダンジョンレベルアップ画面のPresenter。
/// View ↔ Model（DungeonRepository / TomsModel）間を仲介する。
/// </summary>
public class DungeonLevelUpPresenter : IPresenter, IStartable, IDisposable
{
    private readonly DungeonLevelUpView view;
    private readonly DungeonRepository dungeonRepository;
    private readonly TomsModel tomsModel;
    private readonly StateManager stateManager;

    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable slotDisposables = new();

    public DungeonLevelUpPresenter(
        DungeonLevelUpView view,
        DungeonRepository dungeonRepository,
        TomsModel tomsModel,
        StateManager stateManager)
    {
        this.view = view;
        this.dungeonRepository = dungeonRepository;
        this.tomsModel = tomsModel;
        this.stateManager = stateManager;

        stateManager.RegisterOnEnter(TomsShopGamePhase.DungeonLevelUp, Entry);
    }

    public void Start()
    {
        Bind();
    }

    public void Entry()
    {
        RefreshList();
    }

    private void Bind()
    {
        // 閉じるボタン → Shopに戻る
        view.OnCloseRequested
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);
    }

    /// <summary>
    /// ダンジョン一覧を再生成し、各スロットのイベントを購読する。
    /// </summary>
    private void RefreshList()
    {
        slotDisposables.Dispose();
        slotDisposables = new CompositeDisposable();

        var dungeons = new List<DungeonData>(dungeonRepository.availableDungeons);
        var slots = view.PopulateDungeonList(dungeons);

        foreach (var slot in slots)
        {
            // レベルアップボタン押下
            slot.OnLevelUpClicked
                .Subscribe(key => HandleLevelUp(key))
                .AddTo(slotDisposables);

            // 所持金が変わったらボタンの有効/無効を更新
            tomsModel.PlayerMoney
                .Subscribe(money =>
                {
                    var data = dungeonRepository.GetById(slot.DungeonKey);
                    if (data == null) return;
                    bool isMax = data.currentDungeonLevel >= GameConst.MaxDungeonLevel;
                    int cost = data.levelUpCost;
                    slot.SetAffordable(!isMax && money >= cost);
                })
                .AddTo(slotDisposables);
        }
    }

    /// <summary>
    /// レベルアップ処理
    /// </summary>
    private void HandleLevelUp(DungeonName key)
    {
        var data = dungeonRepository.GetById(key);
        if (data == null) return;

        // 上限チェック
        if (data.currentDungeonLevel >= GameConst.MaxDungeonLevel)
        {
            Debug.Log($"[DungeonLevelUp] {key} は最大レベルです。");
            return;
        }

        int cost = data.levelUpCost;

        // 資金チェック
        if (tomsModel.PlayerMoney.Value < cost)
        {
            Debug.Log($"[DungeonLevelUp] 資金不足: 必要={cost}G, 所持={tomsModel.PlayerMoney.Value}G");
            return;
        }

        // 支払い
        tomsModel.PurchaseItem(cost);
        tomsModel.SavePlayerMoney();

        // レベルアップ
        data.currentDungeonLevel++;
        dungeonRepository.Save();

        Debug.Log($"[DungeonLevelUp] {key} Lv.{data.currentDungeonLevel - 1} → Lv.{data.currentDungeonLevel} (費用: {cost}G)");

        // リスト全体を再描画（レベル・コストが変わるため）
        RefreshList();
    }

    public void Dispose()
    {
        slotDisposables.Dispose();
        disposables.Dispose();
    }
}

