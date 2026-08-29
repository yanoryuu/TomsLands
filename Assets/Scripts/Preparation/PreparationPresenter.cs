using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// 準備シーン（出撃準備）の Presenter。タイトルの「ニューゲーム」から遷移してくる
/// （「続きから」はこのシーンを通らず TomsShop へ直行する）。
/// - 借入: メタ通貨で解放した借入枠の範囲でいくら借りるか決める（初回返済に利息付き上乗せ）
/// - 持ち込み: requiredLevel==1 のアイテムをスロット数まで初期在庫として持ち込む
/// - スターターレリック: 呪い以外の Common レリックから1個
/// - スタートダッシュ: メタ通貨を払ってこのランに適用する消費効果3種
/// 出撃時に RunSetupData へ書き出し、GameLifecycleHandler.InitializeNewGame が消費する。
/// UI未配線の間は旧挙動（即 TomsShop へ遷移）にフォールバックする。
/// </summary>
public class PreparationPresenter : IStartable, IDisposable
{
    private readonly PreparationView view;
    private readonly PreparationModel model;
    private readonly MetaProgressModel metaProgress;
    private readonly StartModeData startModeData;
    private readonly RunSetupData runSetupData;
    private readonly List<ItemData> masterItems;
    private readonly List<RelicDefinition> relicDefinitions;
    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable catalogDisposables = new();

    private List<PreparationChoiceSlot> carrySlots = new();
    private List<PreparationChoiceSlot> relicSlots = new();

    public PreparationPresenter(
        PreparationView view,
        PreparationModel model,
        MetaProgressModel metaProgress,
        StartModeData startModeData,
        RunSetupData runSetupData,
        List<ItemData> masterItems,
        List<RelicDefinition> relicDefinitions)
    {
        this.view = view;
        this.model = model;
        this.metaProgress = metaProgress;
        this.startModeData = startModeData;
        this.runSetupData = runSetupData;
        this.masterItems = masterItems;
        this.relicDefinitions = relicDefinitions;
    }

    public void Start()
    {
        // UI が未配線ならスタブ時代と同じく素通りする（RunSetupData は使わない）
        if (view == null || !view.IsInteractiveReady)
        {
            Debug.Log("[PreparationPresenter] 準備UIが未配線のため素通り → TomsShop（Docs/Preparation_UnityWiring.md 参照）");
            runSetupData.Clear();
            SceneManager.LoadScene("TomsShop");
            return;
        }

        SoundManager.Instance?.PlayBGM("OP");
        runSetupData.Clear();

        Bind();
        BuildCatalogs();
        RefreshAll();
        view.ShowMessage("出撃の準備をしよう。借入は初回返済に利息付きで上乗せされる。");
    }

    private void Bind()
    {
        view.OnBorrowPlus.Subscribe(_ => { model.AddBorrow(model.GetCreditLine(metaProgress)); RefreshAll(); }).AddTo(disposables);
        view.OnBorrowMinus.Subscribe(_ => { model.SubtractBorrow(); RefreshAll(); }).AddTo(disposables);

        view.OnCreditUpgrade.Subscribe(_ =>
        {
            int cost = model.GetCreditUpgradeCost(metaProgress);
            if (cost < 0)
            {
                view.ShowMessage("借入枠はすでに最大だ。");
                return;
            }
            if (!metaProgress.TrySpend(cost))
            {
                view.ShowMessage("信用が足りない……。ランを重ねて信用を積もう。");
                return;
            }
            metaProgress.UpgradeCreditLine();
            metaProgress.SaveData();
            view.ShowMessage($"借入枠を拡張した（{model.GetCreditLine(metaProgress):N0}Gまで借りられる）。");
            RefreshAll();
        }).AddTo(disposables);

        view.OnFlyerToggled.Subscribe(_ => { model.ToggleFlyer(); RefreshAll(); }).AddTo(disposables);
        view.OnAppraisalToggled.Subscribe(_ => { model.ToggleAppraisal(); RefreshAll(); }).AddTo(disposables);
        view.OnGraceToggled.Subscribe(_ => { model.ToggleGrace(); RefreshAll(); }).AddTo(disposables);

        view.OnDepart.Subscribe(_ => Depart()).AddTo(disposables);
        view.OnBack.Subscribe(_ => SceneManager.LoadScene("TitleScene")).AddTo(disposables);
    }

