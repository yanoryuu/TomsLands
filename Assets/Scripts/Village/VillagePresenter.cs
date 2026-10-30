using System;
using System.Collections.Generic;
using System.Linq;
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
/// - 村の格に応じてマップの地面と装飾が育つ（VillageRankDecor。格が上がった瞬間は新しい装飾が弾んで出る）
/// - 出店 → PreparationScene / タイトルへ → TitleScene
/// - 初回入場時: 村チュートリアル前半（暗幕）→ 帰還演出（儲け→村資金）→ 後半（看板を調べて投資）
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
    private bool rankUpPlaying; // 村の格が上がった演出（ポップ→会話）の再生中
    private int shownRank; // 画面に出している村の格（上がった瞬間を見つける）
    private VillageRankDecor rankDecor; // 格ごとの地面・装飾（シーンに無ければ null）

    /// <summary>演出・会話中で村の操作を受け付けない状態か。</summary>
    private bool IsBusy => arrivalPlaying || tutorialPlaying || rankUpPlaying;

    /// <summary>王都の広場に立つ自己ベストの掲示板の区画ID（FacilityPlot.facilityId。施設データには無い）。</summary>
    public const string RecordBoardId = "records";

    /// <summary>村の格が上がったときの会話のラベル（VillageTutorial.csv。末尾の数字 = 上がった先の格）。</summary>
    public const string RankUpLabelPrefix = "Village_RankUp_";

    /// <summary>デバッグメニューで施設レベルを変えたとき（村シーンにいれば読み直して格の上がりを確かめる）。</summary>
    public static event Action DebugFacilityLevelsChanged;

    public static void NotifyDebugFacilityLevelsChanged() => DebugFacilityLevelsChanged?.Invoke();

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
        rankDecor = UnityEngine.Object.FindFirstObjectByType<VillageRankDecor>(FindObjectsInactive.Include);
        shownRank = VillageRank.Compute(metaProgress);
        RefreshAll();

        // ラン終了からの帰還なら収支報告を表示（初回は村チュートリアル会話のあと）
        bool hasArrival = VillageArrivalReport.TryConsume(out bool cleared, out int earned, out int converted,
            out VillageFundsBreakdown breakdown);
        if (tutorial != null && tutorial.ShouldPlay)
        {
            PlayTutorialThenArrivalAsync(hasArrival, cleared, earned, converted, breakdown, cts.Token).Forget();
        }
        else if (hasArrival)
        {
            PlayArrival(cleared, earned, converted, breakdown);
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
        VillageFundsBreakdown breakdown,
        CancellationToken ct)
    {
        tutorialPlaying = true;
        view.HideInvestPanel();
        bool arrivalShown = false;
        try
        {
            await tutorial.PlayAsync(view.UiCamera, view.SetUiInputLocked, ct,
                hasArrival
                    ? async token =>
                    {
                        arrivalShown = true;
                        await PlayArrivalAsync(cleared, earned, converted, breakdown, token);
                    }
                    : null,
                bankrupt: hasArrival && !cleared);
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

        if (hasArrival && !arrivalShown) PlayArrival(cleared, earned, converted, breakdown);
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
            .Subscribe(_ => { if (!rankUpPlaying) view.ShowMessage(DefaultMessage); })
            .AddTo(disposables);

        // 左端のショートカットの帯（区画を調べたのと同じ処理。演出・会話中はつまみも受け付けない）
        view.ShortcutBlocked = () => IsBusy;
        view.OnShortcutFacility
            .Subscribe(facilityId => SelectFacility(facilityId))
            .AddTo(disposables);

        DebugFacilityLevelsChanged += OnDebugFacilityLevelsChanged;
    }

    private void OnDebugFacilityLevelsChanged()
    {
        if (metaProgress == null) return;
        metaProgress.LoadData(); // デバッグメニューは別の MetaProgressModel で書き込むので読み直す
        RefreshAll(animatePopulation: true);
        if (selectedFacilityId != null && view.IsInvestPanelOpen) RefreshInvestPanel();
        CheckRankUp();
    }

    /// <summary>
    /// 帰還の到着演出（稼ぎ→村資金へコインが飛ぶ）。VillageArrivalFx が無い/素材未設定なら旧・収支ポップ。
    /// 演出中は区画の調べる・出撃・タイトルを受け付けない（Fx側でもプレイヤー入力とUIをブロック）。
    /// </summary>
    private void PlayArrival(bool cleared, int earned, int converted, VillageFundsBreakdown breakdown)
    {
        var fx = view.ArrivalFx;
        if (fx == null || !fx.CanPlay || view.FundsText == null)
        {
            view.ShowConversionPopup(cleared, earned, converted, breakdown);
            return;
        }

        arrivalPlaying = true;
        fx.Play(cleared, earned, converted, model.VillageFunds, view.FundsText, view.SetFundsDisplay, breakdown, () =>
        {
            arrivalPlaying = false;
            RefreshAll();
            view.ShowMessage(DefaultMessage);
        });
    }

    /// <summary>到着演出を流して終わるまで待つ（チュートリアルの前半と後半のあいだ用）。</summary>
    private async UniTask PlayArrivalAsync(bool cleared, int earned, int converted, VillageFundsBreakdown breakdown,
        CancellationToken ct)
    {
        var fx = view.ArrivalFx;
        if (fx == null || !fx.CanPlay || view.FundsText == null)
        {
            // 旧・収支ポップは閉じるボタンを待たない（会話中は村UIのクリックを止めているため）
            view.ShowConversionPopup(cleared, earned, converted, breakdown);
            return;
        }
        var done = new UniTaskCompletionSource();
        arrivalPlaying = true;
        fx.Play(cleared, earned, converted, model.VillageFunds, view.FundsText, view.SetFundsDisplay, breakdown, () =>
        {
            arrivalPlaying = false;
            RefreshAll();
            done.TrySetResult();
        });
        await done.Task.AttachExternalCancellation(ct);
    }

    private void SelectFacility(string facilityId)
    {
        if (IsBusy) return;
        if (facilityId == RecordBoardId)
        {
            ShowRecordBoard();
            return;
        }
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
        int special = VillageEffects.SpecialEffectLevel;
        string current, next;
        bool nextIsSpecial;
        if (level >= facility.MaxLevel && level >= special && level >= 2)
        {
            // Lv5（最大）: 数値の効果は Lv4 のまま続くので「現在」に Lv4 を、金の枠に Lv5 の特殊な効果を出す
            current = facility.GetLevel(level - 1)?.effectText;
            next = facility.GetLevel(level)?.effectText;
            nextIsSpecial = true;
        }
        else
        {
            current = level > 0 ? facility.GetLevel(level)?.effectText : null;
            next = level < facility.MaxLevel ? facility.GetLevel(level + 1)?.effectText : null;
            nextIsSpecial = level < facility.MaxLevel && level + 1 >= special;
        }
        int cost = model.GetNextCost(facility);
        var (ok, reason) = model.CanInvest(facility);

        view.ShowInvestPanel(facility, level, current, next, cost, ok, reason, nextIsSpecial);
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
            CheckRankUp();
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
        int rank = VillageRank.Compute(metaProgress);
        view.UpdateHud(model.VillageFunds, villageLevel, VillageRank.Name(rank), NextRankLine(rank));

        var population = view.Population;
        if (population != null) population.ApplyScaled(villageLevel, model.MaxVillageLevel, animatePopulation);

        // 村の格に合わせた地面と装飾（投資・デバッグで格が上がったときは新しく出たものが弾む）
        if (rankDecor != null) rankDecor.Apply(rank, animatePopulation);

        foreach (var plot in view.Plots)
        {
            if (plot == null) continue;
            if (plot.FacilityId == RecordBoardId)
            {
                // 自己ベストの掲示板は王都になってから広場に立つ
                plot.gameObject.SetActive(VillageRank.IsTop(rank));
                plot.SetState(0, false, null, "自己ベストの掲示板");
                continue;
            }
            var facility = model.GetFacility(plot.FacilityId);
            if (facility == null) continue;

            int level = model.GetLevel(facility.facilityId);
            // 未解禁表示: レベル0かつ建設ゲート（領主館）未達
            bool locked = level == 0 &&
                          facility.facilityId != VillageModel.HallId &&
                          model.HallLevel < facility.requiredHallLevel;
            plot.SetState(level, locked, facility.icon, facility.facilityName);
            if (rankDecor != null) rankDecor.ApplyFacilityLevel(facility.facilityId, level, facility.MaxLevel, animatePopulation);
        }

        view.SetShortcuts(BuildShortcuts(rank));
    }

    /// <summary>
    /// 左端の帯に並べる施設（マスターの施設を、領主館 → 建設に要る領主館Lv の順。同じならマスターの順）。
    /// 記録掲示板は区画がその格で立っているときだけ足す（上の区画の表示と同じ判定）。
    /// </summary>
    private List<VillageView.ShortcutEntry> BuildShortcuts(int rank)
    {
        var list = new List<VillageView.ShortcutEntry>();
        var ordered = model.Facilities
            .Where(f => f != null)
            .OrderBy(f => f.facilityId == VillageModel.HallId ? 0 : 1)
            .ThenBy(f => f.requiredHallLevel);
        foreach (var f in ordered)
        {
            int level = model.GetLevel(f.facilityId);
            list.Add(new VillageView.ShortcutEntry
            {
                Id = f.facilityId,
                Name = f.facilityName,
                Facility = f,
                LevelLabel = level >= f.MaxLevel ? "MAX" : level <= 0 ? "未建設" : $"Lv{level}",
                CanUpgrade = model.CanInvest(f).ok,
            });
        }
        bool recordShown = VillageRank.IsTop(rank) && view.Plots.Any(p => p != null && p.FacilityId == RecordBoardId);
        if (recordShown)
            list.Add(new VillageView.ShortcutEntry { Id = RecordBoardId, Name = "自己ベストの掲示板", LevelLabel = "" });
        return list;
    }

    // ───────── 村の格 ─────────

    /// <summary>次の格の条件（「次は『町』: 総合Lv10・領主館Lv1」）。王都なら称号。</summary>
    private static string NextRankLine(int rank)
    {
        if (VillageRank.IsTop(rank)) return $"称号「{VillageRank.TopTitle}」";
        int next = rank + 1;
        return $"次は『{VillageRank.Name(next)}』: {VillageRank.ConditionText(next)}";
    }

    /// <summary>格が上がっていれば、格の名前のポップ→短い会話を流す（会話は上がった先の格の1本。既読管理）。</summary>
    private void CheckRankUp()
    {
        int rank = VillageRank.Compute(metaProgress);
        if (rank <= shownRank)
        {
            shownRank = rank; // デバッグで下げたときに合わせる
            return;
        }
        int from = shownRank;
        shownRank = rank;
        PlayRankUpAsync(from, rank, cts.Token).Forget();
    }

    private async UniTaskVoid PlayRankUpAsync(int from, int to, CancellationToken ct)
    {
        rankUpPlaying = true;
        view.HideInvestPanel();
        try
        {
            string name = VillageRank.Name(to);
            string unlock = VillageRank.IsTop(to)
                ? $"称号「{VillageRank.TopTitle}」と、広場に自己ベストの掲示板"
                : VillageRank.TryGetUnlockedMode(to, out var mode)
                    ? $"「{ModeLabel(mode)}」のランが選べるようになった"
                    : "";
            view.ShowMessage($"トコの村が『{name}』になった！");

            if (view.ShowRankUpPopup("村の格が上がった！", $"トコの村は『{name}』に", unlock, from, to))
            {
                var closed = new UniTaskCompletionSource();
                using (view.OnConversionClosed.Subscribe(_ => closed.TrySetResult()))
                {
                    await closed.Task.AttachExternalCancellation(ct);
                }
            }

            // 上がった先の格の会話（一度に2段上がったら上の段だけ。ラベルが無ければ何もしない）
            if (tutorial != null)
            {
                view.SetUiInputLocked(true);
                try
                {
                    await tutorial.PlayRankUpAsync(RankUpLabelPrefix + to, ct);
                }
                finally
                {
                    view.SetUiInputLocked(false);
                }
            }
            view.ShowMessage(VillageRank.IsTop(to)
                ? "広場に自己ベストの掲示板が立った。調べると、ランの長さごとの最高の儲けが見られる。"
                : $"トコの村が『{name}』になった！　{unlock}。");
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
            rankUpPlaying = false;
        }
    }

    /// <summary>自己ベストの掲示板（ランの長さごとの最高の儲けとランク）。</summary>
    private void ShowRecordBoard()
    {
        view.HideInvestPanel();
        // 3列の表（難易度・金額・ランク）
        var rows = new List<VillageView.RecordRow>();
        foreach (var mode in new[] { GameModeId.Short, GameModeId.Medium, GameModeId.Long })
        {
            if (metaProgress != null && metaProgress.TryGetBestProfit(mode, out int best))
            {
                string amount = (best >= 0 ? "+" : "−") + Mathf.Abs(best).ToString("N0") + "G";
                rows.Add(new VillageView.RecordRow(ModeLabel(mode), amount, BestRankOf(mode, best)));
            }
            else
            {
                rows.Add(new VillageView.RecordRow(ModeLabel(mode), "まだ記録なし", ""));
            }
        }
        view.ShowRecordBoard("自己ベスト", rows);
    }

    /// <summary>その長さでいちばん良かったランク（ラン履歴にあればその中の最高。無ければ最高の儲けから求める）。</summary>
    private string BestRankOf(GameModeId mode, int bestProfit)
    {
        string best = ResultModel.EvaluateRank(bestProfit, mode);
        foreach (var r in metaProgress.RunRecords)
        {
            if (r == null || !r.cleared || r.mode != (int)mode || string.IsNullOrEmpty(r.rank)) continue;
            if (RankOrder(r.rank) < RankOrder(best)) best = r.rank;
        }
        return best;
    }

    private static int RankOrder(string r) => r switch { "S" => 0, "A" => 1, "B" => 2, "C" => 3, "D" => 4, _ => 5 };

    private static string ModeLabel(GameModeId mode) => SaveSlotView.GetModeLabel(mode);

    public void Dispose()
    {
        DebugFacilityLevelsChanged -= OnDebugFacilityLevelsChanged;
        cts.Cancel();
        cts.Dispose();
        disposables.Dispose();
    }
}
