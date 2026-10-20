using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 村シーン（歩ける村）のPresenter。
/// - 帰還時: VillageArrivalReport（消費型）を読んで収支ポップを表示
/// - 区画（FacilityPlot）の調べる→投資パネル→投資
/// - 総合Lvに応じて町人が増える（VillagePopulationView。投資で増えた分はその場でポップして現れる）
/// - 出店 → PreparationScene / タイトルへ → TitleScene
/// - 初回入場時: 村チュートリアル会話（VillageTutorialDirector）→ その後に帰還演出
/// 村と店の経営（ラン）は別フロー: ここでの操作はラン外のメタ層のみに影響する。
/// View未配線（IsInteractiveReady=false）の場合は PreparationScene へ素通りする。
/// </summary>
public class VillagePresenter : IStartable, IDisposable
{
    private readonly VillageView view;
    private readonly VillageModel model;
    private readonly MetaProgressModel metaProgress;
    private readonly VillageTutorialDirector tutorial;
    private readonly CompositeDisposable disposables = new();
    private readonly CancellationTokenSource cts = new();

    private string selectedFacilityId;
    private bool arrivalPlaying; // 帰還到着演出の再生中
    private bool tutorialPlaying; // 村チュートリアル会話の再生中

    /// <summary>演出・会話中で村の操作を受け付けない状態か。</summary>
    private bool IsBusy => arrivalPlaying || tutorialPlaying;

    private const string DefaultMessage = "稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。";

    public VillagePresenter(VillageView view, VillageModel model, MetaProgressModel metaProgress,
        TutorialScenarioService tutorialScenario)
    {
        this.view = view;
        this.model = model;
        this.metaProgress = metaProgress;
        tutorial = tutorialScenario != null ? new VillageTutorialDirector(tutorialScenario) : null;
    }

    public void Start()
    {
        if (view == null || !view.IsInteractiveReady)
        {
            Debug.Log("[VillagePresenter] 村UIが未配線のため素通り → PreparationScene（Docs/Village_UnityWiring.md 参照）");
            SceneLoader.Load("PreparationScene");
            return;
        }

        var soundManager = SoundManager.Instance;
        if (soundManager != null) soundManager.PlayBGM("OP");

        Bind();
        RefreshAll();

        // ラン終了からの帰還なら収支報告を表示（初回は村チュートリアル会話のあと）
        bool hasArrival = VillageArrivalReport.TryConsume(out bool cleared, out int earned, out int converted);
        if (tutorial != null && tutorial.ShouldPlay)
        {
            PlayTutorialThenArrivalAsync(hasArrival, cleared, earned, converted, cts.Token).Forget();
        }
        else if (hasArrival)
        {
            PlayArrival(cleared, earned, converted);
        }
        else
        {
            view.ShowMessage(DefaultMessage);
        }
    }

    /// <summary>
    /// 村チュートリアル（未読時のみ）。会話中は移動・区画の調べる・村UIを止め、終わったら操作可能に戻す。
    /// 帰還報告がある場合は会話のあとに到着演出を出す。
    /// </summary>
    private async UniTaskVoid PlayTutorialThenArrivalAsync(bool hasArrival, bool cleared, int earned, int converted,
        CancellationToken ct)
    {
        tutorialPlaying = true;
        view.HideInvestPanel();
        try
        {
            await tutorial.PlayAsync(view.UiCamera, view.SetUiInputLocked, ct);
        }
        catch (OperationCanceledException)
        {
            return; // シーン遷移などで破棄された
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            tutorialPlaying = false;
        }

        if (hasArrival) PlayArrival(cleared, earned, converted);
        else view.ShowMessage("まずは立て看板を調べて、最初の施設を建ててみよう。");
    }

