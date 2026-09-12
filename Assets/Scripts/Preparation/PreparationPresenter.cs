using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// 準備シーン（出店準備）の Presenter。村の「出店準備へ」から遷移してくる
/// （「続きから」はこのシーンを通らず TomsShop へ直行する）。
/// - 持ち込み資金: 前のランで持ち帰って銀行に預けたGを、上限（村の銀行レベル依存）まで持ち込む
/// - 難易度: このランの難易度（かんたん/ふつう/むずかしい）をここで選ぶ（タイトルでは選ばない）
/// - スターターレリック: 呪い以外の Common レリックから1個
/// - スタートダッシュ: 銀行預金Gを払ってこのランに適用する消費効果3種
/// 出店時に RunSetupData（+難易度は StartModeData）へ書き出し、GameLifecycleHandler.InitializeNewGame が消費する。
/// UI未配線の間は旧挙動（即 TomsShop へ遷移）にフォールバックする。
/// </summary>
public class PreparationPresenter : IStartable, IDisposable
{
    private readonly PreparationView view;
    private readonly PreparationModel model;
    private readonly MetaProgressModel metaProgress;
    private readonly StartModeData startModeData;
    private readonly RunSetupData runSetupData;
    private readonly List<RelicDefinition> relicDefinitions;
    private readonly CompositeDisposable disposables = new();
    private CompositeDisposable catalogDisposables = new();

    private List<PreparationChoiceSlot> relicSlots = new();

    public PreparationPresenter(
        PreparationView view,
        PreparationModel model,
        MetaProgressModel metaProgress,
        StartModeData startModeData,
        RunSetupData runSetupData,
        List<RelicDefinition> relicDefinitions)
    {
        this.view = view;
        this.model = model;
        this.metaProgress = metaProgress;
        this.startModeData = startModeData;
        this.runSetupData = runSetupData;
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

        // 難易度の初期値はタイトルが入れた既定値（ふつう）を引き継ぐ
        model.SelectDifficulty(startModeData.SelectedMode);

        Bind();
        BuildCatalogs();
        RefreshAll();
        view.ShowMessage("出店の準備をしよう。前のランで持ち帰ったお金は銀行に預けてあり、上限まで持ち込める。スタートダッシュも預金から買える。");
    }

    private void Bind()
    {
        view.OnBorrowPlus.Subscribe(_ => { model.AddCarry(model.GetCarryMax(metaProgress)); RefreshAll(); }).AddTo(disposables);
        view.OnBorrowMinus.Subscribe(_ => { model.SubtractCarry(); RefreshAll(); }).AddTo(disposables);

        view.OnDifficultySelected.Subscribe(difficulty =>
        {
            model.SelectDifficulty(difficulty);
            view.ShowMessage($"難易度「{DifficultyLabel(difficulty)}」を選択した。");
            RefreshAll();
        }).AddTo(disposables);

        view.OnFlyerToggled.Subscribe(_ => { model.ToggleFlyer(); RefreshAll(); }).AddTo(disposables);
        view.OnAppraisalToggled.Subscribe(_ => { model.ToggleAppraisal(); RefreshAll(); }).AddTo(disposables);
        view.OnGraceToggled.Subscribe(_ => { model.ToggleGrace(); RefreshAll(); }).AddTo(disposables);

        view.OnDepart.Subscribe(_ => Depart()).AddTo(disposables);
        view.OnBack.Subscribe(_ => SceneManager.LoadScene("VillageScene")).AddTo(disposables);
    }

    private void BuildCatalogs()
    {
        catalogDisposables.Dispose();
        catalogDisposables = new CompositeDisposable();

        // スターターレリック: 呪い以外の Common
        var relicPool = (relicDefinitions ?? new List<RelicDefinition>())
            .Where(r => r != null && !r.isCurse && r.rarity == RelicRarity.Common)
            .ToList();
        relicSlots = view.PopulateCatalog(view.RelicCatalogParent, relicPool.Count);
        for (int i = 0; i < relicSlots.Count && i < relicPool.Count; i++)
        {
            var relic = relicPool[i];
            var slot = relicSlots[i];
            slot.Setup(relic.relicId, relic.relicName, relic.icon, showMinus: false, info: relic.description);

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

        int carryMax = model.GetCarryMax(metaProgress);
        model.ClampCarry(carryMax);

        view.UpdateBankedGold(metaProgress.BankedGold.Value);
        view.UpdateDifficulty(DifficultyLabel(model.Difficulty));
        view.UpdateDifficultySelection(model.Difficulty);

        int carryLimit = model.GetCarryLimit(metaProgress);
        int bankLevel = model.GetBankLevel(metaProgress);
        bool isLimitMax = settings.bankCarryLimits != null &&
                          bankLevel >= settings.bankCarryLimits.Length - 1;
        view.UpdateCarry(model.CarryAmount, carryLimit, bankLevel, isLimitMax);

        view.UpdateStartDash(
            $"宣伝ビラ（{settings.flyerCost:N0}G）", model.UseFlyer,
            $"目利きの手引き（{settings.appraisalCost:N0}G）", model.UseAppraisal,
            $"返済猶予証（{settings.graceCost:N0}G）", model.UseGrace);

        // レリック選択のハイライトを反映
        foreach (var slot in relicSlots)
        {
            slot.SetHighlighted(slot.Id == model.StarterRelicId);
        }
    }

    /// <summary>出店: 持ち込み＋スタートダッシュ代を銀行預金から精算し、RunSetupData に書き出して TomsShop へ。</summary>
    private void Depart()
    {
        int totalCost = model.CarryAmount + model.StartDashTotalCost;
        if (totalCost > metaProgress.BankedGold.Value)
        {
            // 持ち込みは預金上限でクランプ済みなので、超えるのはスタートダッシュ分
            view.ShowMessage($"銀行預金が足りない（必要 {totalCost:N0}G / 残高 {metaProgress.BankedGold.Value:N0}G）。持ち込みかスタートダッシュを減らそう。");
            return;
        }
        if (totalCost > 0 && !metaProgress.TrySpendBankedGold(totalCost))
        {
            view.ShowMessage("銀行預金の精算に失敗した。");
            return;
        }
        metaProgress.SaveData();

        // このランの難易度を確定（タイトルではなくここで選ぶ）
        startModeData.SetFlowSelection(model.Difficulty, startModeData.UseAutoGeneration);

        runSetupData.HasSetup = true;
        runSetupData.CarriedGold = model.CarryAmount;
        runSetupData.StarterRelicId = model.StarterRelicId;
        runSetupData.UseFlyer = model.UseFlyer;
        runSetupData.UseAppraisal = model.UseAppraisal;
        runSetupData.UseGrace = model.UseGrace;

        Debug.Log($"[Preparation] 出店: 難易度={model.Difficulty}, 持ち込み={model.CarryAmount}G, レリック={model.StarterRelicId}, ダッシュ=({model.UseFlyer},{model.UseAppraisal},{model.UseGrace}), 預金残高={metaProgress.BankedGold.Value}G");
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