    private void BuildCatalogs()
    {
        catalogDisposables.Dispose();
        catalogDisposables = new CompositeDisposable();

        // 持ち込みアイテム: requiredLevel==1 のみ（上位アイテムの解禁は将来のメタ拡張）
        var carryPool = (masterItems ?? new List<ItemData>())
            .Where(m => m != null && m.requiredLevel <= 1)
            .ToList();
        carrySlots = view.PopulateCatalog(view.CarryCatalogParent, carryPool.Count);
        for (int i = 0; i < carrySlots.Count && i < carryPool.Count; i++)
        {
            var master = carryPool[i];
            var slot = carrySlots[i];
            slot.Setup(master.itemId, master.itemName, master.itemIcon, showMinus: true);

            slot.OnSelected.Subscribe(id =>
            {
                if (!model.TryAddCarry(id))
                    view.ShowMessage($"持ち込み枠がいっぱいだ（{model.CarryTotal}/{model.CarrySlots}）。");
                RefreshAll();
            }).AddTo(catalogDisposables);

            slot.OnMinus.Subscribe(id =>
            {
                model.RemoveCarry(id);
                RefreshAll();
            }).AddTo(catalogDisposables);
        }

        // スターターレリック: 呪い以外の Common
        var relicPool = (relicDefinitions ?? new List<RelicDefinition>())
            .Where(r => r != null && !r.isCurse && r.rarity == RelicRarity.Common)
            .ToList();
        relicSlots = view.PopulateCatalog(view.RelicCatalogParent, relicPool.Count);
        for (int i = 0; i < relicSlots.Count && i < relicPool.Count; i++)
        {
            var relic = relicPool[i];
            var slot = relicSlots[i];
            slot.Setup(relic.relicId, relic.relicName, relic.icon, showMinus: false);

            slot.OnSelected.Subscribe(id =>
            {
                model.SelectStarterRelic(id);
                RefreshAll();
            }).AddTo(catalogDisposables);
        }
    }

    private void RefreshAll()
    {
        var settings = GameConst.Preparation;

        model.ClampBorrow(model.GetCreditLine(metaProgress));

        view.UpdateMetaCurrency(metaProgress.MetaCurrency.Value);
        view.UpdateDifficulty(DifficultyLabel(startModeData.SelectedMode));

        int upgradeCost = model.GetCreditUpgradeCost(metaProgress);
        view.UpdateBorrow(
            model.BorrowAmount,
            model.GetCreditLine(metaProgress),
            upgradeCost,
            upgradeCost >= 0 && metaProgress.MetaCurrency.Value >= upgradeCost);

        view.UpdateCarryCounter(model.CarryTotal, model.CarrySlots);

        view.UpdateStartDash(
            $"宣伝ビラ（信用{settings.flyerCost}）", model.UseFlyer,
            $"目利きの手引き（信用{settings.appraisalCost}）", model.UseAppraisal,
            $"返済猶予証（信用{settings.graceCost}）", model.UseGrace);

        // 持ち込み個数とレリック選択のハイライトを反映
        foreach (var slot in carrySlots)
        {
            model.CarryItems.TryGetValue(slot.Id, out int count);
            slot.SetCount(count);
            slot.SetHighlighted(count > 0);
        }
        foreach (var slot in relicSlots)
        {
            slot.SetHighlighted(slot.Id == model.StarterRelicId);
        }
    }

    /// <summary>出撃: メタ通貨を精算し、RunSetupData に書き出して TomsShop へ。</summary>
    private void Depart()
    {
        int dashCost = model.StartDashTotalCost;
        if (dashCost > 0 && !metaProgress.TrySpend(dashCost))
        {
            view.ShowMessage($"スタートダッシュに必要な信用が足りない（必要 {dashCost}）。");
            return;
        }
        metaProgress.SaveData();

        runSetupData.HasSetup = true;
        runSetupData.BorrowedAmount = model.BorrowAmount;
        runSetupData.CarryItemIds = model.CarryItems.Keys.ToList();
        runSetupData.CarryItemCounts = model.CarryItems.Values.ToList();
        runSetupData.StarterRelicId = model.StarterRelicId;
        runSetupData.UseFlyer = model.UseFlyer;
        runSetupData.UseAppraisal = model.UseAppraisal;
        runSetupData.UseGrace = model.UseGrace;

        Debug.Log($"[Preparation] 出撃: 借入={model.BorrowAmount}G, 持ち込み={model.CarryTotal}個, レリック={model.StarterRelicId}, ダッシュ=({model.UseFlyer},{model.UseAppraisal},{model.UseGrace})");
        SceneManager.LoadScene("TomsShop");
    }

    private static string DifficultyLabel(GameModeId mode) => mode switch
    {
        GameModeId.Short => "かんたん",
        GameModeId.Medium => "ふつう",
        GameModeId.Long => "むずかしい",
        _ => mode.ToString(),
    };

    public void Dispose()
    {
        catalogDisposables.Dispose();
        disposables.Dispose();
    }
}
