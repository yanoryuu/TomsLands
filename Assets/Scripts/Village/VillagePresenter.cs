using System;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// 村シーン（歩ける村）のPresenter。
/// - 帰還時: VillageArrivalReport（消費型）を読んで収支ポップを表示
/// - 区画（FacilityPlot）の調べる→投資パネル→投資
/// - 出店 → PreparationScene / タイトルへ → TitleScene
/// 村と店の経営（ラン）は別フロー: ここでの操作はラン外のメタ層のみに影響する。
/// View未配線（IsInteractiveReady=false）の場合は PreparationScene へ素通りする。
/// </summary>
public class VillagePresenter : IStartable, IDisposable
{
    private readonly VillageView view;
    private readonly VillageModel model;
    private readonly MetaProgressModel metaProgress;
    private readonly CompositeDisposable disposables = new();

    private string selectedFacilityId;
    private bool arrivalPlaying; // 帰還到着演出の再生中

    public VillagePresenter(VillageView view, VillageModel model, MetaProgressModel metaProgress)
    {
        this.view = view;
        this.model = model;
        this.metaProgress = metaProgress;
    }

    public void Start()
    {
        if (view == null || !view.IsInteractiveReady)
        {
            Debug.Log("[VillagePresenter] 村UIが未配線のため素通り → PreparationScene（Docs/Village_UnityWiring.md 参照）");
            SceneManager.LoadScene("PreparationScene");
            return;
        }

        SoundManager.Instance?.PlayBGM("OP");

        Bind();
        RefreshAll();

        // ラン終了からの帰還なら収支報告を表示
        if (VillageArrivalReport.TryConsume(out bool cleared, out int earned, out int converted))
        {
            PlayArrival(cleared, earned, converted);
        }
        else
        {
            view.ShowMessage("稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。");
        }
    }

    private void Bind()
    {
        view.OnDepart.Subscribe(_ =>
        {
            if (arrivalPlaying) return;
            Debug.Log("[VillagePresenter] 出店準備へ");
            SceneManager.LoadScene("PreparationScene");
        }).AddTo(disposables);

        view.OnGoTitle.Subscribe(_ =>
        {
            if (arrivalPlaying) return;
            SceneManager.LoadScene("TitleScene");
        }).AddTo(disposables);

        // 区画の「調べる」→投資パネル
        foreach (var plot in view.Plots)
        {
            if (plot == null) continue;
            plot.OnInteract
                .Subscribe(facilityId => SelectFacility(facilityId))
                .AddTo(disposables);
        }

        view.OnInvest.Subscribe(_ => HandleInvest()).AddTo(disposables);
        view.OnConversionClosed
            .Subscribe(_ => view.ShowMessage("稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。"))
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
            view.ShowMessage("稼いだお金で村に投資しよう。施設に近づいて調べると詳細が見られる。");
        });
    }

    private void SelectFacility(string facilityId)
    {
        if (arrivalPlaying) return;
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
            SoundManager.Instance?.PlaySE("営業/SE_開発完了");
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
            RefreshAll();
            RefreshInvestPanel(); // 連続投資できるよう次Lv表示に更新
        }
        else
        {
            view.ShowMessage(message);
            RefreshInvestPanel();
        }
    }

    /// <summary>HUDと全区画の見た目を最新化する。</summary>
    private void RefreshAll()
    {
        view.UpdateHud(model.VillageFunds, model.VillageLevel);

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
        disposables.Dispose();
    }
}
