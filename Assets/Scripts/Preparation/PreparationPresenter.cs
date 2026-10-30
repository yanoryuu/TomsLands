using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 準備シーン（出店準備）の Presenter。村の「出店準備へ」から遷移してくる
/// （「続きから」はこのシーンを通らず TomsShop へ直行する）。
/// - 持ち込み資金: 前のランで持ち帰って銀行に預けたGを、上限（村の銀行レベル依存）まで持ち込む
/// - 難易度: このランの難易度（かんたん/ふつう/むずかしい）をここで選ぶ（タイトルでは選ばない）
/// - スターターレリック: 呪い以外の Common レリックから1個（村の冒険者ギルドが建つと、解禁済みのレアも並ぶ）
/// - スタートダッシュ: 村資金を払ってこのランに適用する消費効果3種
/// 出店時に RunSetupData（+難易度は StartModeData）へ書き出し、GameLifecycleHandler.InitializeNewGame が消費する。
/// UI未配線の間は旧挙動（即 TomsShop へ遷移）にフォールバックする。
/// 初めてこの画面に来たときは出店準備チュートリアル会話（Tutorial_Preparation）を流し、会話中は操作を止める。
/// </summary>
public class PreparationPresenter : IStartable, IDisposable
{
    private readonly PreparationView view;
    private readonly PreparationModel model;
    private readonly MetaProgressModel metaProgress;
    private readonly StartModeData startModeData;
    private readonly RunSetupData runSetupData;
    private readonly List<RelicDefinition> relicDefinitions;
    private readonly TutorialScenarioService tutorialScenario;
    private readonly CompositeDisposable disposables = new();
    private readonly CancellationTokenSource cts = new();
    private CompositeDisposable catalogDisposables = new();
    private bool tutorialPlaying; // 会話チュートリアル再生中は出店/戻るを受け付けない

    /// <summary>出店準備チュートリアルのラベル（Assets/TomsScenario/Scenarios/VillageTutorial.csv）。</summary>
    private const string PreparationTutorialLabel = "Tutorial_Preparation";

    private List<PreparationChoiceSlot> relicSlots = new();

    public PreparationPresenter(
        PreparationView view,
        PreparationModel model,
        MetaProgressModel metaProgress,
        StartModeData startModeData,
        RunSetupData runSetupData,
        List<RelicDefinition> relicDefinitions,
        TutorialScenarioService tutorialScenario)
    {
        this.tutorialScenario = tutorialScenario;
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
            // 準備シーンは新規ランの入口。前のランの TomsShop 入場で Continue になったままなので戻す
            startModeData.SetNewGame();
            SceneLoader.Load("TomsShop");
            return;
        }

        var soundManager = SoundManager.Instance;
        if (soundManager != null) soundManager.PlayBGM("OP");
        runSetupData.Clear();

        // 難易度の初期値はタイトルが入れた既定値（ふつう）か前回の選択を引き継ぐ。村の格で未解放なら選べる長さに丸める
        model.SelectDifficulty(VillageRank.ClampToUnlocked(startModeData.SelectedMode, metaProgress));

        // 難易度ボタンに配信の回数と日数を添える（ランの長さ＝賭けの回数を分かって選べるように）
        view.SetDifficultyDetails(
            PreparationModel.DescribeModeShort(GameModeId.Short),
            PreparationModel.DescribeModeShort(GameModeId.Medium),
            PreparationModel.DescribeModeShort(GameModeId.Long));
        // 村の格が足りないランは鍵つきで見せ、解放の条件を出す
        foreach (var mode in AllModes)
        {
            bool locked = !VillageRank.IsModeUnlocked(mode, metaProgress);
            view.SetDifficultyLock(mode, locked, PreparationModel.DescribeModeShort(mode), LockButtonText(mode));
        }

        Bind();
        BuildCatalogs();
        RefreshAll();
        view.ShowMessage("出店の準備をしよう。持ち帰ったお金は村資金になっていて、上限まで持ち込める。スタートダッシュも村資金から買える。");

