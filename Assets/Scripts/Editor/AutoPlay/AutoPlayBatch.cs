using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>バッチ（ボット × シード）の設定。summary.json にもそのまま書かれる（API キーは含まない）。</summary>
[Serializable]
public sealed class AutoPlayBatchConfig
{
    public GameModeId Mode = GameModeId.Short;
    public int Seeds = 3;
    public int BaseSeed = 1001;
    public bool UseRandomBot = true;
    public bool UseGreedyBot = true;
    public List<string> JevPersonas = new List<string>();
    public AutoPlayJevDecisionMode JevDecisionMode = AutoPlayJevDecisionMode.Sample;
    /// <summary>同時に進めるラン数（メインスレッド上の交互実行。Jev の待ち時間を重ねるためのもの）。</summary>
    public int MaxConcurrent = 8;
    public float StreamSalesScale = 1f;
    public int StreamMaxKinds = 6;
    public bool ProbabilisticBattle;
    /// <summary>初期資金。本番はサーバー配信で 100,000G に上書きされているためそれに合わせる（0 = GameConst のローカル値）。</summary>
    public int InitMoneyOverride = 100000;
    public int MaxStepsPerRun = 400;
    /// <summary>Jev の累計コストがこれを超えたらバッチを止める（USD。0 = 無制限）。既定 ≒100円。</summary>
    public double CostLimitUsd = 100.0 / AutoPlayReport.JpyPerUsd;
    /// <summary>合格ライン（暫定）。Docs/Jev_AutoPlay_Design.md §4.2。</summary>
    public AutoPlayPassCriteria Criteria = new AutoPlayPassCriteria();
    /// <summary>true = ゲーム本体の Debug.Log をそのまま出す（遅い）。false = 警告以上のみ。</summary>
    public bool VerboseLogs;

    // --- 配信中の介入（Docs/Jev_AutoPlay_Design.md §2.5） ---
    /// <summary>ボットに配信中の介入（スパチャ）を使わせる。</summary>
    public bool EnableInterventions = true;
    /// <summary>同じシードで「介入なし」の対照群（ボット名 +noint）も回す（貪欲・ランダムのみ。API 費用なし）。</summary>
    public bool ControlRunsWithoutInterventions = true;
    /// <summary>対照群に Jev ボットも含める（API 費用が倍になる）。</summary>
    public bool ControlRunsIncludeJev;
    /// <summary>視聴者の赤スパ（必殺技の自動発動）。既定は SO の viewerSuperChatEnabled / viewerRedTriggersSpecial に従う。</summary>
    public AutoPlayViewerSuperChatMode ViewerSuperChat = AutoPlayViewerSuperChatMode.FollowSettings;
    /// <summary>視聴者の赤スパが1戦闘ターンに起きる確率（較正値）。</summary>
    public float ViewerRedChancePerTurn = 0.03f;
    /// <summary>1戦闘ターン ≒ 何秒か（クールダウン 6 秒をターンに直す換算。較正値）。</summary>
    public float SecondsPerBattleTurn = 3f;
}

/// <summary>ボット名に接尾辞を付けるだけのラッパー（対照群の識別用）。</summary>
public sealed class AutoPlayRenamedBot : IAutoPlayBot
{
    private readonly IAutoPlayBot _inner;
    private readonly string _suffix;

    public AutoPlayRenamedBot(IAutoPlayBot inner, string suffix)
    {
        _inner = inner;
        _suffix = suffix;
    }

    public string Name => _inner.Name + _suffix;
    public int Requests => _inner.Requests;
    public long InputTokens => _inner.InputTokens;
    public System.Threading.Tasks.Task<AutoPlayDayPlan> PlanDayAsync(AutoPlaySnapshot s, System.Threading.CancellationToken ct) => _inner.PlanDayAsync(s, ct);
    public System.Threading.Tasks.Task<AutoPlayStreamPlan> PlanStreamAsync(AutoPlaySnapshot s, System.Threading.CancellationToken ct) => _inner.PlanStreamAsync(s, ct);
    public System.Threading.Tasks.Task<AutoPlayRelicPlan> PlanRelicAsync(AutoPlaySnapshot s, System.Threading.CancellationToken ct) => _inner.PlanRelicAsync(s, ct);
}

/// <summary>
/// バッチ実行の司令塔。エディタのメインスレッド上で複数ランを交互に進める（async/await の継続はメインスレッドに戻る）。
/// どこからでも <see cref="Start"/> を呼べる（EditorWindow・メニュー・unity CLI の eval）。
/// 進行中はドメインリロード（スクリプト再コンパイル）しないこと。
/// </summary>
public static class AutoPlayBatch
{
    /// <summary>介入なしの対照群のボット名に付ける接尾辞。</summary>
    public const string NoInterventionSuffix = "+noint";