    private void Bind()
    {
        view.OnDepart.Subscribe(_ =>
        {
            if (IsBusy) return;
            Debug.Log("[VillagePresenter] 出店準備へ");
            SceneLoader.Load("PreparationScene");
        }).AddTo(disposables);

        view.OnGoTitle.Subscribe(_ =>
        {
            if (IsBusy) return;
            SceneLoader.Load("TitleScene");
        }).AddTo(disposables);

        // 区画の「調べる」→投資パネル
        foreach (var plot in view.Plots)
        {
            if (plot == null) continue;
            plot.OnInteract
                .Subscribe(facilityId => SelectFacility(facilityId))
                .AddTo(disposables);
        }

        view.OnInvest.Subscribe(_ =>
        {
            if (IsBusy) return;
            HandleInvest();
        }).AddTo(disposables);
        view.OnConversionClosed
            .Subscribe(_ => view.ShowMessage(DefaultMessage))
            .AddTo(disposables);
    }

    /// <summary>
    /// 帰還の到着演出（稼ぎ→村資金へコインが飛ぶ）。VillageArrivalFx が無い/素材未設定なら旧・収支ポップ。
    /// 演出中は区画の調べる・出撃・タイトルを受け付けない（Fx側でもプレイヤー入力とUIをブロック）。
    /// </summary>
    private void PlayArrival(bool cleared, int earned, int converted)
    {
        var fx = view.ArrivalFx;
        if (fx == null || !fx.CanPlay || view.FundsText == null)
        {
            view.ShowConversionPopup(cleared, earned, converted);
            return;
        }

        arrivalPlaying = true;
        fx.Play(cleared, earned, converted, model.VillageFunds, view.FundsText, view.SetFundsDisplay, () =>
        {
            arrivalPlaying = false;
            RefreshAll();
            view.ShowMessage(DefaultMessage);
        });
    }

    private void SelectFacility(string facilityId)
    {
        if (IsBusy) return;
        var facility = model.GetFacility(facilityId);
        if (facility == null)
        {
            Debug.LogWarning($"[VillagePresenter] 未知の施設ID: {facilityId}（Resources_moved/Village のマスターに存在しない）");
            return;
        }

        selectedFacilityId = facilityId;
        RefreshInvestPanel();
    }

    private void RefreshInvestPanel()
    {
        var facility = model.GetFacility(selectedFacilityId);
        if (facility == null) return;

        int level = model.GetLevel(facility.facilityId);
        string current = level > 0 ? facility.GetLevel(level)?.effectText : null;
        string next = level < facility.MaxLevel ? facility.GetLevel(level + 1)?.effectText : null;
        int cost = model.GetNextCost(facility);
        var (ok, reason) = model.CanInvest(facility);

        view.ShowInvestPanel(facility, level, current, next, cost, ok, reason);
    }

    private void HandleInvest()
    {
        if (string.IsNullOrEmpty(selectedFacilityId)) return;

        if (model.TryInvest(selectedFacilityId, out string message))
        {
            var soundManager = SoundManager.Instance;
            if (soundManager != null) soundManager.PlaySE("営業/SE_開発完了");
            view.ShowMessage(message);

            // 建設演出（該当区画をポップ）
            foreach (var plot in view.Plots)
            {
                if (plot != null && plot.FacilityId == selectedFacilityId)
                {
                    plot.PlayBuildEffect();
                    break;
                }
            }
            RefreshAll(animatePopulation: true); // 総合Lvが上がったら町人が引っ越してくる
            RefreshInvestPanel(); // 連続投資できるよう次Lv表示に更新
        }
        else
        {
            view.ShowMessage(message);
            RefreshInvestPanel();
        }
    }

    /// <summary>HUD・全区画・町人の人数を最新化する。animatePopulation=true なら増えた町人をポップで出す。</summary>
    private void RefreshAll(bool animatePopulation = false)
    {
        int villageLevel = model.VillageLevel;
        view.UpdateHud(model.VillageFunds, villageLevel);

        var population = view.Population;
        if (population != null) population.ApplyScaled(villageLevel, model.MaxVillageLevel, animatePopulation);

        foreach (var plot in view.Plots)
        {
            if (plot == null) continue;
            var facility = model.GetFacility(plot.FacilityId);
            if (facility == null) continue;

            int level = model.GetLevel(facility.facilityId);
            // 未解禁表示: レベル0かつ建設ゲート（領主館）未達
            bool locked = level == 0 &&
                          facility.facilityId != VillageModel.HallId &&
                          model.HallLevel < facility.requiredHallLevel;
            plot.SetState(level, locked, facility.icon, facility.facilityName);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
        disposables.Dispose();
    }
}