        // 初回のみ: 出店準備の説明会話
        if (tutorialScenario != null && !tutorialScenario.HasSeen(PreparationTutorialLabel))
        {
            PlayTutorialAsync(cts.Token).Forget();
        }
    }

    /// <summary>出店準備チュートリアル。会話中は準備画面の操作を止め、終わったら操作可能に戻す。</summary>
    private async UniTaskVoid PlayTutorialAsync(CancellationToken ct)
    {
        tutorialPlaying = true;
        view.SetUiInputLocked(true);
        try
        {
            await tutorialScenario.PlayOnceAsync(PreparationTutorialLabel, ct);
            // 会話を閉じたクリックがそのままボタンに届かないよう少し待つ
            await UniTask.DelayFrame(2, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            // シーン遷移などで破棄された
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            tutorialPlaying = false;
            if (view != null) view.SetUiInputLocked(false);
        }
    }

    private void Bind()
    {
        view.OnBorrowPlus.Subscribe(_ => { model.AddCarry(model.GetCarryMax(metaProgress)); RefreshAll(); }).AddTo(disposables);
        view.OnBorrowMinus.Subscribe(_ => { model.SubtractCarry(); RefreshAll(); }).AddTo(disposables);
        view.OnCarrySliderChanged.Subscribe(amount =>
        {
            model.SetCarry(amount, model.GetCarryMax(metaProgress));
            RefreshAll();
        }).AddTo(disposables);

        view.OnDifficultySelected.Subscribe(difficulty =>
        {
            if (!VillageRank.IsModeUnlocked(difficulty, metaProgress))
            {
                view.ShowMessage($"「{DifficultyLabel(difficulty)}」はまだ選べない。{VillageRank.ModeUnlockText(difficulty)}。");
                return;
            }
            model.SelectDifficulty(difficulty);
            string detail = PreparationModel.DescribeModeShort(difficulty);
            view.ShowMessage(string.IsNullOrEmpty(detail)
                ? $"難易度「{DifficultyLabel(difficulty)}」を選択した。"
                : $"「{DifficultyLabel(difficulty)}」を選んだ。{detail}で、最後の配信のあとに精算する。");
            RefreshAll();
        }).AddTo(disposables);

        view.OnFlyerToggled.Subscribe(_ =>
        {
            model.ToggleFlyer();
            ShowStartDashMessage(FlyerName, FlyerEffect(), GameConst.Preparation.flyerCost, model.UseFlyer);
            RefreshAll();
        }).AddTo(disposables);
        view.OnAppraisalToggled.Subscribe(_ =>
        {
            model.ToggleAppraisal();
            ShowStartDashMessage(AppraisalName, AppraisalEffect(), GameConst.Preparation.appraisalCost, model.UseAppraisal);
            RefreshAll();
        }).AddTo(disposables);
        view.OnGraceToggled.Subscribe(_ =>
        {
            model.ToggleGrace();
            ShowStartDashMessage(GraceName, GraceEffect(), GameConst.Preparation.graceCost, model.UseGrace);
            RefreshAll();
        }).AddTo(disposables);

        view.OnDepart.Subscribe(_ =>
        {
            if (tutorialPlaying) return;
            Depart();
        }).AddTo(disposables);
        view.OnBack.Subscribe(_ =>
        {
            if (tutorialPlaying) return;
            SceneLoader.Load("VillageScene");
        }).AddTo(disposables);
    }

    private void BuildCatalogs()
    {
        catalogDisposables.Dispose();
        catalogDisposables = new CompositeDisposable();

        // スターターレリック: 呪い以外の Common。村の冒険者ギルドが建てば、ギルドで解禁済みのレアも並ぶ。
        // ギルド Lv4 からは解禁済みのエピックから StarterEpicCount 個（ランごとに選び直す）も並ぶ
        var village = VillageEffects.From(metaProgress);
        var definitions = relicDefinitions ?? new List<RelicDefinition>();
        var relicPool = definitions
            .Where(r => r != null && !r.isCurse &&
                        (r.rarity == RelicRarity.Common ||
                         (village.StarterRareUnlocked && r.rarity == RelicRarity.Rare && r.unlockGuildLevel <= village.RelicUnlockTier)))
            .Concat(PickStarterEpics(definitions, village))
            .OrderBy(r => r.rarity)
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
        // 見た目だけ: 並べた札を順にめくって出す（選べるかどうか・並びは変えない）
        view.RevealCatalog(relicSlots, i => i < relicPool.Count ? relicPool[i].rarity : RelicRarity.Common);
    }

    /// <summary>
    /// スターター候補に足すエピック（ギルド Lv4 以上）。準備画面を開き直しても同じ顔ぶれになるよう、
    /// 今までのラン数をシードにして選ぶ（次のランでは選び直される）。
    /// </summary>
    private IEnumerable<RelicDefinition> PickStarterEpics(List<RelicDefinition> definitions, VillageEffects village)
    {
        int count = village.StarterEpicCount;
        if (count <= 0) return Enumerable.Empty<RelicDefinition>();
        var epics = definitions
            .Where(r => r != null && !r.isCurse && r.rarity == RelicRarity.Epic && r.unlockGuildLevel <= village.RelicUnlockTier)
            .OrderBy(r => r.relicId, System.StringComparer.Ordinal)
            .ToList();
        var rng = new System.Random(metaProgress.TotalRuns * 7919 + 17);
        for (int i = epics.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (epics[i], epics[j]) = (epics[j], epics[i]);
        }
        return epics.Take(count);
    }

    private void RefreshAll()
    {
        var settings = GameConst.Preparation;

        int carryMax = model.GetCarryMax(metaProgress);
        model.ClampCarry(carryMax);

        view.UpdateVillageFunds(metaProgress.VillageFunds);
        view.UpdateDifficulty(DifficultyLabel(model.Difficulty));
        view.UpdateDifficultySelection(model.Difficulty);

        int carryLimit = model.GetCarryLimit(metaProgress);
        int bankLevel = model.GetBankLevel(metaProgress);
        bool isLimitMax = settings.bankCarryLimits != null &&
                          bankLevel >= settings.bankCarryLimits.Length - 1;
        view.UpdateCarry(model.CarryAmount, carryLimit, bankLevel, isLimitMax);
        view.UpdateCarrySlider(model.CarryAmount, carryMax, PreparationModel.CarryStepAmount);

        view.UpdateStartDash(
            FlyerName, settings.flyerCost, model.UseFlyer,
            AppraisalName, settings.appraisalCost, model.UseAppraisal,
            GraceName, settings.graceCost, model.UseGrace);

        // レリック選択のハイライトを反映
        foreach (var slot in relicSlots)
        {
            slot.SetHighlighted(slot.Id == model.StarterRelicId);
        }
    }

    /// <summary>出店: 持ち込み＋スタートダッシュ代を村資金から精算し、RunSetupData に書き出して TomsShop へ。</summary>
    private void Depart()
    {
        // 念のため: 未解放のランでは出店しない（選べる長さに丸める）
        if (!VillageRank.IsModeUnlocked(model.Difficulty, metaProgress))
            model.SelectDifficulty(VillageRank.ClampToUnlocked(model.Difficulty, metaProgress));

        int totalCost = model.CarryAmount + model.StartDashTotalCost;
        if (totalCost > metaProgress.VillageFunds)
        {
            // 持ち込みは村資金でクランプ済みなので、超えるのはスタートダッシュ分
            view.ShowMessage($"村資金が足りない（必要 {totalCost:N0}G / 残高 {metaProgress.VillageFunds:N0}G）。持ち込みかスタートダッシュを減らそう。");
            return;
        }
        if (totalCost > 0 && !metaProgress.TrySpendVillageFunds(totalCost))
        {
            view.ShowMessage("村資金の精算に失敗した。");
            return;
        }
        metaProgress.SaveData();

        // このランの難易度を確定（タイトルではなくここで選ぶ）
        startModeData.SetFlowSelection(model.Difficulty, startModeData.UseAutoGeneration);
        // 新規ランとして始める（前のランの TomsShop 入場で Continue になったまま残っているため）
        startModeData.SetNewGame();

        runSetupData.HasSetup = true;
        runSetupData.CarriedGold = model.CarryAmount;
        runSetupData.StarterRelicId = model.StarterRelicId;
        runSetupData.UseFlyer = model.UseFlyer;
        runSetupData.UseAppraisal = model.UseAppraisal;
        runSetupData.UseGrace = model.UseGrace;

        Debug.Log($"[Preparation] 出店: 難易度={model.Difficulty}, 持ち込み={model.CarryAmount}G, レリック={model.StarterRelicId}, ダッシュ=({model.UseFlyer},{model.UseAppraisal},{model.UseGrace}), 村資金残高={metaProgress.VillageFunds}G");
        SceneLoader.Load("TomsShop");
    }

    // スタートダッシュ: 名前だけでは効き目が分からないので、押したときに効果を出す（O-10。名前の枠は狭く2行目が入らない）
    private const string FlyerName = "宣伝ビラ";
    private const string AppraisalName = "目利きの手引き";
    private const string GraceName = "初回減免状";

    private static string FlyerEffect()
        => $"注目+{GameConst.Preparation.flyerAttention}・フォロワー+{GameConst.Preparation.flyerFollowers}";
    private static string AppraisalEffect()
        => $"全商品の需要+{Percent(GameConst.Preparation.appraisalDemandBoost)}";
    private static string GraceEffect()
        => $"最初の納付金-{Percent(GameConst.Preparation.graceDiscountRate)}";

    private void ShowStartDashMessage(string name, string effect, int cost, bool on)
        => view.ShowMessage(on
            ? $"「{name}」を買う（{cost:N0}G）\n効果: {effect}"
            : $"「{name}」をやめた\n効果: {effect}");

    private static string Percent(float rate) => $"{Mathf.RoundToInt(rate * 100f)}%";

    private static readonly GameModeId[] AllModes = { GameModeId.Short, GameModeId.Medium, GameModeId.Long };

    /// <summary>未解放のボタンの2行目（「村が『町』になると解放」＋改行＋「総合Lv10・領主館Lv1」）。</summary>
    private static string LockButtonText(GameModeId mode)
    {
        int rank = VillageRank.RequiredRank(mode);
        return $"村が『{VillageRank.Name(rank)}』になると解放\n{VillageRank.ConditionText(rank)}";
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
        cts.Cancel();
        cts.Dispose();
        catalogDisposables.Dispose();
        disposables.Dispose();
    }
}