    public static bool IsRunning { get; private set; }
    public static string LastReportDir { get; private set; }
    public static string LastMessage { get; private set; }
    public static readonly List<AutoPlayRunner> Runners = new List<AutoPlayRunner>();
    public static event Action Changed;

    private static CancellationTokenSource _cts;

    public static string ReportsRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AutoPlayReports");

    /// <summary>
    /// Jev のコスト見積り（1ラン・1ペルソナあたり）。1リクエスト ≒ 4k tokens、
    /// 営業日 + 配信日 + レリック3択の回数ぶん。実測で差し替えること。
    /// </summary>
    public static double EstimateJevCostUsd(AutoPlayBatchConfig c)
    {
        int days = c.Mode switch { GameModeId.Short => 8, GameModeId.Medium => 16, _ => 30 };
        int streams = c.Mode switch { GameModeId.Short => 3, GameModeId.Medium => 5, _ => 8 };
        int requests = days + streams + streams; // レリックは勝利・返済ごと（多めに見積もる）
        long tokens = requests * 5000L; // 2026-10-05 実測 4.8k tokens/req（配信日は介入メニューのぶん少し増える）
        int control = c.EnableInterventions && c.ControlRunsWithoutInterventions && c.ControlRunsIncludeJev ? 2 : 1;
        return JevApi.EstimateCostUsd(tokens) * c.Seeds * c.JevPersonas.Count * control;
    }

    public static void Cancel()
    {
        _cts?.Cancel();
        LastMessage = "キャンセル要求";
        Changed?.Invoke();
    }

    /// <summary>バッチを開始する（非同期・投げっぱなし）。終わると AutoPlayReports/ にレポートが出る。</summary>
    public static async void Start(AutoPlayBatchConfig config)
    {
        if (IsRunning)
        {
            Debug.LogWarning("[AutoPlay] すでに実行中です。");
            return;
        }

        IsRunning = true;
        Runners.Clear();
        LastMessage = "準備中";
        Changed?.Invoke();
        _cts = new CancellationTokenSource();

        string batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + config.Mode;
        string saveRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "AutoPlay", batchId);
        var prevFilter = Debug.unityLogger.filterLogType;

        try
        {
            var assets = AutoPlayAssets.Load(out var error);
            if (assets == null)
            {
                LastMessage = error;
                Debug.LogError("[AutoPlay] " + error);
                return;
            }

            var personas = config.JevPersonas ?? new List<string>();
            if (personas.Count > 0 && !JevApi.HasApiKey)
            {
                Debug.LogWarning("[AutoPlay] 環境変数 TYPESAFE_API_KEY が無いため Jev ボットを除外します（Unity Hub ごと再起動が必要な場合あり）。");
                personas = new List<string>();
            }

            for (int i = 0; i < config.Seeds; i++)
            {
                int seed = config.BaseSeed + i;
                // (ボット, 介入を使うか)。対照群は名前に +noint を付ける
                var bots = new List<(IAutoPlayBot bot, bool interventions)>();
                if (config.UseGreedyBot) bots.Add((new AutoPlayGreedyBot(), config.EnableInterventions));
                if (config.UseRandomBot) bots.Add((new AutoPlayRandomBot(seed), config.EnableInterventions));
                foreach (var p in personas)
                    bots.Add((new AutoPlayJevBot(AutoPlayPersona.Find(p), config.JevDecisionMode, seed), config.EnableInterventions));
                if (config.EnableInterventions && config.ControlRunsWithoutInterventions)
                {
                    if (config.UseGreedyBot) bots.Add((new AutoPlayRenamedBot(new AutoPlayGreedyBot(), NoInterventionSuffix), false));
                    if (config.UseRandomBot) bots.Add((new AutoPlayRenamedBot(new AutoPlayRandomBot(seed), NoInterventionSuffix), false));
                    if (config.ControlRunsIncludeJev)
                        foreach (var p in personas)
                            bots.Add((new AutoPlayRenamedBot(new AutoPlayJevBot(AutoPlayPersona.Find(p), config.JevDecisionMode, seed), NoInterventionSuffix), false));
                }

                foreach (var (bot, interventions) in bots)
                {
                    string runId = $"{bot.Name}_s{seed}";
                    var ctx = new AutoPlayRunContext(runId, Path.Combine(saveRoot, runId), seed);
                    var game = new AutoPlayHeadlessGame(new AutoPlayHeadlessGame.Settings
                    {
                        Mode = config.Mode,
                        Seed = seed,
                        InitMoneyOverride = config.InitMoneyOverride,
                        StreamSalesScale = config.StreamSalesScale,
                        StreamMaxKinds = config.StreamMaxKinds,
                        ProbabilisticBattle = config.ProbabilisticBattle,
                        EnableInterventions = interventions,
                        ViewerSuperChat = config.ViewerSuperChat,
                        ViewerRedChancePerTurn = config.ViewerRedChancePerTurn,
                        SecondsPerBattleTurn = config.SecondsPerBattleTurn,
                    }, assets, ctx);
                    Runners.Add(new AutoPlayRunner(game, bot, ctx, config.MaxStepsPerRun));
                }
            }

            if (Runners.Count == 0)
            {
                LastMessage = "実行するボットがありません。";
                return;
            }

            // --- グローバルなフックを張る（終了時に必ず外す） ---
            SceneTransitionService.DevSceneLoadInterceptor = OnSceneLoad;
            Application.logMessageReceived += OnLog;
            if (!config.VerboseLogs) Debug.unityLogger.filterLogType = LogType.Warning;

            LastMessage = $"実行中 {Runners.Count} ラン";
            Changed?.Invoke();

            var gate = new SemaphoreSlim(Mathf.Max(1, config.MaxConcurrent));
            var tasks = Runners.Select(async r =>
            {
                await gate.WaitAsync(_cts.Token);
                try
                {
                    await r.RunAsync(_cts.Token);
                }
                finally
                {
                    gate.Release();
                    Changed?.Invoke();
                }
            }).ToList();

            var all = Task.WhenAll(tasks);
            if (config.CostLimitUsd > 0) _ = WatchCostAsync(all, config.CostLimitUsd);

            try
            {
                await all;
            }
            catch (OperationCanceledException)
            {
                // 待機中に取り消されたラン。結果は Cancelled のまま書き出す
            }

            Debug.unityLogger.filterLogType = prevFilter;
            var results = Runners.Select(r => r.Result).ToList();
            foreach (var r in results.Where(r => r.Outcome == "Running")) r.Outcome = "Cancelled";
            LastReportDir = AutoPlayReport.Write(Path.Combine(ReportsRoot, batchId), config, results);
            LastMessage = $"完了: {results.Count} ラン → {LastReportDir}";
            Debug.Log($"[AutoPlay] {LastMessage}");
        }
        catch (Exception e)
        {
            LastMessage = "失敗: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            Debug.unityLogger.filterLogType = prevFilter;
            Application.logMessageReceived -= OnLog;
            SceneTransitionService.DevSceneLoadInterceptor = null;
            SaveSlotManager.DevRootOverride = null;
            TryDeleteDirectory(saveRoot);
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Jev の累計コストが上限を超えたらバッチを止める。</summary>
    private static async Task WatchCostAsync(Task all, double limitUsd)
    {
        while (!all.IsCompleted)
        {
            await Task.Delay(1000);
            double usd = JevApi.EstimateCostUsd(Runners.Sum(r => r.LiveInputTokens));
            if (usd > limitUsd)
            {
                Debug.LogWarning($"[AutoPlay] Jev のコストが上限 ${limitUsd:F3} を超えたため中止します（${usd:F3}）。");
                LastMessage = $"コスト上限で中止: ${usd:F3}";
                _cts?.Cancel();
                return;
            }
        }
    }

    private static bool OnSceneLoad(string sceneName)
    {
        var ctx = AutoPlayRunContext.Current;
        if (ctx == null)
        {
            // ラン外（ユーザー操作など）のシーンロードは素通しする
            return false;
        }
        ctx.LastSceneRequest = sceneName;
        return true;
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Log) return;
        var ctx = AutoPlayRunContext.Current;
        if (ctx == null) return;
        string msg = type == LogType.Exception ? condition + "\n" + stackTrace : condition;
        // 自分（AutoPlay）が出した Jev の警告はボット側で数えているので除外
        if (condition != null && condition.StartsWith("[AutoPlay]")) return;
        ctx.CapturedLogs.Add((type, msg));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AutoPlay] 一時セーブの削除に失敗: {e.Message}");
        }
    }

    // =================================================================
    // メニュー（unity CLI の ExecuteMenuItem からも叩ける）
    // =================================================================

    [MenuItem("Tools/TomsLands/Jev AutoPlay クイック試走（貪欲+ランダム×3シード・Short）", priority = 1)]
    private static void QuickBaseline()
    {
        Start(new AutoPlayBatchConfig { Mode = GameModeId.Short, Seeds = 3, UseGreedyBot = true, UseRandomBot = true });
    }

    [MenuItem("Tools/TomsLands/Jev AutoPlay レポートフォルダを開く", priority = 2)]
    private static void OpenReports()
    {
        Directory.CreateDirectory(ReportsRoot);
        EditorUtility.RevealInFinder(LastReportDir ?? ReportsRoot);
    }
}
