#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 人格プレイ（ずんだもん / 四国めたん）。判断はローカルの Jev 互換モデル（Ollama の /v1/systemone）に聞き、
/// Play モードの実画面を AutoPlayController に押させる（Editor の AutoPlayJevBot は使わない）。
///
/// - IAutoPlayStrategy のメソッドは同期なので、仕入れの前（その日の判断）と配信の開始（配信の判断）で 1 リクエストずつ先に聞き、
///   答えをその場で決め切ってキャッシュする。失敗・時間切れは標準戦略（DefaultAutoPlayStrategy）で代替し、局面ごとに 1 回だけログを出す。
/// - 人格の違い: state の英語の前置き、抽選か最尤か、自信過剰の上乗せ、現金の下限、学び方（PersonaProfile）。
/// - 成長: UserSettings/PersonaMemory/&lt;persona&gt;.json に配信・ランの結果と短い教訓を足し、次から state に載せる。
/// - 学習（PersonaLearning）: 判断ごとに盤面の特徴を取り、結果が出たら想起の履歴（&lt;persona&gt;_recall.json）と
///   学習器（&lt;persona&gt;_model.json）に足す。似た場面を state.similarPastSituations に載せ、kev の確率を学習器で寄せる。
/// - 一言: 判断ごとに Recordings/persona_log_*.jsonl（PersonaLines のテンプレート）。
/// 設定は UserSettings/PersonaPlay.json（PersonaPlayConfig）。
/// </summary>
public class PersonaStrategy : DefaultAutoPlayStrategy, IAutoPlayJikkyoExtras, IAutoPlayPrefetchStrategy, IAutoPlayShopInvestor
{
    private readonly PersonaProfile _p;
    private PersonaPlayConfig _config;
    private PersonaMemory _memory;
    private PersonaLog _log;
    private System.Random _rng = new();
    private System.Random _sessionKey;
    /// <summary>一言のミーム（辞書・このランで使ったミームと回数）。ランの始まりに ResetRun。</summary>
    private PersonaLines.MemeRun _memes;

    // ---- その日の判断（PrefetchShopDayAsync で決め切る） ----
    private int _dayTurn = int.MinValue;
    private bool _dayFailed;
    private float? _budgetFrac;
    private AutoBuyStrategy? _buyStyle;
    private bool? _readNewspaper;
    private bool? _buyIntel;
    private int? _supportLevels;
    private float? _focusShare;
    private List<KeyValuePair<string, float>> _weaponShares;
    private int _supportedTurn = int.MinValue;
    private string _dayChoiceSummary = "";

    // ---- 店への投資（rules 4.2。その日の判断で決め、AutoPlayController が実画面を押す） ----
    private bool _planShopUpgrade, _planSmithUpgrade, _planBrokerUpgrade;
    private string _planMachine, _planFinance, _planWeapon, _planArmor, _planTactics;
    private float _planFinanceShare;
    private PersonaShopView _dayShop;
    private readonly Dictionary<string, PersonaDecisionRecord> _shopRecs = new();
    /// <summary>
    /// 店への投資の質問。「たいていは見送る」のが自然な質問なので、自由な質問の固まり対策（エントロピーの下限・安全弁）には入れない
    /// （入れると毎日半分の確率で改装・購入してしまう）。代わりに kev の確率に「ときどき試す」の下限を足す（TryFloor）。
    /// </summary>
    public static readonly HashSet<string> ShopQuestions = new() { "shop_upgrade", "smith_upgrade", "broker_upgrade", "machine", "finance", "finance_share" };
    /// <summary>「ときどき試す」: 真偽の質問は true の確率をこれより下げない、選択肢の質問は一様分布をこの割合だけ混ぜる。</summary>
    public const float TryFloorNoul = 0.12f, TryMixChoice = 0.15f;

    // ---- 配信の判断（OnStreamStarted で裏で聞く） ----
    private bool _streamPending;
    private bool _streamFailed;
    private string _streamSide;
    private Dictionary<string, float> _interveneWeights;
    private float _intensity;
    private int _streamGeneration;
    private readonly Dictionary<string, int> _usedInterventions = new();
    private bool _streamOutcomePending;
    private int _streamDay;
    private int _streamStartMoney;
    private string _pendingIntervention;
    private int _pendingSpent;
    // 配信リザルトの時点で BattleOutputData から写した結果（店に戻ると片付けられて 0 になるため）
    private bool _outcomeCaptured;
    private string _capturedResult;
    private int _capturedDelta;
    private int _capturedReward;
    private string _capturedDungeon;

    // ---- 学習（PersonaLearning） ----
    private PersonaRecallStore _recall;
    private PersonaReweighter _model;
    private readonly List<PersonaDecisionRecord> _pending = new();
    private float[] _dayFeatures;
    private float[] _streamFeatures;
    private List<string> _dayRecalled;
    private List<string> _streamRecalled;
    private int _dayWorth;
    private int _decisionSeq;
    private bool _streamResolvedSinceDay;
    private int _nextStreamLevel;
    private bool _nextStreamWeaknessKnown;
    private const int MaxPending = 400;   // 店への投資の判断はランの終わりまで結果待ち（1 日 4〜5 件）
    /// <summary>結果の一言でミームの確率を上げる「大きく動いた」損益（配信 1 回 / ラン 1 回、G）。</summary>
    private const int BigSwingStreamGold = 20000;
    private const int BigSwingRunGold = 50000;

    /// <summary>ラン（1 周）が終わった（人格 ID・破産したか・所持金・最初の営業日からの増減）。Editor の PersonaMarathon が周回を数える。</summary>
    public static event Action<string, bool, int, int> RunFinished;

    // ---- 村の判断（PrefetchVillageAsync で決め切る。セーブは人格ごとに続けて遊ぶので村が育つ） ----
    private bool _villageDecided;
    private readonly HashSet<string> _villagePlan = new();
    private readonly Dictionary<string, int> _villageLevelsBefore = new();
    private readonly Dictionary<string, PersonaDecisionRecord> _villageRecords = new();
    private int _villageFundsBefore;
    private string _villageBudgetPct = "";

    // ---- 一言で言う額（割合ではなく、判断時点の G で言う。視聴者が画面の数字と結びつけられるように） ----
    private int _spokenBudgetBase = -1;     // 仕入れに使える額（判断時点。予算の段階 × これ = 仕入れの予算）
    private int _spokenBudgetCap = -1;      // 現金の下限で削られたあとの上限（無ければ -1）
    private int _spokenFocusBase = -1;      // ダンジョン向けの割合を掛ける元（その日の仕入れの予算）
    private int _spokenVillageFunds = -1;   // 村資金
    private int _spokenVillageReserve;      // 村資金から残す額（自分の決まり）
    // 割合の決め台詞（「100%なのだ！」）は 1 ランに数回だけ。間を空ける
    public const int PercentLinesPerRun = 4;
    public const int PercentLineMinDays = 3;
    private int _percentLinesThisRun;
    private int _lastPercentLineDay = -999;

    // ---- 相談プレイ（PersonaConsult）。_advisor = 助言するだけの人格（ゲームは触らない） ----
    private readonly PersonaStrategy _advisor;
    private readonly bool _isAdvisor;
    private PersonaConsult _consult;
    private bool _sessionReady;
    /// <summary>直前に問い合わせたモデル（2 人に聞くとき、読み込み済みのモデルから先に聞いて入れ替えを減らす）。</summary>
    private static string _lastModelQueried;
    private Dictionary<string, PersonaJevAnswer> _advStreamAnswers;
    private Dictionary<string, float> _advInterveneWeights;
    private float _advIntensity;
    private int _streamConsults;
    private bool _consultedFirstIv, _consultedHpLow, _consultedBoss;
    private const int MaxStreamConsults = 3;

    // ---- 気分（PersonaMood。ランの始まりに基準点へ戻す） ----
    private readonly PersonaMood _mood;
    /// <summary>このランの自分の答え（質問 → 選択肢 → 回数）。固まりの安全弁（自由な質問の最多の手が 85% 超え → 次のランは技能の重みを半分）。</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _runPicks = new();

    // ---- ラン ----
    private int _runStartMoney = -1;
    private int _lastMoney;
    private int _lastTurn;
    private int _invested;
    private readonly HashSet<string> _warned = new();

    /// <param name="advisor">相談プレイ: この人格が遊び、advisor に判断ごとに相談する（null なら 1 人で遊ぶ）。</param>
    public PersonaStrategy(PersonaProfile profile, PersonaProfile advisor = null)
    {
        _p = profile ?? PersonaProfile.Zundamon;
        _mood = new PersonaMood(_p.Traits);
        if (_p.CashFloor > 0) StreamReserveGold = Mathf.Max(StreamReserveGold, _p.CashFloor);
        if (_p.Id == PersonaProfile.Zundamon.Id) StreamReserveGold = 10000;   // 配信用の手持ちはほぼ残さない
        if (advisor != null && advisor.Id != _p.Id) _advisor = new PersonaStrategy(advisor, true);
    }

    private PersonaStrategy(PersonaProfile profile, bool isAdvisor) : this(profile, (PersonaProfile)null)
    {
        _isAdvisor = isAdvisor;
    }

    public PersonaProfile Profile => _p;

    /// <summary>今の気分（相談プレイの掛け合いも読む）。</summary>
    public PersonaMood Mood => _mood;

    /// <summary>相談プレイか（ずんだもんが遊び、めたんに相談する）。</summary>
    public bool IsConsult => _advisor != null;

    /// <summary>相談プレイの会話ログ（相談プレイでなければ null）。</summary>
    public string ConsultLogPath => _consult?.LogPath;

    public override string Name => _advisor != null
        ? $"人格 相談（{_p.LabelJa}が遊び {_advisor._p.LabelJa}に相談・{Model ?? "未設定"} / {_advisor.Model ?? "未設定"}）"
        : $"人格 {_p.LabelJa}（{Model ?? "モデル未設定"}）";

    public bool IsThinking { get; private set; }

    /// <summary>今のセッションの判断ログ（まだ作っていなければ null）。</summary>
    public string LogFilePath => _log?.FilePath;

    private string Model => (_config ??= PersonaPlayConfig.Load()).ModelFor(_p.Id);

    // =====================================================
    // セッション（AutoPlay.Start ごと）
    // =====================================================

    private void EnsureSession()
    {
        if (_sessionReady && ReferenceEquals(_sessionKey, AutoPlay.Random)) return;
        _sessionReady = true;
        _sessionKey = AutoPlay.Random;
        _config = PersonaPlayConfig.Load();
        _memory = PersonaMemory.Load(_p.Id);
        _log = _isAdvisor ? null : new PersonaLog();   // 助言役は判断ログを書かない（会話は相談ログに出る）
        _recall = PersonaRecallStore.Load(_p.Id);
        _model = PersonaReweighter.Load(_p);
        _pending.Clear();
        _decisionSeq = 0;
        _streamResolvedSinceDay = false;
        _dayFeatures = null;
        _streamFeatures = null;
        int seed = AutoPlay.Random != null ? AutoPlay.Random.Next() : Environment.TickCount;
        _rng = new System.Random(seed);
        // ミームの抽選は別系統の乱数（同じ種から作るので再現でき、判断・テンプレートの乱数列はミームの有無で変わらない）
        _memes = new PersonaLines.MemeRun(PersonaMemeLexicon.Load(_config.MemeLexiconPath), new System.Random(seed ^ 0x4D454D45));
        _dayTurn = int.MinValue;
        _supportedTurn = int.MinValue;
        _runStartMoney = -1;
        _invested = 0;
        _villageDecided = false;
        _villagePlan.Clear();
        _villageRecords.Clear();
        _warned.Clear();
        ClearStream();
        _streamOutcomePending = false;
        if (_isAdvisor) return;
        if (_advisor != null)
        {
            _advisor.EnsureSession();
            _consult = new PersonaConsult(new System.Random(seed ^ 0x434F4E53), _memes, _memory, _advisor._memory, _mood, _advisor._mood);
        }
        AutoPlay.AddLog($"人格 {_p.LabelJa}: モデル {Model} / {_config.Endpoint} / 記憶 {_memory.Outcomes.Count} 件 / 想起 {_recall.Records.Count} 件 / " +
                        $"学習 {_model.Updates} 回（λ {_p.LearningRate:0.##}）/ ミーム {(_memes.Enabled ? _memes.Lexicon.Memes.Count + " 件" : "なし")} / ログ {_log.FilePath}");
        if (_advisor != null)
            AutoPlay.AddLog($"相談プレイ: {_advisor._p.LabelJa}（{_advisor.Model}）に相談 / 記憶 {_advisor._memory.Outcomes.Count} 件 / " +
                            $"学習 {_advisor._model.Updates} 回 / {_consult.Summary}");
    }

    private List<string> Lessons() =>
        _memory.LatestLessons(Mathf.Min(_p.MaxLessons, Mathf.Max(0, _config.LessonsInState)));

    // =====================================================
    // 先読み: その日の判断
    // =====================================================

    public async UniTask PrefetchShopDayAsync(AutoPlayContext ctx, AutoPlayStreamPrepContext prep, CancellationToken ct)
    {
        EnsureSession();
        FinalizeStreamOutcome();

        var snapshot = BuildDaySnapshot(ctx, prep);
        if (_runStartMoney < 0)
        {
            _runStartMoney = snapshot.Cash;
            _memes?.ResetRun();   // ミームの回数・使ったミームはランごと
            _consult?.ResetRun();
            _mood.ResetRun();     // 気分はランの始まりに基準点へ
            _advisor?._mood.ResetRun();
            _runPicks.Clear();
            _advisor?._runPicks.Clear();
        }
        _lastMoney = snapshot.Cash;
        _lastTurn = ctx.Turn;
        _lastDebtDays = snapshot.DebtDaysLeft;
        _lastDebtAmount = snapshot.DebtAmount;

        // 学習: 前の判断の結果（資産の増減）を付けてから、今日の盤面の特徴を取る
        int worth = Worth(snapshot);
        bool afterStream = _streamResolvedSinceDay;
        ResolvePending(r => r.Horizon == PersonaFeatures.HorizonNextDay || (afterStream && r.Horizon == PersonaFeatures.HorizonAfterStream),
            r => worth - r.StartWorth);
        _streamResolvedSinceDay = false;
        _dayWorth = worth;
        var next = snapshot.NextStream;
        if (next != null)
        {
            _nextStreamLevel = next.Level;
            _nextStreamWeaknessKnown = next.WeaknessKnown;
        }
        _dayFeatures = PersonaFeatures.ForDay(ctx.Turn, snapshot.Cash, snapshot.DebtAmount, snapshot.DebtDaysLeft, next?.DaysUntil ?? -1,
            next?.WeaknessKnown ?? false, next?.Level ?? 0, HeroLevel(), worth > 0 ? (worth - snapshot.Cash) / (float)worth : 0f, snapshot.ShopLevel,
            snapshot.MaxVillageLevel > 0 ? snapshot.VillageLevel / (float)snapshot.MaxVillageLevel : 0f);

        ResetDay(ctx.Turn);
        var req = PersonaRequests.BuildDayRequest(_p, Model, Lessons(), snapshot);
        _dayRecalled = Recall(PersonaFeatures.Day, _dayFeatures, req.Questions.Keys);
        PersonaRequests.AttachSimilarPast(req, _dayRecalled);
        PersonaJevRequest advReq = null;
        if (_advisor != null)
        {
            advReq = PersonaRequests.BuildDayRequest(_advisor._p, _advisor.Model, _advisor.Lessons(), snapshot);
            PersonaRequests.AttachSimilarPast(advReq, _advisor.Recall(PersonaFeatures.Day, _dayFeatures, advReq.Questions.Keys));
        }
        IsThinking = true;
        AutoPlay.Status = _advisor != null ? $"{_p.LabelJa} が {_advisor._p.LabelJa} に相談中（T{ctx.Turn}）" : $"{_p.LabelJa} が考え中（T{ctx.Turn}）";
        PersonaJevResponse resp, advResp;
        try
        {
            (resp, advResp) = await AskPairAsync(req, advReq, ct, "day", ctx.Turn);
        }
        finally
        {
            IsThinking = false;
        }
        if (resp == null)
        {
            _dayFailed = true;
            return;
        }
        ResolveDay(ctx, snapshot, resp, advResp);
    }

    /// <summary>
    /// 自分（と相談相手）のモデルに聞く。相談相手が先に読み込まれていればそちらから聞く（GPU でモデルの入れ替えを 1 回に減らす）。
    /// 自分が失敗したら Fallback（標準戦略）、相談相手だけ失敗したら相談なしで続ける。
    /// </summary>
    private async UniTask<(PersonaJevResponse own, PersonaJevResponse adv)> AskPairAsync(PersonaJevRequest own, PersonaJevRequest adv,
        CancellationToken ct, string phase, int day, Func<bool> stillWanted = null)
    {
        PersonaJevResponse a = null, b = null;
        bool advFirst = adv != null && _lastModelQueried == adv.Model && own.Model != adv.Model;
        if (advFirst) b = await AskAsync(adv, ct, phase, day, true, stillWanted);
        a = await AskAsync(own, ct, phase, day, false, stillWanted);
        if (adv != null && !advFirst && a != null) b = await AskAsync(adv, ct, phase, day, true, stillWanted);
        return (a, b);
    }

    private async UniTask<PersonaJevResponse> AskAsync(PersonaJevRequest req, CancellationToken ct, string phase, int day, bool advisor, Func<bool> stillWanted)
    {
        try
        {
            var r = await PersonaJevClient.SendAsync(req, _config, ct);
            _lastModelQueried = req.Model;
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            if (stillWanted != null && !stillWanted()) return null;
            if (advisor)
            {
                if (_warned.Add("advisor_" + phase))
                {
                    Debug.LogWarning($"[PersonaPlay] 相談相手 {_advisor._p.LabelJa}: {phase} の問い合わせに失敗したので相談なしで続けます: {e.Message}");
                    AutoPlay.AddLog($"相談プレイ: {_advisor._p.LabelJa} に聞けなかった（{phase}: {e.Message}）→ {_p.LabelJa} だけで決める");
                }
            }
            else Fallback(phase, day, e.Message);
            return null;
        }
    }

    private void ResetDay(int turn)
    {
        _dayTurn = turn;
        _dayFailed = false;
        _budgetFrac = null;
        _buyStyle = null;
        _readNewspaper = null;
        _buyIntel = null;
        _supportLevels = null;
        _focusShare = null;
        _weaponShares = null;
        _dayChoiceSummary = "";
        _planShopUpgrade = _planSmithUpgrade = _planBrokerUpgrade = false;
        _planMachine = _planFinance = _planWeapon = _planArmor = _planTactics = null;
        _planFinanceShare = 0f;
        _shopRecs.Clear();
    }

    private void ResolveDay(AutoPlayContext ctx, PersonaDaySnapshot s, PersonaJevResponse resp, PersonaJevResponse advResp)
    {
        var a = resp.Answers;
        var adv = advResp?.Answers;
        var summary = new List<string>();
        int day = ctx.Turn;
        var f = _dayFeatures;
        var n = s.NextStream;
        var sit = new PersonaSituation
        {
            Kind = PersonaFeatures.Day, Features = f, Cash = s.Cash,
            IntelCostShare = n != null && n.IntelCost > 0 && s.Cash > 0 ? n.IntelCost / (float)s.Cash : -1f,
            SupportGainRatio = n != null && n.SupportCost > 0 ? (n.RewardAfterSupport - n.DefeatReward) / (float)n.SupportCost : -1f,
        };
        var facts = new ConsultFacts
        {
            Cash = s.Cash, DebtAmount = s.DebtAmount, DebtDaysLeft = s.DebtDaysLeft,
            StreamDays = n?.DaysUntil ?? -1, WeaknessKnown = n?.WeaknessKnown ?? false, Weakness = n?.Weakness,
            IntelCost = n?.IntelCost ?? -1, SupportCost = n?.SupportCost ?? -1, DefeatReward = n?.DefeatReward ?? -1,
            RewardAfterSupport = n?.RewardAfterSupport ?? -1,
        };
        var lat = (resp.LatencyMs, advResp?.LatencyMs);

        if (a.TryGetValue("budget", out var budget))
        {
            var own = PickQuestion("budget", budget, sit);
            _spokenBudgetBase = SpendableAfterStreamReserve(ctx);
            _spokenBudgetCap = _p.CashFloor > 0 ? Mathf.Max(0, ctx.Money - Mathf.Max(_p.CashFloor, ctx.TaxReserve)) : -1;
            string line = PersonaLines.Pick(_p.Id, "budget", BandOf(budget), null, SpokenPct("budget", own.Key), _rng, _memes, _mood.Label);
            string key = Consult("budget", budget, own, line, adv, sit, facts, "day", day, lat, out var bind);
            int level = int.TryParse(key, out var lv) ? lv : 0;
            _budgetFrac = level / (float)(PersonaRequests.BudgetLevels.Length - 1);
            summary.Add("budget=" + LinePct("budget", key));
            var rec = Decide("budget", key, PersonaFeatures.Day, PersonaFeatures.HorizonNextDay, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonNextDay);
            Log(day, resp, "budget", key, budget, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("buy_style", out var style))
        {
            var own = PickQuestion("buy_style", style, sit);
            string line = PersonaLines.Pick(_p.Id, "buy_style", BandOf(style), PersonaLines.ChoiceJa("buy_style", own.Key), null, _rng, _memes, _mood.Label);
            string key = Consult("buy_style", style, own, line, adv, sit, facts, "day", day, lat, out var bind);
            _buyStyle = key switch
            {
                "bargain" => AutoBuyStrategy.Bargain,
                "dividend" => AutoBuyStrategy.Dividend,
                "dungeon_focus" => AutoBuyStrategy.DungeonFocus,
                _ => AutoBuyStrategy.Recommend,
            };
            summary.Add("buy=" + key);
            var rec = Decide("buy_style", key, PersonaFeatures.Day, PersonaFeatures.HorizonNextDay, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonNextDay);
            Log(day, resp, "buy_style", key, style, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("newspaper", out var news))
        {
            var own = PickQuestion("newspaper", news, sit);
            string line = PersonaLines.Pick(_p.Id, own.Key == "true" ? "newspaper_yes" : "newspaper_no", BandOf(news), null, null, _rng, _memes, _mood.Label);
            string key = Consult("newspaper", news, own, line, adv, sit, facts, "day", day, lat, out var bind);
            _readNewspaper = key == "true";
            var rec = Decide("newspaper", key, PersonaFeatures.Day, PersonaFeatures.HorizonNextDay, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonNextDay);
            Log(day, resp, "newspaper", key, news, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("intel", out var intel))
        {
            var own = PickQuestion("intel", intel, sit);
            string line = PersonaLines.Pick(_p.Id, own.Key == "true" ? "intel_yes" : "intel_no", BandOf(intel), null, null, _rng, _memes, _mood.Label);
            string key = Consult("intel", intel, own, line, adv, sit, facts, "day", day, lat, out var bind);
            bool buy = key == "true";
            _buyIntel = buy;
            if (buy) _nextStreamWeaknessKnown = true;
            summary.Add("intel=" + (buy ? "yes" : "no"));
            var rec = Decide("intel", key, PersonaFeatures.Day, PersonaFeatures.HorizonAfterStream, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonAfterStream);
            Log(day, resp, "intel", key, intel, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("support", out var support))
        {
            var own = PickQuestion("support", support, sit);
            string line = PersonaLines.Pick(_p.Id, "support", BandOf(support), Label("support", own.Key), null, _rng, _memes, _mood.Label);
            string key = Consult("support", support, own, line, adv, sit, facts, "day", day, lat, out var bind);
            int level = int.TryParse(key, out var lv) ? lv : 0;
            _supportLevels = level >= PersonaRequests.SupportLevels.Length - 1 ? 99 : level;
            summary.Add("support=" + (level >= 3 ? "max" : level.ToString()));
            var rec = Decide("support", key, PersonaFeatures.Day, PersonaFeatures.HorizonAfterStream, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonAfterStream);
            Log(day, resp, "support", key, support, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("focus_share", out var share))
        {
            var own = PickQuestion("focus_share", share, sit);
            _spokenFocusBase = _budgetFrac.HasValue && _spokenBudgetBase >= 0 ? SpokenBudgetGold(_budgetFrac.Value) : SpendableAfterStreamReserve(ctx);
            string line = PersonaLines.Pick(_p.Id, "focus_share", BandOf(share), null, SpokenPct("focus_share", own.Key), _rng, _memes, _mood.Label);
            string key = Consult("focus_share", share, own, line, adv, sit, facts, "day", day, lat, out var bind);
            int level = int.TryParse(key, out var lv) ? lv : 0;
            _focusShare = level / (float)(PersonaRequests.ShareLevels.Length - 1);
            summary.Add("focus=" + LinePct("focus_share", key));
            var rec = Decide("focus_share", key, PersonaFeatures.Day, PersonaFeatures.HorizonAfterStream, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonAfterStream);
            Log(day, resp, "focus_share", key, share, line, own, rec, _dayRecalled, bind);
        }

        if (a.TryGetValue("weapon", out var weapon) && weapon.Probabilities != null && weapon.Probabilities.Count > 0)
        {
            // ずんだもんは 1〜2 銘柄に集中、めたんは 3 銘柄に分ける（銘柄はランごとに変わるので学習器は使わない。相談もしない）
            int kinds = _p.Sample ? 2 : 3;
            _weaponShares = TopShares(weapon.Probabilities, 0.08f, kinds);
            if (_p.Sample)
            {
                string pick = PickChoice("weapon", weapon, null, null, null, out _);
                _weaponShares.RemoveAll(kv => kv.Key == pick);
                _weaponShares.Insert(0, new KeyValuePair<string, float>(pick, 1f));
                _weaponShares = Normalize(_weaponShares.Take(kinds).ToList(), first: 0.7f);
            }
            string top = _weaponShares.Count > 0 ? _weaponShares[0].Key : weapon.Choice;
            Log(day, resp, "weapon", top, weapon, PersonaLines.Pick(_p.Id, "weapon", BandOf(weapon), ItemNameJa(top), null, _rng, _memes, _mood.Label), null, null, null, null);
        }

        ResolveShopInvest(s, a, adv, sit, facts, day, lat, resp, summary);

        _dayChoiceSummary = string.Join(",", summary);
        AutoPlay.AddLog($"{_p.LabelJa}: T{day} の判断 {_dayChoiceSummary}（{resp.LatencyMs} ms" +
                        (advResp != null ? $" / {_advisor._p.LabelJa} {advResp.LatencyMs} ms" : "") + "）" +
                        (_dayRecalled != null && _dayRecalled.Count > 0 ? $" 想起 {_dayRecalled.Count} 件" : ""));
    }

    private bool HasDay(int turn) => _dayTurn == turn && !_dayFailed;

    /// <summary>配信日の朝は仕入れ前のフックが来ないので、前日の予算・方針をそのまま使う。</summary>
    private bool HasPlan(int turn) => !_dayFailed && (_dayTurn == turn || _dayTurn == turn - 1);

    // =====================================================
    // 先読み: 配信の判断
    // =====================================================

    public void OnStreamStarted(AutoPlayStreamContext ctx)
    {
        EnsureSession();
        ClearStream();
        _streamPending = true;
        _streamDay = AutoPlay.CurrentTurn;
        int generation = ++_streamGeneration;
        var snapshot = BuildStreamSnapshot(ctx);
        _lastMoney = ctx.AvailableBalance;
        _streamStartMoney = ctx.AvailableBalance;
        // 前の配信の結果が付かないまま残った判断は捨てる（別の配信の損益を付けないため）
        _pending.RemoveAll(r => r.Horizon == PersonaFeatures.HorizonStream);
        bool supportPaid = _supportedTurn != int.MinValue && _supportedTurn >= _streamDay - 1;
        _streamFeatures = PersonaFeatures.ForStream(snapshot.Day, snapshot.Balance, snapshot.HeroHpPct, snapshot.Monsters, snapshot.BossAlive,
            _nextStreamLevel, HeroLevel(), _nextStreamWeaknessKnown,
            snapshot.Balance > 0 ? snapshot.SpendBudget / (float)snapshot.Balance : 0f, supportPaid, VillageShareInRun());
        RequestStreamAsync(snapshot, generation, supportPaid).Forget();
    }

    private async UniTaskVoid RequestStreamAsync(PersonaStreamSnapshot snapshot, int generation, bool supportPaid)
    {
        var req = PersonaRequests.BuildStreamRequest(_p, Model, Lessons(), snapshot);
        _streamRecalled = Recall(PersonaFeatures.Stream, _streamFeatures, req.Questions.Keys);
        PersonaRequests.AttachSimilarPast(req, _streamRecalled);
        PersonaJevRequest advReq = null;
        if (_advisor != null)
        {
            advReq = PersonaRequests.BuildStreamRequest(_advisor._p, _advisor.Model, _advisor.Lessons(), snapshot);
            PersonaRequests.AttachSimilarPast(advReq, _advisor.Recall(PersonaFeatures.Stream, _streamFeatures, advReq.Questions.Keys));
        }
        PersonaJevResponse resp = null, advResp = null;
        IsThinking = true;
        try
        {
            (resp, advResp) = await AskPairAsync(req, advReq, CancellationToken.None, "stream", snapshot.Day, () => generation == _streamGeneration);
        }
        catch (Exception e)
        {
            if (generation == _streamGeneration) Fallback("stream", snapshot.Day, e.Message);
        }
        finally
        {
            IsThinking = false;
        }
        if (generation != _streamGeneration) return;   // 配信が終わった・次の配信が始まった
        _streamPending = false;
        if (resp == null)
        {
            _streamFailed = true;
            return;
        }
        ResolveStream(snapshot, resp, advResp, supportPaid);
    }

    private void ResolveStream(PersonaStreamSnapshot s, PersonaJevResponse resp, PersonaJevResponse advResp, bool supportPaid)
    {
        var a = resp.Answers;
        var adv = advResp?.Answers;
        _advStreamAnswers = adv;
        var f = _streamFeatures;
        var sit = new PersonaSituation { Kind = PersonaFeatures.Stream, Features = f, Cash = s.Balance };
        var facts = new ConsultFacts
        {
            Cash = s.Balance, HeroHpPct = s.HeroHpPct, Monsters = s.Monsters, Boss = s.BossAlive, SpendBudget = s.SpendBudget,
            SupportPaid = supportPaid, WeaknessKnown = _nextStreamWeaknessKnown,
        };
        var lat = (resp.LatencyMs, advResp?.LatencyMs);
        if (a.TryGetValue("stream_side", out var side))
        {
            var own = PickQuestion("stream_side", side, sit);
            string line = PersonaLines.Pick(_p.Id, "stream_side", BandOf(side), PersonaLines.ChoiceJa("stream_side", own.Key), null, _rng, _memes, _mood.Label);
            _streamSide = Consult("stream_side", side, own, line, adv, sit, facts, "stream", s.Day, lat, out var bind);
            var rec = Decide("stream_side", _streamSide, PersonaFeatures.Stream, PersonaFeatures.HorizonStream, f, 0);
            Bind(rec, bind, PersonaFeatures.HorizonStream);
            Log(s.Day, resp, "stream_side", _streamSide, side, line, own, rec, _streamRecalled, bind);
        }
        else _streamSide = "none";
        sit.Side = _streamSide;
        facts.Side = _streamSide;

        if (a.TryGetValue("intervene", out var iv) && iv.Probabilities != null)
        {
            // どのスパチャを実際に払ったかは配信の終わりに分かるので、学習の判断はそこで 1 件にまとめる（FinalizeStreamOutcome）
            var rw = Reweigh("intervene", iv.Probabilities, f);
            _interveneWeights = InterveneWeights(rw?.Final ?? iv.Probabilities, _streamSide);
            string top = TopKey(_interveneWeights);
            string line = PersonaLines.Pick(_p.Id, "intervene", BandOf(iv), PersonaLines.ChoiceJa("intervene", top), null, _rng, _memes, _mood.Label);
            ConsultBinding bind = null;
            if (_advisor != null && adv != null && adv.TryGetValue("intervene", out var aiv) && aiv.Probabilities != null)
            {
                var arw = _advisor.Reweigh("intervene", aiv.Probabilities, f);
                _advInterveneWeights = _advisor.InterveneWeights(arw?.Final ?? aiv.Probabilities, _streamSide);
                string mTop = TopKey(_advInterveneWeights);
                var own = new PickOutcome { Key = top, Why = "weights" };
                var advPick = new PickOutcome { Key = mTop, Why = "weights" };
                string key = ConsultWith("intervene", iv, aiv, own, advPick, line, sit, facts, "stream", s.Day, lat, out bind, voice: 1f);
                // めたんの案を聞いたら、配信中に投げる種類の重みもめたんのものにする
                if (bind != null && bind.Outcome.Followed && key == mTop) _interveneWeights = new Dictionary<string, float>(_advInterveneWeights);
                top = key;
            }
            Log(s.Day, resp, "intervene", top, iv, line, new PickOutcome { Key = top, Rw = rw }, null, _streamRecalled, bind);
        }

        if (a.TryGetValue("intensity", out var intensity))
        {
            var own = PickQuestion("intensity", intensity, sit);
            string line = PersonaLines.Pick(_p.Id, "intensity", BandOf(intensity), null, SpokenPct("intensity", own.Key), _rng, _memes, _mood.Label);
            string key = Consult("intensity", intensity, own, line, adv, sit, facts, "stream", s.Day, lat, out var bind);
            int level = int.TryParse(key, out var lv) ? lv : 0;
            _intensity = level / (float)(PersonaRequests.IntensityLevels.Length - 1);
            if (_streamSide == "none") _intensity *= 0.3f;
            var rec = Decide("intensity", key, PersonaFeatures.Stream, PersonaFeatures.HorizonStream, f, 0);
            Bind(rec, bind, PersonaFeatures.HorizonStream);
            Log(s.Day, resp, "intensity", key, intensity, line, own, rec, _streamRecalled, bind);
            if (_advisor != null && adv != null && adv.TryGetValue("intensity", out var aint))
            {
                var mp = _advisor.PickQuestion("intensity", aint, sit);
                _advIntensity = (int.TryParse(mp.Key, out var ml) ? ml : 0) / (float)(PersonaRequests.IntensityLevels.Length - 1);
            }
        }
        AutoPlay.AddLog($"{_p.LabelJa}: 配信は {_streamSide} に張る / スパチャの勢い {_intensity:P0}（{resp.LatencyMs} ms" +
                        (advResp != null ? $" / {_advisor._p.LabelJa} {advResp.LatencyMs} ms" : "") + "）");
    }

    /// <summary>スパチャの種類ごとの重み（張った側に合わない種類は薄く、ずんだもんは派手な一発が好き）。</summary>
    private Dictionary<string, float> InterveneWeights(Dictionary<string, float> probs, string side)
    {
        var w = new Dictionary<string, float>();
        foreach (var kv in probs)
        {
            if (kv.Key == "none") continue;
            float v = kv.Value;
            bool heroSide = kv.Key is "heal" or "skill" or "special";
            if (side == "hero" && !heroSide) v *= 0.2f;
            if (side == "demon" && heroSide) v *= 0.2f;
            if (_p.Sample && kv.Key is "special" or "bossbuff") v *= 1.5f;   // 派手な一発が好き
            w[kv.Key] = v;
        }
        return w;
    }

    private static string TopKey(Dictionary<string, float> weights) =>
        weights != null && weights.Count > 0 ? weights.OrderByDescending(kv => kv.Value).First().Key : "none";

    public void OnStreamEnded()
    {
        if (_streamSide != null || _streamFailed || _streamPending) _streamOutcomePending = true;
        CaptureStreamOutput();
        _streamGeneration++;
        _streamPending = false;
    }

    private void ClearStream()
    {
        _advStreamAnswers = null;
        _advInterveneWeights = null;
        _advIntensity = 0f;
        _streamConsults = 0;
        _consultedFirstIv = _consultedHpLow = _consultedBoss = false;
        _streamPending = false;
        _streamFailed = false;
        _streamSide = null;
        _interveneWeights = null;
        _intensity = 0f;
        _usedInterventions.Clear();
        _pendingIntervention = null;
        _pendingSpent = 0;
    }

    /// <summary>配信リザルトの時点（FightScene）で結果を写しておく。店に戻ると出力は片付けられて 0 になる。</summary>
    private void CaptureStreamOutput()
    {
        if (_outcomeCaptured) return;
        var output = AutoPlayUi.Resolve<BattleOutputData>();
        if (output == null) return;
        bool filled = output.Result == BattleResult.Defeat || output.TotalEarnings != 0 || output.DefenseReward != 0 || output.InterventionSpending != 0;
        if (!filled) return;
        _outcomeCaptured = true;
        _capturedResult = output.Result switch
        {
            BattleResult.Victory => "win",
            BattleResult.Defeat => "lose",
            _ => "aborted",
        };
        _capturedDelta = output.TotalEarnings + output.DefenseReward - output.InterventionNetSpending;
        _capturedReward = output.DefenseReward;
        _capturedDungeon = output.DungeonKey.ToString();
    }

    /// <summary>直前の配信の結果を記憶に足す（店に戻った・ラン終了の画面で呼ぶ）。写せていなければ所持金の差で代用する。</summary>
    private void FinalizeStreamOutcome()
    {
        if (!_streamOutcomePending || _memory == null) return;
        _streamOutcomePending = false;

        string result, dungeon;
        int delta, reward;
        if (_outcomeCaptured)
        {
            result = _capturedResult;
            delta = _capturedDelta;
            reward = _capturedReward;
            dungeon = _capturedDungeon;
        }
        else
        {
            var toms = AutoPlayUi.Resolve<TomsModel>();
            result = "unknown";
            delta = toms != null ? toms.PlayerMoney.Value - _streamStartMoney : 0;
            reward = 0;
            dungeon = "the dungeon";
        }
        _outcomeCaptured = false;
        string side = _streamSide ?? "none";
        string used = _usedInterventions.Count > 0
            ? string.Join("+", _usedInterventions.Select(kv => $"{kv.Key}x{kv.Value}"))
            : "none";
        string lesson = StreamLesson(_streamDay, dungeon, side, result, delta, reward, used);

        // 学習: この配信の判断に損益を付ける。スパチャは実際にいちばん多く払った種類（無ければ none）を 1 件の判断にする
        if (_interveneWeights != null && _streamFeatures != null)
        {
            string mostUsed = _usedInterventions.Count > 0 ? _usedInterventions.OrderByDescending(kv => kv.Value).First().Key : "none";
            var ivRec = Decide("intervene", mostUsed, PersonaFeatures.Stream, PersonaFeatures.HorizonStream, _streamFeatures, 0);
            if (ivRec != null)
                _log.Write(new PersonaLogEntry
                {
                    Day = _streamDay, Persona = _p.Id, Model = Model, Question = "intervene", Choice = mostUsed,
                    Rules = PersonaFeatures.Rules, DecisionId = ivRec.Id, Kind = ivRec.Kind, Horizon = ivRec.Horizon, Features = ivRec.Features,
                });
        }
        int streamDelta = delta;
        var moodBefore = _mood.Snapshot();
        ResolvePending(r => r.Horizon == PersonaFeatures.HorizonStream, _ => streamDelta);
        _streamResolvedSinceDay = true;
        var keys = new List<string> { "side:" + side };
        keys.AddRange(_usedInterventions.Keys.Select(k => "intervene:" + k));
        _memory.Append(new PersonaOutcome
        {
            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            Kind = "stream",
            Day = _streamDay,
            GoldDelta = delta,
            Result = result,
            Choice = $"side={side},intervene={used},{_dayChoiceSummary}",
            Lesson = lesson,
        }, keys);

        bool good = result == (side == "demon" ? "lose" : "win") || delta > 0;
        string line = PersonaLines.PickResult(_p.Id, "stream_result", good, _rng, _memes, Math.Abs(delta) >= BigSwingStreamGold);
        _log.Write(new PersonaLogEntry
        {
            Day = _streamDay, Persona = _p.Id, Model = Model, Question = "stream_result",
            Choice = $"{result} {delta:+#;-#;0}G side={side}", Line = line,
            Meme = _memes?.MemeFor(line), MemeCue = _memes?.CueFor(line),
            MoodBefore = moodBefore, MoodAfter = _mood.Snapshot(), Reward = delta,
            E = ResultExpression(good), Beat = good ? "win" : "cry",
        });
        AutoPlay.AddLog($"{_p.LabelJa}: 配信の結果 {result}（{delta:+#,0;-#,0;0}G）→ 記憶に追加「{lesson}」");
        ClearStream();
    }

    /// <summary>結果の一言の表情（ずんだもん: 勝ち → ama / ドヤなら tsun、負け → namida。めたん: 勝ち → normal、負け → tsun）。</summary>
    private string ResultExpression(bool good)
    {
        if (_p.Id == "metan") return good ? (_mood.Label == "smug" ? "tsun" : "normal") : "tsun";
        if (!good) return "namida";
        return _mood.Label == "smug" ? "tsun" : _mood.Conf >= 0.7f ? "surprise" : "ama";
    }

    private static string StreamLesson(int day, string dungeon, string side, string result, int delta, int reward, string used)
    {
        string net = $"{delta:+#;-#;0}G";
        if (side == "demon" && result == "lose")
            return $"Day {day}: backing the demon army at {dungeon} paid a {reward}G defeat reward (stream net {net}).";
        if (side == "demon" && result == "win")
            return delta >= 0
                ? $"Day {day}: the hero beat {dungeon} despite my demon bet, but sales kept the stream net {net}; the demon side only pays when the hero is weak."
                : $"Day {day}: the hero beat {dungeon} despite my demon bet (net {net}); the demon side only pays when the hero is weak.";
        if (side == "hero" && result == "lose")
            return delta >= 0
                ? $"Day {day}: the hero lost at {dungeon} although I backed him, but sales kept the stream net {net}; check the clear chance before paying super chats."
                : $"Day {day}: the hero lost at {dungeon} although I backed him (net {net}); check the clear chance before paying super chats.";
        if (side == "hero" && result == "win")
            return $"Day {day}: backing the hero at {dungeon} worked (net {net}, super chats {used}).";
        string what = result == "win" ? $"the hero cleared {dungeon}" : result == "lose" ? $"the hero lost at {dungeon}" : $"the {dungeon} stream ended ({result})";
        return $"Day {day}: I only watched and {what} (net {net}); a bet on the right side would have paid more.";
    }

    // =====================================================
    // 店への投資（勇者の装備・店の改装・鍛冶屋 / 情報屋の開発・設備・取引所。rules 4.2）
    // =====================================================

    /// <summary>その日の店への投資の質問に答える（聞いていなければ何もしない）。結果はランの終わり（装備は配信の翌朝）に付く。</summary>
    private void ResolveShopInvest(PersonaDaySnapshot s, Dictionary<string, PersonaJevAnswer> a, Dictionary<string, PersonaJevAnswer> adv,
        PersonaSituation daySit, ConsultFacts dayFacts, int day, (long? own, long? adv) lat, PersonaJevResponse resp, List<string> summary)
    {
        var v = s.Shop;
        _dayShop = v;
        if (v == null) return;
        var f = _dayFeatures;
        int keep = KeepCash(s);
        int room = Mathf.Max(0, s.Cash - keep);

        PersonaSituation Sit(bool? plausible, Dictionary<string, float> opt = null) => new PersonaSituation
        {
            Kind = PersonaFeatures.Day, Features = f, Cash = s.Cash, Plausible = plausible, OptionMult = opt,
            IntelCostShare = daySit.IntelCostShare, SupportGainRatio = daySit.SupportGainRatio,
        };
        ConsultFacts Facts(string name, int cost, int payback = -1) => new ConsultFacts
        {
            Cash = s.Cash, DebtAmount = s.DebtAmount, DebtDaysLeft = s.DebtDaysLeft, StreamDays = dayFacts.StreamDays,
            WeaknessKnown = dayFacts.WeaknessKnown, Weakness = dayFacts.Weakness, FacilityName = name, Cost = cost, PaybackDays = payback,
            Reserve = keep,
        };

        // ---- 勇者の装備（配信の翌朝までの資産で学ぶ） ----
        string Gear(string q, List<PersonaGearView> list, string current, out string chosen)
        {
            chosen = null;
            if (!a.TryGetValue(q, out var ans) || ans?.Probabilities == null || ans.Probabilities.Count == 0) return null;
            var opt = new Dictionary<string, float>();
            foreach (var g in list)
            {
                float m = 1f;
                if (g.WeakAttribute) m *= 1.6f;                  // めたん: 弱点の属性
                if (g.Stock > 0) m *= 1.3f;                      // 持っている品ほど勝ったときの値上がりで得をする
                if (g.Tier >= list.Max(x => x.Tier)) m *= 1.2f;  // いちばん強いランク
                opt[g.Id] = m;
            }
            var sit = Sit(null, opt);
            var own = PickQuestion(q, ans, sit);
            string nameJa = list.FirstOrDefault(g => g.Id == own.Key)?.Name ?? own.Key;
            string line = PersonaLines.Pick(_p.Id, own.Key == current ? q + "_keep" : q, BandOf(ans), nameJa, null, _rng, _memes, _mood.Label);
            var facts = Facts(nameJa, -1);
            facts.ItemName = nameJa;
            string key = Consult(q, ans, own, line, adv, sit, facts, "day", day, lat, out var bind);
            chosen = key;
            var rec = Decide(q, key == current ? "keep" : "change", PersonaFeatures.Day, PersonaFeatures.HorizonAfterStream, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonAfterStream);
            if (rec != null) _shopRecs[q] = rec;
            Log(day, resp, q, key, ans, line, own, rec, _dayRecalled, bind);
            return key;
        }
        if (a.ContainsKey("hero_weapon"))
        {
            string k = Gear("hero_weapon", v.Weapons, v.HeroWeaponId, out _);
            if (k != null && k != v.HeroWeaponId) _planWeapon = k;
            if (k != null) summary.Add("weapon=" + k);
        }
        if (a.ContainsKey("hero_armor"))
        {
            string k = Gear("hero_armor", v.Armors, v.HeroArmorId, out _);
            if (k != null && k != v.HeroArmorId) _planArmor = k;
            if (k != null) summary.Add("armor=" + k);
        }
        if (a.TryGetValue("hero_tactics", out var tac) && tac?.Probabilities != null)
        {
            var opt = new Dictionary<string, float> { ["Balanced"] = 1.2f, ["Defensive"] = 1.15f };   // めたん: 値動きを荒らさない
            var sit = Sit(null, opt);
            var own = PickQuestion("hero_tactics", tac, sit);
            string line = PersonaLines.Pick(_p.Id, "hero_tactics", BandOf(tac), Label("hero_tactics", own.Key), null, _rng, _memes, _mood.Label);
            string key = Consult("hero_tactics", tac, own, line, adv, sit, Facts(Label("hero_tactics", own.Key), -1), "day", day, lat, out var bind);
            if (key != v.HeroTactics) _planTactics = key;
            var rec = Decide("hero_tactics", key, PersonaFeatures.Day, PersonaFeatures.HorizonAfterStream, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonAfterStream);
            if (rec != null) _shopRecs["hero_tactics"] = rec;
            Log(day, resp, "hero_tactics", key, tac, line, own, rec, _dayRecalled, bind);
            summary.Add("tactics=" + key);
        }

        // ---- 店の改装・鍛冶屋 / 情報屋の開発（ランの終わりまでの資産で学ぶ） ----
        bool Upgrade(string q, int cost, string nameJa, bool plausible)
        {
            if (!a.TryGetValue(q, out var ans) || cost <= 0) return false;
            var sit = Sit(plausible);
            var own = PickQuestion(q, ans, sit);
            string line = PersonaLines.Pick(_p.Id, own.Key == "true" ? q + "_yes" : q + "_no", BandOf(ans), PersonaConsult.Money(cost), null,
                _rng, _memes, _mood.Label);
            string key = Consult(q, ans, own, line, adv, sit, Facts(nameJa, cost), "day", day, lat, out var bind);
            var rec = Decide(q, key, PersonaFeatures.Day, PersonaFeatures.HorizonRunEnd, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonRunEnd);
            if (rec != null) _shopRecs[q] = rec;
            Log(day, resp, q, key, ans, line, own, rec, _dayRecalled, bind);
            summary.Add(q.Replace("_upgrade", "") + "=" + (key == "true" ? "yes" : "no"));
            return key == "true";
        }
        // 元が取れそうか（めたんの見方）: 使ってよい額の 3 割以内で、返済が近くて重いときでない
        bool tight = s.DebtDaysLeft >= 0 && s.DebtDaysLeft <= 3 && s.DebtAmount > 0 && s.DebtAmount > s.Cash * 0.6f;
        bool Plausible(int cost) => !tight && cost > 0 && cost <= room * 0.3f;
        _planShopUpgrade = Upgrade("shop_upgrade", v.ShopUpgradeCost, "店の改装", Plausible(v.ShopUpgradeCost));
        _planSmithUpgrade = Upgrade("smith_upgrade", v.SmithUpgradeCost, "鍛冶屋の開発", Plausible(v.SmithUpgradeCost));
        _planBrokerUpgrade = Upgrade("broker_upgrade", v.BrokerUpgradeCost, "情報屋の開発", Plausible(v.BrokerUpgradeCost));

        // ---- 設備 ----
        if (a.TryGetValue("machine", out var mach) && mach?.Probabilities != null)
        {
            var opt = new Dictionary<string, float> { ["none"] = tight ? 2f : 1f };
            foreach (var m in v.Machines)
            {
                int payback = m.DailyGold > 0 ? (m.Cost + m.DailyGold - 1) / m.DailyGold : -1;
                // めたん: 元が取れる日数が分かる設備（お金の設備）で短いものを好み、高すぎるものは避ける
                float mul = payback > 0 ? (payback <= 7 ? 1.8f : payback <= 12 ? 1.2f : 0.7f) : 1f;
                if (m.Cost > room * 0.4f) mul *= 0.3f;
                opt[m.Id] = mul;
            }
            var sit = Sit(null, opt);
            var own = PickQuestion("machine", mach, sit);
            var pick = v.Machines.FirstOrDefault(m => m.Id == own.Key);
            string line = PersonaLines.Pick(_p.Id, own.Key == "none" ? "machine_no" : "machine_yes", BandOf(mach), pick?.Name,
                pick != null ? PersonaConsult.Money(pick.Cost) : null, _rng, _memes, _mood.Label);
            var facts = Facts(pick?.Name, pick?.Cost ?? -1, pick != null && pick.DailyGold > 0 ? (pick.Cost + pick.DailyGold - 1) / pick.DailyGold : -1);
            string key = Consult("machine", mach, own, line, adv, sit, facts, "day", day, lat, out var bind);
            _planMachine = key != "none" && v.Machines.Any(m => m.Id == key) ? key : null;
            var rec = Decide("machine", _planMachine != null ? MachineKind(key) : "none", PersonaFeatures.Day, PersonaFeatures.HorizonRunEnd, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonRunEnd);
            if (rec != null) _shopRecs["machine"] = rec;
            Log(day, resp, "machine", key, mach, line, own, rec, _dayRecalled, bind);
            summary.Add("machine=" + key);
        }

        // ---- 取引所（債券・投資信託）と額 ----
        if (a.TryGetValue("finance", out var fin) && fin?.Probabilities != null)
        {
            var opt = new Dictionary<string, float> { ["none"] = tight ? 2f : 1f };
            foreach (var p in v.Products)
            {
                opt[p.Id] = p.Bond ? 1.5f : 0.7f;   // めたん: 債券は堅い、ファンドは値動きが怖い
                if (!p.Bond && p.Held > 0) opt["sell_" + p.Id] = p.HeldValue > p.HeldCost ? 1.4f : 0.8f;   // 利が乗ったら売る
            }
            var sit = Sit(null, opt);
            var own = PickQuestion("finance", fin, sit);
            string nameJa = FinanceNameJa(v, own.Key);
            string line = PersonaLines.Pick(_p.Id, own.Key == "none" ? "finance_no" : own.Key.StartsWith("sell_") ? "finance_sell" : FinanceIsBond(v, own.Key) ? "finance_bond" : "finance_fund",
                BandOf(fin), nameJa, null, _rng, _memes, _mood.Label);
            var facts = Facts(nameJa, -1);
            facts.ItemName = nameJa;
            string key = Consult("finance", fin, own, line, adv, sit, facts, "day", day, lat, out var bind);
            _planFinance = key != "none" ? key : null;
            var rec = Decide("finance", FinanceKind(v, key), PersonaFeatures.Day, PersonaFeatures.HorizonRunEnd, f, _dayWorth);
            Bind(rec, bind, PersonaFeatures.HorizonRunEnd);
            if (rec != null) _shopRecs["finance"] = rec;
            Log(day, resp, "finance", key, fin, line, own, rec, _dayRecalled, bind);
            summary.Add("finance=" + key);

            // 額（買うときだけ聞いた答えを使う。売るときは全部売る）
            if (_planFinance != null && !_planFinance.StartsWith("sell_") && a.TryGetValue("finance_share", out var share))
            {
                _spokenFinanceBase = room;
                var shareSit = Sit(null, tight ? new Dictionary<string, float> { ["0"] = 1.6f, ["2"] = 0.6f, ["3"] = 0.3f } : null);
                var sOwn = PickQuestion("finance_share", share, shareSit);
                string sLine = PersonaLines.Pick(_p.Id, "finance_share", BandOf(share), nameJa, SpokenPct("finance_share", sOwn.Key), _rng, _memes, _mood.Label);
                string sKey = Consult("finance_share", share, sOwn, sLine, adv, shareSit, Facts(nameJa, -1), "day", day, lat, out var sBind);
                int level = int.TryParse(sKey, out var lv) ? lv : 0;
                _planFinanceShare = FinanceShareFrac(level);
                var sRec = Decide("finance_share", sKey, PersonaFeatures.Day, PersonaFeatures.HorizonRunEnd, f, _dayWorth);
                Bind(sRec, sBind, PersonaFeatures.HorizonRunEnd);
                if (sRec != null) _shopRecs["finance_share"] = sRec;
                Log(day, resp, "finance_share", sKey, share, sLine, sOwn, sRec, _dayRecalled, sBind);
                summary.Add("financeShare=" + LinePct("finance_share", sKey));
            }
            else if (_planFinance != null && !_planFinance.StartsWith("sell_")) _planFinanceShare = FinanceShareFrac(1);
        }
    }

    /// <summary>取引所の額の段階 → 使ってよい額のうちの割合（10% / 25% / 50% / 75%）。</summary>
    internal static float FinanceShareFrac(int level) => level switch { <= 0 => 0.10f, 1 => 0.25f, 2 => 0.5f, _ => 0.75f };

    /// <summary>手を付けない現金: 人格の下限（めたん 1 万G）・3 日以内の返済。</summary>
    private int KeepCash(PersonaDaySnapshot s)
    {
        int keep = Mathf.Max(0, _p.CashFloor);
        if (s.DebtDaysLeft >= 0 && s.DebtDaysLeft <= 3) keep = Mathf.Max(keep, s.DebtAmount + _p.CashFloor);
        return keep;
    }

    private static string MachineKind(string id)
    {
        var m = AutoPlayUi.Resolve<ShopMachineModel>()?.GetMachine(id);
        if (m == null) return "machine";
        return m.effectType switch
        {
            ShopMachineEffectType.DailyMoney => "money",
            ShopMachineEffectType.DailyItem => "maker",
            ShopMachineEffectType.RevenueMultiplier => "attract",
            _ => "fridge",
        };
    }

    private static bool FinanceIsBond(PersonaShopView v, string key) => v?.Products.FirstOrDefault(p => p.Id == key)?.Bond ?? false;

    /// <summary>学習の選択肢は商品の種類（債券・市場ファンド・属性ファンド・売り）にまとめる（属性はランごとに意味が変わるため）。</summary>
    private static string FinanceKind(PersonaShopView v, string key)
    {
        if (string.IsNullOrEmpty(key) || key == "none") return "none";
        if (key.StartsWith("sell_", StringComparison.Ordinal)) return "sell";
        var p = v?.Products.FirstOrDefault(x => x.Id == key);
        if (p == null) return "other";
        if (p.Bond) return p.MaturityDays >= 10 ? "bond_long" : "bond_short";
        return p.Tracks == "all goods" ? "fund_market" : "fund_attribute";
    }

    private static string FinanceNameJa(PersonaShopView v, string key)
    {
        if (string.IsNullOrEmpty(key) || key == "none") return "";
        string id = key.StartsWith("sell_", StringComparison.Ordinal) ? key.Substring(5) : key;
        return v?.Products.FirstOrDefault(p => p.Id == id)?.Name ?? id;
    }

    private int _spokenFinanceBase = -1;

    // ---- IAutoPlayShopInvestor ----

    public AutoPlayShopInvestPlan ChooseShopInvestments(AutoPlayContext ctx)
    {
        var plan = new AutoPlayShopInvestPlan();
        if (!HasDay(ctx.Turn) || _dayShop == null) return plan;
        var v = _dayShop;
        var reasons = new List<string>();
        int keep = Mathf.Max(0, _p.CashFloor);
        if (v != null && _lastDebtDays >= 0 && _lastDebtDays <= 3) keep = Mathf.Max(keep, _lastDebtAmount + _p.CashFloor);
        // 1 日に店への投資へ回すのは、使ってよい額のうち ずんだもん 6 割・めたん 3.5 割まで（残りは仕入れ。
        // 試走で、ずんだもんが毎日ファンド・債券・開発に回して所持金が 4G になり、仕入れができなくなった）
        int room = Mathf.RoundToInt(Mathf.Max(0, ctx.Money - Mathf.Max(keep, ctx.TaxReserve)) * (_p.Sample ? 0.6f : 0.35f));
        int planned = 0;
        bool Afford(int cost, string what)
        {
            if (cost <= 0) return false;
            if (planned + cost <= room) { planned += cost; return true; }
            reasons.Add($"{what}は今日の投資の枠に入らないので見送り（{cost:N0}G / 枠の残り {room - planned:N0}G）");
            return false;
        }
        plan.HeroWeaponId = _planWeapon;
        plan.HeroArmorId = _planArmor;
        plan.HeroTactics = _planTactics;
        if (_planShopUpgrade) plan.UpgradeShop = Afford(v.ShopUpgradeCost, "店の改装");
        if (_planSmithUpgrade) plan.UpgradeSmith = Afford(v.SmithUpgradeCost, "鍛冶屋の開発");
        if (_planMachine != null)
        {
            var m = v.Machines.FirstOrDefault(x => x.Id == _planMachine);
            if (m != null && Afford(m.Cost, m.Name)) plan.MachineId = m.Id;
        }
        if (_planBrokerUpgrade) plan.UpgradeBroker = Afford(v.BrokerUpgradeCost, "情報屋の開発");
        if (_planFinance != null)
        {
            if (_planFinance.StartsWith("sell_", StringComparison.Ordinal))
            {
                string id = _planFinance.Substring(5);
                var p = v.Products.FirstOrDefault(x => x.Id == id);
                if (p != null && p.Held > 0)
                {
                    plan.FinanceProductId = id;
                    plan.FinanceUnits = -p.Held;
                }
            }
            else
            {
                var p = v.Products.FirstOrDefault(x => x.Id == _planFinance);
                // 取引所に入れたお金が資産（現金＋取引所）の半分を超えていたら、新しくは買わない（現金が無いと仕入れも返済もできない）
                if (p != null && v.PortfolioValue > (ctx.Money + v.PortfolioValue) / 2)
                {
                    reasons.Add($"取引所にもう {v.PortfolioValue:N0}G 入れているので {p.Name}は見送り");
                    p = null;
                }
                if (p != null && p.UnitPrice > 0)
                {
                    int gold = Mathf.RoundToInt(Mathf.Max(0, room - planned) * _planFinanceShare);
                    int per = Mathf.RoundToInt(p.UnitPrice * (1f + p.FeePct / 100f));
                    int units = per > 0 ? gold / per : 0;
                    if (p.Bond && p.RemainingUnits >= 0) units = Mathf.Min(units, p.RemainingUnits);
                    if (units > 0)
                    {
                        plan.FinanceProductId = p.Id;
                        plan.FinanceUnits = units;
                    }
                    else reasons.Add($"{p.Name}は 1 口も買えない額なので見送り（{gold:N0}G）");
                }
            }
        }
        var what = new List<string>();
        if (plan.HeroWeaponId != null) what.Add("武器 " + plan.HeroWeaponId);
        if (plan.HeroArmorId != null) what.Add("防具 " + plan.HeroArmorId);
        if (plan.HeroTactics != null) what.Add("作戦 " + plan.HeroTactics);
        if (plan.UpgradeShop) what.Add("店の改装");
        if (plan.UpgradeSmith) what.Add("鍛冶屋の開発");
        if (plan.MachineId != null) what.Add("設備 " + plan.MachineId);
        if (plan.UpgradeBroker) what.Add("情報屋の開発");
        if (plan.FinanceProductId != null) what.Add($"{plan.FinanceProductId} {(plan.FinanceUnits > 0 ? "買い" : "売り")} {Math.Abs(plan.FinanceUnits)} 口");
        if (what.Count > 0 || reasons.Count > 0)
            plan.Reason = $"{_p.LabelJa}: " + (what.Count > 0 ? string.Join("・", what) : "なし") + (reasons.Count > 0 ? "（" + string.Join("、", reasons) + "）" : "");
        if (reasons.Count > 0)
            _log?.Write(new PersonaLogEntry
            {
                Day = ctx.Turn, Persona = _p.Id, Model = Model, Question = "shop_invest_guard", Kind = "shop_invest_guard",
                Choice = string.Join("; ", reasons), Line = "",
            });
        return plan;
    }

    public void OnShopInvestmentsDone(AutoPlayShopInvestResult r)
    {
        if (r == null) return;
        // 予定どおりに押せなかったものは、学習には実際の結果（しなかった）を付ける（村の施設と同じ）
        void Actual(string q, bool done, string doneKey, string notKey)
        {
            if (_shopRecs.TryGetValue(q, out var rec) && rec != null) rec.Choice = done ? doneKey : notKey;
        }
        if (_planShopUpgrade) Actual("shop_upgrade", r.ShopUpgraded, "true", "false");
        if (_planSmithUpgrade) Actual("smith_upgrade", r.SmithUpgraded, "true", "false");
        if (_planBrokerUpgrade) Actual("broker_upgrade", r.BrokerUpgraded, "true", "false");
        if (_planMachine != null) Actual("machine", r.MachinePlaced != null, MachineKind(_planMachine), "none");
        if (_planFinance != null)
        {
            bool moved = r.FinanceUnits != 0;
            Actual("finance", moved, FinanceKind(_dayShop, _planFinance), "none");
            if (!moved && _shopRecs.TryGetValue("finance_share", out var sr) && sr != null) _pending.Remove(sr);   // 買わなかった額は学ばない
        }
        if (_planWeapon != null) Actual("hero_weapon", r.HeroWeaponEquipped != null, "change", "keep");
        if (_planArmor != null) Actual("hero_armor", r.HeroArmorEquipped != null, "change", "keep");
        if (_planTactics != null && r.HeroTacticsSet == null && _shopRecs.TryGetValue("hero_tactics", out var tr) && tr != null)
            tr.Choice = _dayShop?.HeroTactics ?? "Balanced";

        var done = new List<string>();
        if (r.HeroWeaponEquipped != null) done.Add($"hero weapon {r.HeroWeaponEquipped}");
        if (r.HeroArmorEquipped != null) done.Add($"hero armor {r.HeroArmorEquipped}");
        if (r.HeroTacticsSet != null) done.Add($"tactics {r.HeroTacticsSet}");
        if (r.ShopUpgraded) done.Add($"shop Lv{r.ShopLevelAfter}");
        if (r.SmithUpgraded) done.Add($"blacksmith Lv{r.SmithLevelAfter}");
        if (r.BrokerUpgraded) done.Add($"broker Lv{r.BrokerLevelAfter}");
        if (r.MachinePlaced != null) done.Add($"machine {r.MachinePlaced}");
        if (r.FinanceUnits != 0) done.Add($"{r.FinanceProductId} {(r.FinanceUnits > 0 ? "+" : "")}{r.FinanceUnits} units");
        _log?.Write(new PersonaLogEntry
        {
            Day = r.Turn, Persona = _p.Id, Model = Model, Question = "shop_invest_result", Kind = "shop_invest_result",
            Choice = (done.Count > 0 ? string.Join(", ", done) : "nothing done") + $" (spent {r.Spent}G)" +
                     (r.Notes.Count > 0 ? " | " + string.Join(" / ", r.Notes) : ""),
            Line = "", Reward = -r.Spent,
        });
        AutoPlay.AddLog($"{_p.LabelJa}: 店への投資の結果 — " + (done.Count > 0 ? string.Join("・", done) : "押せたものなし") + $"（{r.Spent:+#,0;-#,0;0}G）");
    }

    private int _lastDebtDays = -1, _lastDebtAmount;

    /// <summary>店への投資の判断材料（画面とモデルから。取れなければ null）。</summary>
    private static PersonaShopView BuildShopView(AutoPlayContext ctx, PersonaStreamView next)
    {
        var toms = AutoPlayUi.Resolve<TomsModel>();
        var items = AutoPlayUi.Resolve<ItemModel>();
        if (toms == null || items?.RuntimeItems == null) return null;
        var v = new PersonaShopView();
        try
        {
            var shopView = AutoPlayUi.Resolve<TomsShopView>();
            // 解禁されているか（ボタン自身が出ているか）。店・魔王軍支援のボタンは陳列フェーズのまとまりにあり、仕入れの前（この判断の時点）は
            // まとまりごと隠れているので activeInHierarchy では見ない（押すのは陳列フェーズ。AutoPlayController.OnBeforeDisplay）
            bool Unlocked(string field)
            {
                var b = shopView != null ? AutoPlayUi.GetField<UnityEngine.UI.Button>(shopView, field) : null;
                return b != null && b.gameObject.activeSelf && b.interactable;
            }
            v.HeroAvailable = Unlocked("HeroButton");
            v.ShopAvailable = Unlocked("ShopUpgradeButton");
            v.BrokerAvailable = Unlocked("InfoButton");
            int smith = toms.BlacksmithLevel.Value;

            // 勇者
            var hero = AutoPlayUi.Resolve<HeroModel>()?.heroData;
            if (hero != null)
            {
                v.HeroWeaponId = string.IsNullOrEmpty(hero.weaponId.Value) ? null : hero.weaponId.Value;
                v.HeroArmorId = string.IsNullOrEmpty(hero.armorId.Value) ? null : hero.armorId.Value;
                v.HeroTactics = hero.tactics.Value.ToString();
                foreach (var r in items.RuntimeItems)
                {
                    if (r == null || r.RequiredLevel.Value > smith) continue;
                    bool weapon = r.ItemType == ItemTypeData.ItemType.Weapon;
                    if (!weapon && r.ItemType != ItemTypeData.ItemType.Armor) continue;
                    int tier = HeroEquipmentBonus.TierOf(r);
                    var g = new PersonaGearView
                    {
                        Id = r.ItemId, Name = r.ItemName, Attribute = r.ItemAttribute.ToString(), Tier = tier,
                        BonusPct = Mathf.RoundToInt((weapon ? HeroEquipmentBonus.WeaponAttackBonus(tier) : HeroEquipmentBonus.ArmorDefenseBonus(tier)) * 100f),
                        Stock = r.Stock.Value, Price = r.CurrentPrice.Value,
                        Equipped = r.ItemId == (weapon ? v.HeroWeaponId : v.HeroArmorId),
                        WeakAttribute = next != null && next.WeaknessKnown && next.Weakness == r.ItemAttribute.ToString(),
                    };
                    (weapon ? v.Weapons : v.Armors).Add(g);
                }
            }
            else v.HeroAvailable = false;

            // 店の改装・設備
            var levels = AutoPlayUi.Resolve<ShopLevelSettings>();
            var machines = AutoPlayUi.Resolve<ShopMachineModel>();
            v.ShopLevel = toms.ShopLevel.Value;
            if (levels != null)
            {
                v.ShopMaxLevel = levels.MaxLevel;
                v.ShopUpgradeCost = levels.GetLevelUpCost(v.ShopLevel);
                var now = levels.GetEntry(v.ShopLevel);
                var nx = v.ShopLevel < levels.MaxLevel ? levels.GetEntry(v.ShopLevel + 1) : now;
                v.KindsNow = now.maxDisplayKinds;
                v.KindsNext = nx.maxDisplayKinds;
                int extra = machines?.ExtraSlots ?? 0;
                v.SlotsNow = now.machineSlots + extra;
                v.SlotsNext = nx.machineSlots + extra;
                v.SlotsMax = v.SlotsNow;
            }
            if (machines != null)
            {
                v.SlotsUsed = machines.PlacedCount;
                foreach (var id in machines.PlacedMachineIds) v.PlacedNames.Add(machines.GetMachine(id)?.machineName ?? id);
                foreach (var m in machines.AllMachines)
                {
                    if (m == null || m.requiredShopLevel > v.ShopLevel) continue;
                    v.Machines.Add(new PersonaMachineView
                    {
                        Id = m.machineId, Name = m.machineName, Cost = machines.GetPurchaseCost(m), Effect = m.EffectSummary,
                        EffectEn = MachineEffectEn(m), DailyGold = m.effectType == ShopMachineEffectType.DailyMoney ? m.dailyMoney : 0,
                        Flashy = PersonaRequests.FlashyMachines.Contains(m.machineId), Placed = machines.CountOf(m.machineId),
                    });
                }
            }

            // 鍛冶屋の開発
            v.SmithLevel = smith;
            v.SmithMaxLevel = GameConst.MaxBlackSmithLevel;
            v.SmithUpgradeCost = GameConst.GetBlackSmithLevelUpCost(smith);
            var unlock = items.RuntimeItems.Where(r => r != null && r.RequiredLevel.Value == smith + 1
                && (r.ItemType == ItemTypeData.ItemType.Weapon || r.ItemType == ItemTypeData.ItemType.Armor)).ToList();
            v.SmithUnlocks = unlock.Count;
            if (unlock.Count > 0)
            {
                v.SmithUnlockMinPrice = unlock.Min(r => r.CurrentPrice.Value);
                v.SmithUnlockMaxPrice = unlock.Max(r => r.CurrentPrice.Value);
            }

            // 情報屋の開発・取引所
            var portfolio = AutoPlayUi.Resolve<PortfolioModel>();
            int broker = toms.InfoBrokerLevel.Value;
            v.BrokerLevel = broker;
            v.BrokerMaxLevel = GameConst.MaxInfoBrokerLevel;
            var costs = GameConst.InfoBrokerLevelUpCosts;
            v.BrokerUpgradeCost = broker < v.BrokerMaxLevel && costs != null && broker < costs.Length ? costs[broker] : -1;
            if (portfolio != null)
            {
                v.BrokerUnlocks = portfolio.AllProducts.Count(p => p != null && p.unlockInfoBrokerLevel == broker + 1);
                var fs = AutoPlayUi.Resolve<FinanceSettings>();
                int feePct = Mathf.RoundToInt((fs != null ? fs.fundBuyFeeRate : 0.02f) * 100f);
                foreach (var p in portfolio.GetUnlockedProducts(broker))
                {
                    if (p == null) continue;
                    bool bond = p.kind == FinancialProductKind.Bond;
                    var positions = portfolio.Positions.Where(x => x.ProductId == p.productId).ToList();
                    int unitPrice = bond ? p.bondUnitPrice : portfolio.CalculateFundUnitPrice(p, items, smith);
                    var hist = portfolio.GetNavHistory(p.productId);
                    int prev = hist.Count >= 2 ? hist[hist.Count - 2] : unitPrice;
                    int remaining = bond ? portfolio.GetRemainingBondUnits(p, broker) : -1;
                    int held = positions.Sum(x => x.Units);
                    v.Products.Add(new PersonaFinanceView
                    {
                        Id = p.productId, Name = p.productName, Bond = bond, UnitPrice = unitPrice,
                        RatePct = Mathf.RoundToInt(p.bondInterestRate * 100f), MaturityDays = p.bondMaturityTurns,
                        RemainingUnits = remaining == int.MaxValue ? -1 : remaining,
                        Tracks = p.useAttributeFilter ? p.attribute + " goods" : "all goods",
                        Change1d = prev > 0 ? (unitPrice - prev) / (float)prev : 0f, FeePct = bond ? 0 : feePct,
                        Held = held, HeldValue = bond ? positions.Sum(x => x.Principal) : unitPrice * held,
                        HeldCost = positions.Sum(x => x.Principal),
                    });
                }
                v.PortfolioValue = PortfolioValueNow(portfolio, items, smith);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 店への投資の材料を読めませんでした（その日は聞かない）: {e.Message}");
            return null;
        }
        return v;
    }

    private static string MachineEffectEn(ShopMachineData m) => m.effectType switch
    {
        ShopMachineEffectType.DailyMoney => $"+{m.dailyMoney}G every morning",
        ShopMachineEffectType.DailyItem => m.dailyItemSelectable
            ? $"makes goods worth about {m.dailyProductionBudget}G every morning"
            : $"makes {m.dailyItemCount} goods every morning",
        ShopMachineEffectType.RevenueMultiplier => $"shop sales +{Mathf.RoundToInt(m.revenueMultiplierBonus * 100f)}%",
        _ => $"raises the demand floor of all goods by {Mathf.RoundToInt(m.demandFloorBonus * 100f)}%",
    };

    /// <summary>取引所に入れているお金の今の評価額（ファンド = 今の基準価額、債券 = 元本）。資産（学習の結果）に足す。</summary>
    private static int PortfolioValueNow(PortfolioModel portfolio = null, ItemModel items = null, int smith = -1)
    {
        try
        {
            portfolio ??= AutoPlayUi.Resolve<PortfolioModel>();
            items ??= AutoPlayUi.Resolve<ItemModel>();
            if (portfolio == null) return 0;
            if (smith < 0) smith = AutoPlayUi.Resolve<TomsModel>()?.BlacksmithLevel.Value ?? 1;
            long sum = 0;
            foreach (var pos in portfolio.Positions)
            {
                if (pos == null) continue;
                if (pos.Kind == FinancialProductKind.Bond) sum += pos.Principal;
                else sum += (long)portfolio.CalculateFundUnitPrice(portfolio.GetProduct(pos.ProductId), items, smith) * pos.Units;
            }
            return (int)Math.Min(int.MaxValue, sum);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // =====================================================
    // ラン終了
    // =====================================================

    public void OnRunEnded(bool bankrupt)
    {
        _percentLinesThisRun = 0;
        _lastPercentLineDay = -999;
        if (_memory == null) return;
        FinalizeStreamOutcome();
        var toms = AutoPlayUi.Resolve<TomsModel>();
        int money = toms != null ? toms.PlayerMoney.Value : _lastMoney;
        int delta = _runStartMoney >= 0 ? money - _runStartMoney : 0;
        string result = bankrupt ? "bankrupt" : "finished";
        string lesson = bankrupt
            ? $"Run {_memory.Runs + 1}: went bankrupt on day {_lastTurn}; keep cash for the debt payment."
            : $"Run {_memory.Runs + 1}: finished with {money}G ({delta:+#;-#;0}G from the first shop day).";
        _memory.Runs++;
        _memory.Append(new PersonaOutcome
        {
            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            Kind = "run",
            Day = _lastTurn,
            GoldDelta = delta,
            Result = result,
            Choice = _dayChoiceSummary,
            Lesson = lesson,
        }, null);
        string runLine = PersonaLines.PickResult(_p.Id, "run_result", !bankrupt && delta >= 0, _rng, _memes,
            bankrupt || Math.Abs(delta) >= BigSwingRunGold);
        _log?.Write(new PersonaLogEntry
        {
            Day = _lastTurn, Persona = _p.Id, Model = Model, Question = "run_result",
            Choice = $"{result} {money}G ({delta:+#;-#;0}G)",
            Line = runLine, Meme = _memes?.MemeFor(runLine), MemeCue = _memes?.CueFor(runLine),
            Mood = _mood.Snapshot(), Reward = delta, E = ResultExpression(!bankrupt && delta >= 0),
            Beat = !bankrupt && delta >= 0 ? "win" : "cry",
        });
        AutoPlay.AddLog($"{_p.LabelJa}: ラン終了 {result} 所持金 {money:N0}G（{delta:+#,0;-#,0;0}G）→ 記憶に追加");

        // 学習: このランの前に村で決めた判断（施設への投資・村の予算）には、このランの所持金の増減を付ける
        int runDelta = delta;
        if (_runStartMoney >= 0) ResolvePending(r => r.Horizon == PersonaFeatures.HorizonNextRun, _ => runDelta);
        // 学習: 残った判断に、ラン終了時の資産で結果を付ける（在庫が読めなければ付けずに捨てる）
        int? endWorth = WorthNow(money);
        if (endWorth.HasValue)
        {
            int w = endWorth.Value;
            ResolvePending(r => r.Horizon != PersonaFeatures.HorizonStream, r => w - r.StartWorth);
        }
        _pending.Clear();
        _streamResolvedSinceDay = false;
        SafetyValve(_log);
        _advisor?.SafetyValve(_log);

        try { RunFinished?.Invoke(_p.Id, bankrupt, money, delta); }
        catch (Exception e) { Debug.LogException(e); }
        _runStartMoney = -1;
    }

    // =====================================================
    // 同期の判断（キャッシュを読む。無ければ標準戦略）
    // =====================================================

    public override bool ShouldProcure(AutoPlayContext ctx) => true;

    public override int ChooseProcurementBudget(AutoPlayContext ctx)
    {
        int budget = HasPlan(ctx.Turn) && _budgetFrac.HasValue
            ? (int)(SpendableAfterStreamReserve(ctx) * (double)_budgetFrac.Value)
            : base.ChooseProcurementBudget(ctx);
        if (_p.CashFloor > 0) budget = Mathf.Min(budget, ctx.Money - Mathf.Max(_p.CashFloor, ctx.TaxReserve));
        return Mathf.Max(0, budget);
    }

    public override AutoBuyStrategy ChooseAutoBuyStrategy(AutoPlayContext ctx)
    {
        if (!HasPlan(ctx.Turn) || !_buyStyle.HasValue) return base.ChooseAutoBuyStrategy(ctx);
        // 次ダンジョン特化は配信前の仕入れ（ChooseStreamPurchase）で使うので、ふだんの方針はおすすめ順に戻す
        return _buyStyle.Value == AutoBuyStrategy.DungeonFocus ? AutoBuyStrategy.Recommend : _buyStyle.Value;
    }

    public override bool ShouldBuyDungeonIntel(AutoPlayStreamPrepContext ctx)
    {
        if (ctx.WeaknessKnown || ctx.IntelCost <= 0) return false;
        if (_p.CashFloor > 0 && ctx.Money - ctx.IntelCost < _p.CashFloor) return false;
        if (HasDay(AutoPlay.CurrentTurn) && _buyIntel.HasValue) return _buyIntel.Value;
        return base.ShouldBuyDungeonIntel(ctx);
    }

    public override AutoPlayStreamPurchasePlan ChooseStreamPurchase(AutoPlayStreamPrepContext ctx)
    {
        if (!HasPlan(AutoPlay.CurrentTurn) || !_focusShare.HasValue)
        {
            var fb = base.ChooseStreamPurchase(ctx);
            if (_buyStyle.HasValue && _buyStyle.Value != AutoBuyStrategy.DungeonFocus) fb.RegularStrategy = _buyStyle.Value;
            return fb;
        }

        var plan = new AutoPlayStreamPurchasePlan
        {
            RegularStrategy = _buyStyle.HasValue && _buyStyle.Value != AutoBuyStrategy.DungeonFocus ? _buyStyle.Value : AutoBuyStrategy.Recommend,
            FocusBudget = Mathf.Clamp(Mathf.RoundToInt(ctx.Budget * _focusShare.Value), 0, ctx.Budget),
            WeaponOrders = new List<(string, int)>(),
        };
        var names = new List<string>();
        if (_weaponShares != null && plan.FocusBudget > 0)
        {
            foreach (var kv in _weaponShares)
            {
                var c = ctx.Matching.FirstOrDefault(m => m.ItemId == kv.Key);
                if (string.IsNullOrEmpty(c.ItemId) || c.UnitPrice <= 0) continue;
                int qty = Mathf.Min(c.RemainToMax, Mathf.FloorToInt(plan.FocusBudget * kv.Value / c.UnitPrice));
                if (qty <= 0) continue;
                plan.WeaponOrders.Add((c.ItemId, qty));
                names.Add($"{c.ItemName}×{qty}");
            }
        }
        plan.Reason = $"{_p.LabelJa}: ダンジョン向け {Mathf.RoundToInt(_focusShare.Value * 100)}%（{plan.FocusBudget:N0}G）" +
                      (names.Count > 0 ? $" 武器 {string.Join("・", names)}" : "");
        return plan;
    }

    public override int ChooseRelic(AutoPlayContext ctx, int choiceCount) =>
        _p.Sample && choiceCount > 0 ? _rng.Next(choiceCount) : base.ChooseRelic(ctx, choiceCount);

    public override int ChooseScenarioSelection(AutoPlayContext ctx, int choiceCount) =>
        _p.Sample && choiceCount > 0 ? _rng.Next(choiceCount) : base.ChooseScenarioSelection(ctx, choiceCount);

    // ---- 配信中 ----

    public override AutoPlayStreamIntervention ChooseStreamIntervention(AutoPlayStreamContext ctx)
    {
        // 前回選んだ介入が実際に払われたか（使った額が増えたか）で数える
        if (_pendingIntervention != null)
        {
            if (ctx.SpentThisStream > _pendingSpent)
                _usedInterventions[_pendingIntervention] = _usedInterventions.TryGetValue(_pendingIntervention, out var c) ? c + 1 : 1;
            _pendingIntervention = null;
        }
        if (ctx.Enemies.Count == 0) return AutoPlayStreamIntervention.None;
        if (_streamPending) return AutoPlayStreamIntervention.None;            // 考え中は手を出さない
        if (_streamFailed || _interveneWeights == null) return base.ChooseStreamIntervention(ctx);

        // めたん: 勇者が危ないときだけ回復を許す・現金の下限を割らない
        if (_rng.NextDouble() >= _intensity * (_p.Sample ? 1f : 0.6f)) return AutoPlayStreamIntervention.None;

        var options = new List<(AutoPlayStreamIntervention iv, string key, float w)>();
        foreach (var kv in _interveneWeights)
        {
            if (!TryParseIntervention(kv.Key, out var iv)) continue;
            bool usable = iv.IsHeroSide ? ctx.CanUse(iv.Hero) : ctx.CanUse(iv.Dungeon);
            if (!usable) continue;
            int price = iv.IsHeroSide ? ctx.PriceOf(iv.Hero) : ctx.PriceOf(iv.Dungeon);
            if (_p.CashFloor > 0 && ctx.AvailableBalance - price < _p.CashFloor) continue;
            if (!_p.Sample && kv.Key == "heal" && ctx.HeroHp01 >= 0f && ctx.HeroHp01 > 0.5f) continue;
            if (kv.Key == "bossbuff" && !ctx.BossAlive) continue;
            options.Add((iv, kv.Key, kv.Value));
        }
        if (options.Count == 0) return AutoPlayStreamIntervention.None;

        (AutoPlayStreamIntervention iv, string key, float w) pick;
        if (_p.Sample)
        {
            var weights = options.Select(o => Mathf.Pow(Mathf.Max(1e-4f, o.w), 1f / _p.Temperature)).ToList();
            pick = options[SampleIndex(weights)];
        }
        else pick = options.OrderByDescending(o => o.w).First();
        float conf = pick.w / Mathf.Max(1e-4f, options.Sum(o => o.w));

        // 相談プレイ: 配信の山場（最初の 1 発・勇者の HP が 3 割を切った・ボスが出た）だけ、投げる前にめたんに聞く
        string moment = _consult != null && _advInterveneWeights != null && _streamConsults < MaxStreamConsults ? StreamMoment(ctx) : null;
        if (moment != null)
        {
            _streamConsults++;
            string mKey = AdvisorInStream(ctx, options, out float mConf);
            var facts = new ConsultFacts
            {
                Cash = ctx.AvailableBalance, SpendBudget = ctx.SpendBudgetLeft, Boss = ctx.BossAlive, Side = _streamSide,
                HeroHpPct = ctx.HeroHp01 >= 0f ? Mathf.RoundToInt(ctx.HeroHp01 * 100f) : -1, Monsters = ctx.Enemies.Count,
            };
            string final = _consult.InStream(AutoPlay.CurrentTurn, pick.key, mKey, conf, mConf, Label("intervene_now", pick.key), Label("intervene_now", mKey), facts, moment);
            if (final == "none")
            {
                _log?.Write(new PersonaLogEntry
                {
                    Day = AutoPlay.CurrentTurn, Persona = _p.Id, Model = Model, Question = "intervene_now", Choice = "none",
                    Confidence = conf, Proposal = pick.key, Advice = mKey, Followed = true,
                });
                return AutoPlayStreamIntervention.None;
            }
            if (final != pick.key)
            {
                var alt = options.FirstOrDefault(o => o.key == final);
                if (alt.key != null) pick = alt;
            }
        }

        _pendingIntervention = pick.key;
        _pendingSpent = ctx.SpentThisStream;
        string nowLine = PersonaLines.Pick(_p.Id, "intervene_now", PersonaLines.BandOf(conf), PersonaLines.ChoiceJa("intervene", pick.key), null, _rng, _memes, _mood.Label);
        _log?.Write(new PersonaLogEntry
        {
            Day = AutoPlay.CurrentTurn, Persona = _p.Id, Model = Model, Question = "intervene_now", Choice = pick.key,
            Confidence = conf,
            Line = nowLine, Meme = _memes?.MemeFor(nowLine), MemeCue = _memes?.CueFor(nowLine),
        });
        return pick.iv;
    }

    /// <summary>相談する山場か（最初の 1 発 / 勇者の HP が 3 割を切った / ボスが出た。それぞれ 1 配信に 1 回）。</summary>
    private string StreamMoment(AutoPlayStreamContext ctx)
    {
        if (!_consultedHpLow && ctx.HeroHp01 >= 0f && ctx.HeroHp01 < 0.3f)
        {
            _consultedHpLow = _consultedFirstIv = true;
            return "hp_low";
        }
        if (!_consultedBoss && ctx.BossAlive)
        {
            _consultedBoss = _consultedFirstIv = true;
            return "boss";
        }
        if (!_consultedFirstIv)
        {
            _consultedFirstIv = true;
            return "first";
        }
        return null;
    }

    /// <summary>
    /// めたんならこの瞬間に何を投げるか（"none" = 待つ）。めたんの配信の答え（種類の重み・勢い）と彼女の決まり（現金の下限・回復は HP 半分以下だけ）。
    /// </summary>
    private string AdvisorInStream(AutoPlayStreamContext ctx, List<(AutoPlayStreamIntervention iv, string key, float w)> options, out float conf)
    {
        conf = 0.6f;
        var ap = _advisor._p;
        bool urgent = (ctx.HeroHp01 >= 0f && ctx.HeroHp01 < 0.3f) || ctx.BossAlive;
        if (!urgent && _advIntensity < 0.5f) return "none";
        var cands = new List<(string key, float w)>();
        foreach (var o in options)
        {
            if (!_advInterveneWeights.TryGetValue(o.key, out var w) || w <= 0f) continue;
            int price = o.iv.IsHeroSide ? ctx.PriceOf(o.iv.Hero) : ctx.PriceOf(o.iv.Dungeon);
            if (ap.CashFloor > 0 && ctx.AvailableBalance - price < ap.CashFloor) continue;
            if (o.key == "heal" && ctx.HeroHp01 > 0.5f) continue;
            cands.Add((o.key, w));
        }
        if (cands.Count == 0) return "none";
        var best = cands.OrderByDescending(c => c.w).First();
        conf = best.w / Mathf.Max(1e-4f, cands.Sum(c => c.w));
        return best.key;
    }

    public override int ChooseRestockQuantity(AutoPlayStreamContext ctx, string itemName, int maxQuantity, int defaultQuantity)
    {
        if (maxQuantity <= 0) return 0;
        if (_p.Sample) return Mathf.Clamp(Mathf.RoundToInt(defaultQuantity * 1.5f), 1, maxQuantity);   // つい多め
        return base.ChooseRestockQuantity(ctx, itemName, maxQuantity, defaultQuantity);
    }

    // ---- 実況用の寄り道（IAutoPlayJikkyoExtras） ----

    public float NewspaperReadSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2) return 0f;   // 初日はチュートリアル
        if (_p.NewspaperSeconds > 0f) return _p.NewspaperSeconds;
        return HasDay(ctx.Turn) && _readNewspaper == true ? 4f : 0f;
    }

    public float MapPeekSeconds(AutoPlayContext ctx)
    {
        if (ctx.Turn < 2) return 0f;
        return !_p.Sample && ctx.Turn % 2 == 0 ? 3f : 0f;   // めたんは配信先も確かめる
    }

    public int ChooseDemonSupportLevels(AutoPlayStreamPrepContext prep)
    {
        if (prep.TurnsUntilStream > 0) return 0;
        if (_supportedTurn >= AutoPlay.CurrentTurn - 1) return 0;   // 前日に出資した配信へ二重に出さない
        if (!HasDay(AutoPlay.CurrentTurn) || !_supportLevels.HasValue || _supportLevels.Value <= 0) return 0;
        if (_p.CashFloor > 0 && prep.Money < _p.CashFloor * 4) return 0;
        _supportedTurn = AutoPlay.CurrentTurn;
        return _supportLevels.Value;
    }

    public float VillageLookSeconds => _p.VillageLookSeconds;

    /// <summary>
    /// 村で決めた投資の予定（PrefetchVillageAsync）にあれば投資する。
    /// モデルに聞けなかったときは標準: ずんだもんは目についた施設に 3 つまで、めたんは 1 つだけ（村資金の下限は守る）。
    /// </summary>
    public bool ShouldInvestFacility(string facilityId, int index)
    {
        if (_villageDecided) return _villagePlan.Remove(facilityId);
        if (_p.VillageFundsFloor > 0 || _p.VillageReserveShare > 0f)
        {
            var village = AutoPlayUi.Resolve<VillageModel>();
            var fac = village?.GetFacility(facilityId);
            int cost = village != null ? village.GetNextCost(fac) : -1;
            if (village != null && cost > 0 && village.VillageFunds - cost < _p.VillageReserve(village.VillageFunds)) return false;
        }
        return _invested++ < (_p.Sample ? 3 : 1);
    }

    // =====================================================
    // 先読み: 村の判断（施設への投資）
    // =====================================================

    public async UniTask PrefetchVillageAsync(CancellationToken ct)
    {
        EnsureSession();
        _villageDecided = false;
        _villagePlan.Clear();
        _villageRecords.Clear();
        _villageLevelsBefore.Clear();
        _invested = 0;
        var v = BuildVillageSnapshot();
        if (v == null) return;
        _villageFundsBefore = v.Funds;
        foreach (var fac in v.Facilities) _villageLevelsBefore[fac.Id] = fac.Level;
        var req = PersonaRequests.BuildVillageRequest(_p, Model, Lessons(), v);
        if (req == null)
        {
            AutoPlay.AddLog($"{_p.LabelJa}: 村 — 投資できる施設が無い（村資金 {v.Funds:N0}G）");
            _villageDecided = true;
            return;
        }
        var budgetFeatures = VillageFeatures(v, null);
        var recalled = Recall(PersonaFeatures.Village, budgetFeatures, req.Questions.Keys);
        PersonaRequests.AttachSimilarPast(req, recalled);
        PersonaJevRequest advReq = null;
        if (_advisor != null)
        {
            advReq = PersonaRequests.BuildVillageRequest(_advisor._p, _advisor.Model, _advisor.Lessons(), v);
            if (advReq != null)
                PersonaRequests.AttachSimilarPast(advReq, _advisor.Recall(PersonaFeatures.Village, budgetFeatures, advReq.Questions.Keys));
        }
        IsThinking = true;
        AutoPlay.Status = _advisor != null ? $"{_p.LabelJa} が村で {_advisor._p.LabelJa} に相談中" : $"{_p.LabelJa} が村で考え中";
        PersonaJevResponse resp, advResp;
        try
        {
            (resp, advResp) = await AskPairAsync(req, advReq, ct, "village", AutoPlay.CurrentTurn);
        }
        finally
        {
            IsThinking = false;
        }
        if (resp == null) return;
        ResolveVillage(v, resp, advResp, budgetFeatures, recalled);
    }

    /// <summary>
    /// めたん（慎重）の村の「元が取れる施設に要る額」: 元が取れそうな施設を安い順に、残す額を割らない範囲で足した額。
    /// </summary>
    internal static int CarefulVillageNeeded(PersonaProfile p, PersonaVillageSnapshot v, IReadOnlyDictionary<string, bool> plausible, out string names)
    {
        int room = Mathf.Max(0, v.Funds - p.VillageReserve(v.Funds));
        int needed = 0;
        var picked = new List<string>();
        foreach (var fa in v.Facilities.Where(fa => fa.CanInvest && fa.NextCost > 0 && plausible.TryGetValue(fa.Id, out var ok) && ok)
                     .OrderBy(fa => fa.NextCost))
        {
            if (needed + fa.NextCost > room) continue;
            needed += fa.NextCost;
            picked.Add(fa.Name);
        }
        names = picked.Count > 0 ? string.Join("と", picked) : null;
        return needed;
    }

    /// <summary>
    /// めたんの村の予算の段階（rules 4.1）: 段階は「村資金のうち使う割合」。残す割合（1/4）を割る段階には上げず（1/4 残すなら 75% まで）、
    /// 元が取れそうな施設があれば、それに足りる段階までは上げる（以前は「1/4 残す」を 25% と取り違えて何も買えなかった）。
    /// </summary>
    internal static int CarefulVillageLevel(int picked, PersonaProfile p, PersonaVillageSnapshot v, IReadOnlyDictionary<string, bool> plausible)
    {
        int max = PersonaRequests.VillageBudgetLevels.Length - 1;
        int room = Mathf.Max(0, v.Funds - p.VillageReserve(v.Funds));
        int Gold(int lv) => Mathf.Min(Mathf.RoundToInt(v.Funds * lv / (float)max), room);
        int cap = 0;
        for (int lv = max - 1; lv >= 0; lv--)
            if (Mathf.RoundToInt(v.Funds * lv / (float)max) <= room + 1) { cap = lv; break; }
        int level = Mathf.Clamp(picked, 0, cap);
        int needed = CarefulVillageNeeded(p, v, plausible, out _);
        if (needed > 0 && Gold(level) < needed)
        {
            int cover = max;
            for (int lv = 1; lv <= max; lv++)
                if (Gold(lv) >= needed) { cover = lv; break; }
            level = cover;
        }
        return level;
    }

    private static void CarefulVillageBudget(PickOutcome po, PersonaProfile p, PersonaVillageSnapshot v, IReadOnlyDictionary<string, bool> plausible)
    {
        int picked = int.TryParse(po.Key, out var lv) ? lv : 0;
        int level = CarefulVillageLevel(picked, p, v, plausible);
        if (level == picked) return;
        po.Key = level.ToString();
        po.Why = (po.Why ?? "") + (level > picked ? "+cover" : "+reserve");
    }

    private void ResolveVillage(PersonaVillageSnapshot v, PersonaJevResponse resp, PersonaJevResponse advResp, float[] budgetFeatures, List<string> recalled)
    {
        var a = resp.Answers;
        var adv = advResp?.Answers;
        int day = AutoPlay.CurrentTurn;
        var lat = (resp.LatencyMs, advResp?.LatencyMs);

        // 元が取れそうか（めたんの見方。村資金から残す額は割合: 2 億G で 1 万G だけ残しても意味がない）
        var careful = _p.Careful ? _p : _advisor != null && _advisor._p.Careful ? _advisor._p : null;
        int carefulReserve = careful?.VillageReserve(v.Funds) ?? 0;
        var plausible = v.Facilities.Where(fa => fa.CanInvest && fa.NextCost > 0)
            .ToDictionary(fa => fa.Id, fa => PersonaChooser.PaybackPlausible(fa.NextCost, v.Funds, carefulReserve, v.LastRunProfit));
        var budgetSit = new PersonaSituation
        {
            Kind = PersonaFeatures.Village, Features = budgetFeatures, Cash = v.Funds, PlausibleCount = plausible.Count(kv => kv.Value),
        };
        string carefulNeededNames = null;
        int carefulNeeded = careful != null ? CarefulVillageNeeded(careful, v, plausible, out carefulNeededNames) : 0;
        var baseFacts = new ConsultFacts
        {
            Funds = v.Funds, Reserve = carefulReserve, LastProfit = v.LastRunProfit, Needed = carefulNeeded, NeededNames = carefulNeededNames,
        };

        // 予算: 村資金のうち今回使ってよい額（残す額は、聞いた方の人格の決まり）
        float frac = 0.5f;
        int reserve = _p.VillageReserve(v.Funds);
        _villageBudgetPct = "50%";
        _spokenVillageFunds = v.Funds;
        _spokenVillageReserve = reserve;
        if (a.TryGetValue("village_budget", out var budget))
        {
            var own = PickQuestion("village_budget", budget, budgetSit);
            if (_p.Careful) CarefulVillageBudget(own, _p, v, plausible);
            string line = PersonaLines.Pick(_p.Id, "village_budget", BandOf(budget), null, SpokenPct("village_budget", own.Key), _rng, _memes, _mood.Label);
            string key = own.Key;
            ConsultBinding bind = null;
            if (_consult != null && _advisor != null && adv != null && adv.TryGetValue("village_budget", out var advBudget) && advBudget != null)
            {
                // 助言役の段階も同じ決まりで直す（めたん: 残す割合は守りつつ、元が取れる施設に足りる額までは出す）
                var advPick = _advisor.PickQuestion("village_budget", advBudget, budgetSit);
                if (_advisor._p.Careful) CarefulVillageBudget(advPick, _advisor._p, v, plausible);
                key = ConsultWith("village_budget", budget, advBudget, own, advPick, line, budgetSit, baseFacts, "village", day, lat, out bind, 1f);
            }
            int level = int.TryParse(key, out var lv) ? lv : 0;
            frac = level / (float)(PersonaRequests.VillageBudgetLevels.Length - 1);
            _villageBudgetPct = $"{Mathf.RoundToInt(frac * 100)}%";
            if (bind != null && bind.Outcome.Followed) reserve = _advisor._p.VillageReserve(v.Funds);
            var rec = Decide("village_budget", key, PersonaFeatures.Village, PersonaFeatures.HorizonNextRun, budgetFeatures, 0);
            Bind(rec, bind, PersonaFeatures.HorizonNextRun);
            Log(day, resp, "village_budget", key, budget, line, own, rec, recalled, bind);
        }
        int allowance = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(v.Funds * frac), v.Funds - reserve));

        // 施設ごと: 投資したい度合い（noul を学習器で寄せ、ずんだもんは派手な施設に寄せて抽選・めたんは元が取れそうかで寄せて慎重に）
        var wants = new List<(PersonaFacilityView fac, float p, bool want, PersonaJevAnswer ans, PickOutcome own, float[] feats, string line, ConsultBinding bind)>();
        foreach (var fac in v.Facilities)
        {
            string q = "invest_" + fac.Id;
            if (!a.TryGetValue(q, out var ans)) continue;
            var feats = VillageFeatures(v, fac);
            var sit = new PersonaSituation
            {
                Kind = PersonaFeatures.Village, Features = feats, Cash = v.Funds, FacilityId = fac.Id,
                Plausible = plausible.TryGetValue(fac.Id, out var pl) ? pl : (bool?)null,
            };
            var own = PickQuestion(q, ans, sit);
            string line = null;
            ConsultBinding bind = null;
            string key = own.Key;
            if (_advisor != null)
            {
                // 相談するときは、案の一言を先に言う
                line = PersonaLines.Pick(_p.Id, own.Key == "true" ? "invest_yes" : "invest_no", BandOf(ans), fac.Name, null, _rng, _memes, _mood.Label);
                var facts = new ConsultFacts
                {
                    Funds = v.Funds, Reserve = carefulReserve, LastProfit = v.LastRunProfit, FacilityName = fac.Name, Cost = fac.NextCost,
                };
                key = Consult(q, ans, own, line, adv, sit, facts, "village", day, lat, out bind);
            }
            wants.Add((fac, own.P, key == "true", ans, own, feats, line, bind));
        }
        // 予算の中で、投資したい度合いの高い順に予定を立てる（同じ村資金を取り合うため）
        var order = wants.OrderByDescending(x => x.want).ThenByDescending(x => x.p).ToList();
        HashSet<string> PlanWithin(int budgetGold)
        {
            var set = new HashSet<string>();
            int rest = budgetGold;
            foreach (var w in order)
            {
                if (!w.want || w.fac.NextCost <= 0 || w.fac.NextCost > rest) continue;
                rest -= w.fac.NextCost;
                set.Add(w.fac.Id);
            }
            return set;
        }
        var planned = PlanWithin(allowance);
        // 予算が足りない: 投資すると決めた施設が予算に入らないが、残す額を割らなければ買える
        int room = Mathf.Max(0, v.Funds - reserve);
        var shortOf = order.Where(w => w.want && w.fac.NextCost > 0 && !planned.Contains(w.fac.Id) && w.fac.NextCost <= room).ToList();
        if (shortOf.Count > 0)
        {
            int before = allowance;
            string how;
            if (!_p.Careful)
            {
                // ずんだもん: 予算を上げる（残す額の内側で、決めた施設に足りるまで）
                var all = PlanWithin(room);
                int need = order.Where(w => all.Contains(w.fac.Id)).Sum(w => w.fac.NextCost);
                allowance = Mathf.Clamp(need, allowance, room);
                planned = PlanWithin(allowance);
                how = "raise";
            }
            else
            {
                // めたん: 元が取れそうで買える施設を 1 つ選ぶ（予算は足りるぶんだけ上げる）
                var pick = shortOf.Where(w => plausible.TryGetValue(w.fac.Id, out var ok) && ok)
                    .OrderBy(w => w.fac.NextCost).FirstOrDefault();
                if (pick.fac != null && planned.Count == 0)
                {
                    allowance = Mathf.Clamp(pick.fac.NextCost, allowance, room);
                    planned = PlanWithin(allowance);
                    how = "pick_affordable";
                }
                else how = "keep";
            }
            string detail = $"budget {before}G -> {allowance}G (funds {v.Funds}G, keep {reserve}G, {how}); short of: " +
                            string.Join(", ", shortOf.Select(w => $"{w.fac.Id} {w.fac.NextCost}G"));
            _log?.Write(new PersonaLogEntry
            {
                Day = day, Persona = _p.Id, Model = Model, Question = "budget_short", Kind = "budget_short", Choice = detail, Line = "",
            });
            AutoPlay.AddLog($"{_p.LabelJa}: 村 budget_short — {detail}");
            if (allowance != before && v.Funds > 0) _villageBudgetPct += $"→{Mathf.RoundToInt(allowance * 100f / v.Funds)}%";
        }
        foreach (var w in order)
        {
            bool plan = planned.Contains(w.fac.Id);
            if (plan) _villagePlan.Add(w.fac.Id);
            string q = "invest_" + w.fac.Id;
            string key = plan ? "true" : "false";
            var rec = Decide(q, key, PersonaFeatures.Village, PersonaFeatures.HorizonNextRun, w.feats, 0);
            if (rec != null) _villageRecords[w.fac.Id] = rec;
            Bind(rec, w.bind, PersonaFeatures.HorizonNextRun);
            string line = w.line ?? PersonaLines.Pick(_p.Id, plan ? "invest_yes" : "invest_no", BandOf(w.ans), w.fac.Name, null, _rng, _memes, _mood.Label);
            Log(day, resp, q, key, w.ans, line, w.own, rec, recalled, w.bind);
        }
        _villageDecided = true;
        AutoPlay.AddLog($"{_p.LabelJa}: 村の判断 予算 {_villageBudgetPct}（{allowance:N0}G / 村資金 {v.Funds:N0}G・残す {reserve:N0}G）→ " +
                        (_villagePlan.Count > 0 ? "投資: " + string.Join("・", _villagePlan) : "投資しない") + $"（{resp.LatencyMs} ms" +
                        (advResp != null ? $" / {_advisor._p.LabelJa} {advResp.LatencyMs} ms" : "") + "）" +
                        (recalled != null && recalled.Count > 0 ? $" 想起 {recalled.Count} 件" : ""));
    }

    public void OnVillageTourEnded()
    {
        var village = AutoPlayUi.Resolve<VillageModel>();
        if (village == null || _log == null) return;
        var grown = new List<string>();
        int spent = 0;
        foreach (var fac in village.Facilities)
        {
            if (fac == null) continue;
            int before = _villageLevelsBefore.TryGetValue(fac.facilityId, out var b) ? b : village.GetLevel(fac.facilityId);
            int after = village.GetLevel(fac.facilityId);
            bool invested = after > before;
            if (invested)
            {
                for (int lv = before + 1; lv <= after; lv++) spent += fac.GetLevel(lv)?.cost ?? 0;
                grown.Add($"{fac.facilityId} Lv{before}->Lv{after}");
            }
            // 予定どおりに押せなかった（村資金・ゲート）ときは、学習には実際の結果（投資しなかった）を付ける
            if (_villageRecords.TryGetValue(fac.facilityId, out var rec)) rec.Choice = invested ? "true" : "false";
        }
        _villagePlan.Clear();
        bool any = grown.Count > 0;
        string line = PersonaLines.PickResult(_p.Id, "village_result", any, _rng, _memes);
        _log.Write(new PersonaLogEntry
        {
            Day = AutoPlay.CurrentTurn, Persona = _p.Id, Model = Model, Question = "village_result",
            Choice = any
                ? $"invested {spent}G: {string.Join(", ", grown)} (funds {_villageFundsBefore}G -> {village.VillageFunds}G, village Lv{village.VillageLevel}/{village.MaxVillageLevel})"
                : $"no investment (funds {village.VillageFunds}G, village Lv{village.VillageLevel}/{village.MaxVillageLevel})",
            Line = line, Meme = _memes?.MemeFor(line), MemeCue = _memes?.CueFor(line),
        });
        AutoPlay.AddLog($"{_p.LabelJa}: 村 — " + (any ? $"{string.Join("・", grown)}（{spent:N0}G）" : "投資なし") +
                        $" / 村資金 {village.VillageFunds:N0}G / 総合Lv {village.VillageLevel}/{village.MaxVillageLevel}");
    }

    /// <summary>村の判断材料（VillageModel・MetaProgressModel。村の画面でだけ取れる）。</summary>
    private static PersonaVillageSnapshot BuildVillageSnapshot()
    {
        var village = AutoPlayUi.Resolve<VillageModel>();
        if (village == null) return null;
        var meta = AutoPlayUi.Resolve<MetaProgressModel>();
        int rank = 0;
        try { rank = meta != null ? VillageRank.Compute(meta) : 0; }
        catch (Exception) { /* 格が出せなくても判断はできる */ }
        var v = new PersonaVillageSnapshot
        {
            Runs = meta?.TotalRuns ?? 0,
            Funds = village.VillageFunds,
            VillageLevel = village.VillageLevel,
            MaxVillageLevel = village.MaxVillageLevel,
            HallLevel = village.HallLevel,
            Rank = rank,
            TopRank = VillageRank.TopRank,
            RankName = VillageRank.Name(rank),
        };
        var last = meta?.RunRecords != null && meta.RunRecords.Count > 0 ? meta.RunRecords[meta.RunRecords.Count - 1] : null;
        if (last != null)
        {
            v.LastRunProfit = last.profit;
            v.LastRunCleared = last.cleared;
        }
        foreach (var fac in village.Facilities)
        {
            if (fac == null || string.IsNullOrEmpty(fac.facilityId)) continue;
            int level = village.GetLevel(fac.facilityId);
            var (ok, reason) = village.CanInvest(fac);
            v.Facilities.Add(new PersonaFacilityView
            {
                Id = fac.facilityId,
                Name = fac.facilityName,
                Level = level,
                MaxLevel = fac.MaxLevel,
                NextCost = village.GetNextCost(fac),
                NextEffect = fac.GetLevel(level + 1)?.effectText,
                CanInvest = ok,
                BlockedReason = reason,
            });
        }
        return v;
    }

    /// <summary>村の特徴（fac が null なら予算の質問用）。</summary>
    private static float[] VillageFeatures(PersonaVillageSnapshot v, PersonaFacilityView fac) =>
        PersonaFeatures.ForVillage(v.Runs, v.Funds, v.Facilities.Count(f => f.CanInvest), v.Facilities.Count, v.HallLevel,
            fac?.Level ?? -1, fac?.MaxLevel ?? 0, fac?.NextCost ?? -1, fac?.Id == VillageModel.HallId, v.LastRunProfit,
            v.TopRank > 0 ? v.Rank / (float)v.TopRank : 0f, v.Facilities.Count(f => f.Level >= 1), v.LevelShare);

    /// <summary>店・配信の画面での村の総合Lv の上限（村の施設は 6 つ × 最大 Lv5）。</summary>
    private const int MaxVillageLevelInRun = 30;

    private static float VillageShareInRun()
    {
        try { return Mathf.Clamp01(VillageRank.TotalLevel(VillageEffects.Current) / (float)MaxVillageLevelInRun); }
        catch (Exception) { return 0f; }
    }

    // =====================================================
    // 判断材料（ゲームのモデルから。画面で見える情報だけ）
    // =====================================================

    private static PersonaDaySnapshot BuildDaySnapshot(AutoPlayContext ctx, AutoPlayStreamPrepContext prep)
    {
        var toms = AutoPlayUi.Resolve<TomsModel>();
        var items = AutoPlayUi.Resolve<ItemModel>();
        var flow = AutoPlayUi.Resolve<GameFlowManager>();
        var repo = AutoPlayUi.Resolve<DungeonRepository>();
        var s = new PersonaDaySnapshot
        {
            Day = ctx.Turn,
            Cash = ctx.Money,
            ShopLevel = ctx.ShopLevel,
            BlacksmithLevel = ctx.BlacksmithLevel,
            MaxVillageLevel = MaxVillageLevelInRun,
        };
        try { s.VillageLevel = VillageRank.TotalLevel(VillageEffects.Current); }
        catch (Exception) { /* 村の効果が読めなくても判断はできる */ }
        if (toms != null)
        {
            try
            {
                s.DebtAmount = toms.GetNextDebtAmount();
                int interval = GameConst.DebtPaymentInterval;
                if (interval > 0) s.DebtDaysLeft = (toms.DebtCycle.Value + 1) * interval - ctx.Turn;
            }
            catch (Exception) { /* 返済の見込みが出せなくても判断はできる */ }
        }
        if (items?.RuntimeItems != null)
        {
            foreach (var r in items.RuntimeItems)
            {
                if (r == null) continue;
                s.Items.Add(new PersonaItemView
                {
                    Id = r.ItemId,
                    Type = r.ItemType.ToString(),
                    Attribute = r.ItemAttribute.ToString(),
                    Price = r.CurrentPrice.Value,
                    PreviousPrice = r.PreviousPrice,
                    Demand = r.Demand.Value,
                    PreviousDemand = r.PreviousDemand,
                    Stock = r.Stock.Value,
                    MaxStock = r.MaxStock.Value,
                    Displayed = r.IsDisplay.Value ? r.DisplayStock.Value : 0,
                    DividendPerDay = r.DividendPerTurn,
                    Unlocked = r.RequiredLevel.Value <= ctx.BlacksmithLevel,
                    ExpectedRevenue = r.ExpectedRevenue,
                });
            }
        }

        // 次の配信（prep が無い＝まだ遠い日も、行き先と日数だけは載せる）
        DungeonName? key = prep?.Dungeon ?? flow?.GetNextBattleDungeon();
        var dungeon = key.HasValue && repo != null ? repo.GetById(key.Value) : null;
        if (dungeon != null)
        {
            bool isMax = dungeon.currentDungeonLevel >= GameConst.MaxDungeonLevel;
            s.NextStream = new PersonaStreamView
            {
                Dungeon = key.Value.ToString(),
                DaysUntil = prep != null ? prep.TurnsUntilStream : Mathf.Max(0, flow.GetTurnsUntilNextBattle()),
                Level = dungeon.currentDungeonLevel,
                WeaknessKnown = prep != null && prep.WeaknessKnown,
                Weakness = prep != null && prep.Weakness.HasValue ? prep.Weakness.Value.ToString() : null,
                IntelCost = prep?.IntelCost ?? -1,
                DefeatReward = dungeon.rewardGold,
                SupportCost = isMax ? 0 : dungeon.levelUpCost,
                RewardAfterSupport = isMax ? dungeon.rewardGold : dungeon.GetDefenseReward(dungeon.currentDungeonLevel + 1),
            };
        }
        s.Shop = BuildShopView(ctx, s.NextStream);
        if (prep != null)
        {
            foreach (var c in prep.Matching)
            {
                if (!c.IsWeapon) continue;
                s.Weapons.Add(new PersonaWeaponView
                {
                    Id = c.ItemId,
                    Attribute = c.Attribute.ToString(),
                    UnitPrice = c.UnitPrice,
                    RemainToMax = c.RemainToMax,
                    WeakAttribute = prep.IsWeakness(c.Attribute),
                });
            }
        }
        return s;
    }

    private static PersonaStreamSnapshot BuildStreamSnapshot(AutoPlayStreamContext ctx)
    {
        var s = new PersonaStreamSnapshot
        {
            Day = AutoPlay.CurrentTurn,
            Balance = ctx.AvailableBalance,
            SpendBudget = ctx.SpendBudgetLeft,
            HeroHpPct = ctx.HeroHp01 >= 0f ? Mathf.RoundToInt(ctx.HeroHp01 * 100f) : -1,
            Monsters = ctx.Enemies.Count,
            BossAlive = ctx.BossAlive,
        };
        foreach (var kv in ctx.HeroButtons) s.Interventions[InterventionKey(AutoPlayStreamIntervention.Of(kv.Key))] = kv.Value.Price;
        foreach (var kv in ctx.DungeonButtons) s.Interventions[InterventionKey(AutoPlayStreamIntervention.Of(kv.Key))] = kv.Value.Price;
        return s;
    }

    private static string ItemNameJa(string itemId)
    {
        var item = AutoPlayUi.Resolve<ItemModel>()?.GetRuntimeItem(itemId);
        return item != null ? item.ItemName : itemId;
    }

    // =====================================================
    // 補助
    // =====================================================

    private static string InterventionKey(AutoPlayStreamIntervention iv)
    {
        if (iv.IsHeroSide)
            return iv.Hero switch { HeroInterventionType.Heal => "heal", HeroInterventionType.Skill => "skill", _ => "special" };
        return iv.Dungeon switch
        {
            DungeonInterventionType.Trap => "trap",
            DungeonInterventionType.Curse => "curse",
            DungeonInterventionType.Reinforce => "reinforce",
            _ => "bossbuff",
        };
    }

    private static bool TryParseIntervention(string key, out AutoPlayStreamIntervention iv)
    {
        iv = key switch
        {
            "heal" => AutoPlayStreamIntervention.Of(HeroInterventionType.Heal),
            "skill" => AutoPlayStreamIntervention.Of(HeroInterventionType.Skill),
            "special" => AutoPlayStreamIntervention.Of(HeroInterventionType.Special),
            "trap" => AutoPlayStreamIntervention.Of(DungeonInterventionType.Trap),
            "curse" => AutoPlayStreamIntervention.Of(DungeonInterventionType.Curse),
            "reinforce" => AutoPlayStreamIntervention.Of(DungeonInterventionType.Reinforce),
            "bossbuff" => AutoPlayStreamIntervention.Of(DungeonInterventionType.BossBuff),
            _ => AutoPlayStreamIntervention.None,
        };
        return iv.HasValue;
    }

    private PersonaLines.Band BandOf(PersonaJevAnswer a) => PersonaLines.BandOf(a?.EffectiveConfidence ?? 0f);

    /// <summary>1 つの質問への自分の答え（相談プレイでは助言役も同じ手順で自分の答えを出す）。</summary>
    private sealed class PickOutcome
    {
        public string Key;
        public Reweighed Rw;
        /// <summary>選び方の理由（sample / margin / close / explore / floor / always / hard_route …）。</summary>
        public string Why;
        /// <summary>真偽の確率（村の施設: 投資したい度合い。予定の順番に使う）。</summary>
        public float P = -1f;
        /// <summary>場面の倍率（慎重な選び方のときだけ）と、それを掛けた後の分布。</summary>
        public Dictionary<string, float> Situ;
        public Dictionary<string, float> Shaped;
        /// <summary>手持ちの苦しさ（流儀の損失回避に使う）と、めたんの崩れ（イラ、1 ランに 1 回）の最中か。</summary>
        public float CashStress;
        public bool Collapse;
        /// <summary>慎重な選び方の「別の手を試す」確率（-1 = 人格の既定。エントロピーの下限のときは上げる）。</summary>
        public float ExploreRate = -1f;
    }

    /// <summary>
    /// 質問 1 つの答えを、この人格の選び方と決まり（現金の下限・新聞は必ず読む・難しい道…）で出す。ゲームには触らない。
    /// </summary>
    private PickOutcome PickQuestion(string q, PersonaJevAnswer a, PersonaSituation sit)
    {
        var po = new PickOutcome();
        var f = sit?.Features;
        _mood.Decay();   // 気分は判断ごとに基準点へ戻る（半減期は特性から）
        po.CashStress = PersonaStyle.CashStress(sit);
        po.Collapse = _p.Traits != null && _p.Traits.LossAversion > 1f && _mood.CollapseActive(AutoPlay.CurrentTurn);
        Dictionary<string, float> St(IEnumerable<string> keys) => PersonaStyle.S(_p, _mood, q, keys, po.CashStress, po.Collapse);
        if (_p.Careful && _p.IsFreeQuestion(q) && !q.StartsWith("invest_", StringComparison.Ordinal) && !ShopQuestions.Contains(q))
        {
            var kevP = a?.Probabilities != null && a.Probabilities.Count > 0 ? a.Probabilities : a?.Noul.HasValue == true ? NoulProbs(a) : null;
            if (kevP != null && kevP.Values.Count(v => v >= 0.10f) >= 2) po.ExploreRate = Mathf.Max(_p.ExploreRate, PersonaChooser.EntropyFloorExplore);
        }
        if (_p.Careful && !_p.Sample)
        {
            var m = PersonaChooser.Situation(q, sit);
            if (m.Count > 0) po.Situ = m;
        }
        switch (q)
        {
            case "budget":
            case "support":
            case "focus_share":
            case "intensity":
            case "village_budget":
            case "finance_share":
            {
                int levels = q switch
                {
                    "budget" => PersonaRequests.BudgetLevels.Length,
                    "support" => PersonaRequests.SupportLevels.Length,
                    "focus_share" => PersonaRequests.ShareLevels.Length,
                    "intensity" => PersonaRequests.IntensityLevels.Length,
                    "finance_share" => PersonaRequests.FinanceShareLevels.Length,
                    _ => PersonaRequests.VillageBudgetLevels.Length,
                };
                po.Rw = Reweigh(q, a.Probabilities, f, St(a.Probabilities?.Keys));
                int level = ScoreLevel(a, levels, po);
                if (q == "support" && _p.CashFloor > 0 && sit != null && sit.Cash >= 0 && sit.Cash < _p.CashFloor * 4)
                {
                    level = 0;   // 余裕がなければ張らない
                    po.Why = "floor";
                }
                po.Key = level.ToString();
                break;
            }
            case "buy_style":
                po.Rw = Reweigh(q, a.Probabilities, f, St(a.Probabilities?.Keys));
                po.Key = PickChoice(q, a, null, po.Rw, po, out po.Why);
                break;
            case "stream_side":
                po.Rw = Reweigh(q, a.Probabilities, f, St(a.Probabilities?.Keys));
                po.Key = PickChoice(q, a, null, po.Rw, po, out po.Why);
                // ずんだもんはたまに、わざと難しい方（魔王軍）に張る
                if (_p.HardRouteChance > 0f && po.Key != "demon" && _rng.NextDouble() < _p.HardRouteChance * 0.5f)
                {
                    po.Key = "demon";
                    po.Why = "hard_route";
                }
                break;
            case "newspaper":
            {
                po.Rw = Reweigh(q, NoulProbs(a), f, St(NoulKeys));
                bool read = DecideNoul(NoulOf(a, po.Rw), _p.Sample ? 0.5f : 0.4f, po);
                if (_p.NewspaperSeconds > 0f)
                {
                    read = true;   // めたんは情報を先に読む
                    po.Why = "always";
                }
                po.Key = read ? "true" : "false";
                break;
            }
            case "intel":
            {
                po.Rw = Reweigh(q, NoulProbs(a), f, St(NoulKeys));
                bool buy = DecideNoul(NoulOf(a, po.Rw), _p.Sample ? 0.6f : 0.35f, po);
                if (_p.HardRouteChance > 0f && _rng.NextDouble() < _p.HardRouteChance)
                {
                    buy = false;   // 情報を買わずに当てる
                    po.Why = "hard_route";
                }
                po.Key = buy ? "true" : "false";
                break;
            }
            case "shop_upgrade":
            case "smith_upgrade":
            case "broker_upgrade":
            {
                // ときどき試す: kev がほぼ 0 をつけても true の確率を TryFloorNoul より下げない（「絶対しない」を作らない）
                var probs = NoulProbs(a);
                if (probs["true"] < TryFloorNoul) probs = new Dictionary<string, float> { ["true"] = TryFloorNoul, ["false"] = 1f - TryFloorNoul };
                po.Rw = Reweigh(q, probs, f, St(NoulKeys));
                float p = po.Rw != null && po.Rw.Changed && po.Rw.Final.TryGetValue("true", out var t) ? t : probs["true"];
                po.P = p;
                po.Key = DecideNoul(p, 0.5f, po) ? "true" : "false";
                break;
            }
            case "machine":
            case "finance":
            case "hero_weapon":
            case "hero_armor":
            case "hero_tactics":
            {
                var probs = Taste(q, TryMix(q, a.Probabilities));
                po.Rw = Reweigh(q, probs, f, St(probs?.Keys));
                po.Key = PickChoice(q, a, null, po.Rw ?? new Reweighed { Kev = probs, Final = probs }, po, out po.Why);
                break;
            }
            default:
                if (q.StartsWith("invest_", StringComparison.Ordinal))
                {
                    po.Rw = Reweigh(q, NoulProbs(a), f, St(NoulKeys));
                    float p = NoulOf(a, po.Rw);
                    if (PersonaRequests.FlashyFacilities.Contains(q.Substring("invest_".Length))) p = Mathf.Clamp01(p * _p.FlashyBoost);
                    po.P = p;
                    po.Key = DecideNoul(p, 0.5f, po) ? "true" : "false";
                }
                else
                {
                    po.Rw = Reweigh(q, a.Probabilities, f, St(a.Probabilities?.Keys));
                    po.Key = PickChoice(q, a, null, po.Rw, po, out po.Why);
                }
                break;
        }
        if (po.Key != null && _p.IsFreeQuestion(q) && !ShopQuestions.Contains(q))
        {
            if (!_runPicks.TryGetValue(q, out var byKey)) _runPicks[q] = byKey = new Dictionary<string, int>();
            byKey[po.Key] = (byKey.TryGetValue(po.Key, out var n) ? n : 0) + 1;
        }
        return po;
    }

    /// <summary>
    /// ときどき試す（店への投資の選択肢の質問だけ）: kev の確率に一様分布を TryMixChoice だけ混ぜる（「設備は絶対買わない」を作らない）。
    /// 勇者の装備・作戦は自由な質問の下限（エントロピー）があるので混ぜない。
    /// </summary>
    private static Dictionary<string, float> TryMix(string q, Dictionary<string, float> probs)
    {
        if (probs == null || probs.Count == 0 || !ShopQuestions.Contains(q)) return probs;
        float u = 1f / probs.Count;
        return probs.ToDictionary(kv => kv.Key, kv => (1f - TryMixChoice) * kv.Value + TryMixChoice * u);
    }

    /// <summary>
    /// 人格の好み（kev の確率に掛けて正規化。学習器と流儀はこの後）。ずんだもん: 派手な設備（FlashyBoost 倍）・投資信託（大きく張る）・
    /// 弱点の属性の武器（名前が強そう）・攻撃重視。めたんの好み（元が取れる設備・債券・弱点の装備）は場面の倍率（PersonaChooser.Situation）で入れる。
    /// </summary>
    private Dictionary<string, float> Taste(string q, Dictionary<string, float> probs)
    {
        if (probs == null || probs.Count == 0 || !_p.Sample) return probs;
        var shop = _dayShop;
        var w = new Dictionary<string, float>();
        foreach (var kv in probs)
        {
            float m = 1f;
            switch (q)
            {
                case "machine":
                    if (PersonaRequests.FlashyMachines.Contains(kv.Key)) m = _p.FlashyBoost;
                    break;
                case "finance":
                    if (kv.Key.StartsWith("sell_", StringComparison.Ordinal)) m = 0.8f;            // 持ったら離さない
                    else if (kv.Key != "none") m = FinanceIsBond(shop, kv.Key) ? 0.7f : 1.5f;       // 債券は地味、ファンドは夢がある
                    break;
                case "hero_weapon":
                    if (shop?.Weapons.FirstOrDefault(g => g.Id == kv.Key) is { } wg && (wg.WeakAttribute || wg.Tier >= 3)) m = 1.3f;
                    break;
                case "hero_tactics":
                    if (kv.Key == "Aggressive") m = 1.5f;
                    break;
            }
            w[kv.Key] = Mathf.Max(1e-4f, kv.Value) * m;
        }
        float sum = w.Values.Sum();
        return w.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
    }

    /// <summary>
    /// 固まりの安全弁（5.3.4）: このランで自由な質問の最多の手が 85% を超えた（5 件以上）なら、次のランは技能の重みを半分（下限 1/8）。
    /// 超えなければ 1.5 倍で 1 へ戻す。ログ（question = safety_valve）と AutoPlay のログに出す。
    /// </summary>
    private void SafetyValve(PersonaLog log)
    {
        if (_model == null) return;
        var stuck = _runPicks
            .Select(kv => (q: kv.Key, n: kv.Value.Values.Sum(), top: kv.Value.OrderByDescending(x => x.Value).First()))
            .Where(x => x.n >= 5 && x.top.Value / (float)x.n > 0.85f)
            .Select(x => $"{x.q}={x.top.Key} {x.top.Value}/{x.n}")
            .ToList();
        float before = _model.SkillScale;
        _model.SkillScale = stuck.Count > 0 ? Mathf.Max(0.125f, before * 0.5f) : Mathf.Min(1f, before * 1.5f);
        _model.Save();
        if (stuck.Count == 0 && Mathf.Approximately(before, _model.SkillScale)) return;
        string msg = stuck.Count > 0 ? "固まり: " + string.Join(", ", stuck) : "固まりなし";
        AutoPlay.AddLog($"{_p.LabelJa}: 安全弁 {msg} → 技能の重み {before:0.###} → {_model.SkillScale:0.###}");
        log?.Write(new PersonaLogEntry
        {
            Day = AutoPlay.CurrentTurn, Persona = _p.Id, Model = Model, Question = "safety_valve",
            Choice = $"{before:0.###}->{_model.SkillScale:0.###}", Line = "", Why = msg, SkillWeight = _model.SkillScale,
        });
    }

    private static bool IsLevelQuestion(string q) => q is "budget" or "support" or "focus_share" or "intensity" or "village_budget" or "finance_share";

    /// <summary>相談の会話で使う日本語の名前。段階の質問は割合ではなく、判断時点の額（G）か言葉で言う（ログの key は段階のまま）。</summary>
    private string Label(string q, string key)
    {
        int.TryParse(key, out var lv);
        switch (q)
        {
            case "budget":
            case "focus_share":
            case "village_budget":
                return SpokenAmount(q, key);
            case "intensity":
                return IntensityJa(lv);
            case "support":
                return lv == 0 ? "出資なし" : lv >= 3 ? "出せるだけ" : $"{lv} レベル";
            case "newspaper":
                return key == "true" ? "読む方" : "読まない方";
            case "intel":
                return key == "true" ? "情報を買う方" : "買わない方";
            case "shop_upgrade":
                return key == "true" ? "改装する方" : "改装しない方";
            case "smith_upgrade":
                return key == "true" ? "鍛冶屋を開発する方" : "開発しない方";
            case "broker_upgrade":
                return key == "true" ? "情報屋を開発する方" : "開発しない方";
            case "machine":
                return key == "none" ? "置かない方" : _dayShop?.Machines.FirstOrDefault(m => m.Id == key)?.Name ?? key;
            case "finance":
                if (key == "none") return "何もしない方";
                return key.StartsWith("sell_", StringComparison.Ordinal) ? FinanceNameJa(_dayShop, key) + "を売る方" : FinanceNameJa(_dayShop, key);
            case "finance_share":
                return SpokenAmount(q, key);
            case "hero_weapon":
                return _dayShop?.Weapons.FirstOrDefault(g => g.Id == key)?.Name ?? key;
            case "hero_armor":
                return _dayShop?.Armors.FirstOrDefault(g => g.Id == key)?.Name ?? key;
            case "hero_tactics":
                return Enum.TryParse<HeroTactics>(key, out var tac) ? HeroBattleInfluence.GetDisplayName(tac) : key;
            case "intervene_now":
                return key == "none" ? "様子見" : PersonaLines.ChoiceJa("intervene", key);
            default:
                if (q.StartsWith("invest_", StringComparison.Ordinal)) return key == "true" ? "投資する方" : "見送り";
                return PersonaLines.ChoiceJa(q, key);
        }
    }

    /// <summary>スパチャの勢いの言葉（「全力」「控えめ」「見守るだけ」）。</summary>
    internal static string IntensityJa(int lv) => lv switch { <= 0 => "見守るだけ", 1 => "控えめ", 2 => "ほどほど", 3 => "多め", _ => "全力" };

    private static float LevelFrac(string q, int lv)
    {
        int max = q switch
        {
            "budget" => PersonaRequests.BudgetLevels.Length - 1,
            "focus_share" => PersonaRequests.ShareLevels.Length - 1,
            "intensity" => PersonaRequests.IntensityLevels.Length - 1,
            "village_budget" => PersonaRequests.VillageBudgetLevels.Length - 1,
            "finance_share" => PersonaRequests.FinanceShareLevels.Length - 1,
            _ => 1,
        };
        return Mathf.Clamp01(lv / (float)Mathf.Max(1, max));
    }

    private int SpokenBudgetGold(float frac)
    {
        int g = (int)(_spokenBudgetBase * (double)frac);
        if (_spokenBudgetCap >= 0) g = Mathf.Min(g, _spokenBudgetCap);
        return Mathf.Max(0, g);
    }

    /// <summary>段階を「判断時点の額」で言う（例「12万G」「半分の4万G」「ほぼ全部の30万G」）。額が分からないときは言葉（ちょっとだけ・半分・全部）。</summary>
    private string SpokenAmount(string q, string key)
    {
        int.TryParse(key, out var lv);
        float frac = LevelFrac(q, lv);
        int gold;
        string top;
        switch (q)
        {
            case "budget":
                if (_spokenBudgetBase < 0) return WordsFor(frac);
                gold = SpokenBudgetGold(frac);
                top = "ほぼ全部の";
                break;
            case "focus_share":
                if (_spokenFocusBase < 0) return WordsFor(frac);
                gold = Mathf.RoundToInt(_spokenFocusBase * frac);
                top = "予算全部の";
                break;
            case "finance_share":
                if (_spokenFinanceBase < 0) return WordsFor(frac);
                gold = Mathf.RoundToInt(_spokenFinanceBase * FinanceShareFrac(lv));
                top = "使える分の4分の3の";
                break;
            case "village_budget":
                if (_spokenVillageFunds < 0) return WordsFor(frac);
                int room = Mathf.Max(0, _spokenVillageFunds - _spokenVillageReserve);
                gold = frac >= 1f ? room : Mathf.Min(Mathf.RoundToInt(_spokenVillageFunds * frac), room);
                top = "使える分全部の";
                break;
            default:
                return WordsFor(frac);
        }
        if ((lv <= 0 && q != "finance_share") || gold <= 0) return "0G";   // 取引所の額のいちばん下（10%）は 0G ではない
        string money = PersonaConsult.Money(gold);
        if (frac >= 1f) return top + money;
        if (Mathf.Approximately(frac, 0.5f)) return "半分の" + money;
        return money;
    }

    private static string WordsFor(float frac) =>
        frac <= 0f ? "ゼロ" : frac < 0.35f ? "ちょっとだけ" : frac < 0.65f ? "半分くらい" : frac < 1f ? "多め" : "全部";

    /// <summary>
    /// 自分の一言の {pct}。ふつうは額か言葉（<see cref="Label"/>）。いちばん上の段階のときだけ、1 ランに <see cref="PercentLinesPerRun"/> 回まで・
    /// <see cref="PercentLineMinDays"/> 日以上あけて「100%」の決め台詞を言ってよい（同じ判断が続いても毎回は言わない）。
    /// </summary>
    private string SpokenPct(string q, string key)
    {
        int.TryParse(key, out var lv);
        int day = AutoPlay.CurrentTurn;
        if (day < _lastPercentLineDay) { _percentLinesThisRun = 0; _lastPercentLineDay = -999; }   // 新しいラン
        if (q != "intensity" && q != "finance_share" && LevelFrac(q, lv) >= 1f && _percentLinesThisRun < PercentLinesPerRun
            && day - _lastPercentLineDay >= PercentLineMinDays)
        {
            _percentLinesThisRun++;
            _lastPercentLineDay = day;
            return LinePct(q, key);
        }
        return Label(q, key);
    }

    /// <summary>段階の割合（ログ・要約用。一言では <see cref="SpokenPct"/> を使う）。</summary>
    private static string LinePct(string q, string key)
    {
        int.TryParse(key, out var lv);
        int max = q switch
        {
            "budget" => PersonaRequests.BudgetLevels.Length - 1,
            "focus_share" => PersonaRequests.ShareLevels.Length - 1,
            "intensity" => PersonaRequests.IntensityLevels.Length - 1,
            "village_budget" => PersonaRequests.VillageBudgetLevels.Length - 1,
            "finance_share" => -1,
            _ => 1,
        };
        if (max < 0) return $"{Mathf.RoundToInt(FinanceShareFrac(lv) * 100)}%";
        return $"{Mathf.RoundToInt(lv / (float)Mathf.Max(1, max) * 100)}%";
    }

    // =====================================================
    // 相談（PersonaConsult）
    // =====================================================

    private sealed class ConsultBinding
    {
        public ConsultRequest Request;
        public ConsultOutcome Outcome;
    }

    /// <summary>相談プレイなら、助言役にも同じ質問の答えを出させて掛け合いをし、最終の答えを返す（1 人ならそのまま own.Key）。</summary>
    private string Consult(string q, PersonaJevAnswer a, PickOutcome own, string line, Dictionary<string, PersonaJevAnswer> adv,
        PersonaSituation sit, ConsultFacts facts, string phase, int day, (long? own, long? adv) lat, out ConsultBinding bind)
    {
        bind = null;
        if (_consult == null || _advisor == null || adv == null || !adv.TryGetValue(q, out var aa) || aa == null) return own.Key;
        var advPick = _advisor.PickQuestion(q, aa, sit);
        return ConsultWith(q, a, aa, own, advPick, line, sit, facts, phase, day, lat, out bind, q == "newspaper" ? 0.3f : 1f);
    }

    private string ConsultWith(string q, PersonaJevAnswer a, PersonaJevAnswer aa, PickOutcome own, PickOutcome advPick, string line,
        PersonaSituation sit, ConsultFacts facts, string phase, int day, (long? own, long? adv) lat, out ConsultBinding bind, float voice)
    {
        bool noul = a?.Noul.HasValue == true && (a.Probabilities == null || a.Probabilities.Count == 0);
        var r = new ConsultRequest
        {
            Question = q, Phase = phase, Day = day, Levels = IsLevelQuestion(q), Facts = facts,
            Z = new ConsultPick
            {
                Key = own.Key, Label = Label(q, own.Key), Conf = a?.EffectiveConfidence ?? 0f,
                Probs = own.Rw?.Final ?? (noul ? NoulProbs(a) : a?.Probabilities), Why = own.Why, LatencyMs = lat.own,
            },
            M = new ConsultPick
            {
                Key = advPick.Key, Label = Label(q, advPick.Key), Conf = aa?.EffectiveConfidence ?? 0f,
                Probs = advPick.Shaped ?? advPick.Rw?.Final ?? (noul ? NoulProbs(aa) : aa?.Probabilities), Why = advPick.Why, LatencyMs = lat.adv,
            },
            ZLine = line, ZMeme = _memes?.MemeFor(line), ZBand = BandOf(a),
            SilentAgree = q == "newspaper" || q.StartsWith("invest_", StringComparison.Ordinal)
                          || (ShopQuestions.Contains(q) && (own.Key is "false" or "none") && (advPick.Key is "false" or "none")),
            VoiceChance = voice,
            MCollapse = advPick.Collapse,
        };
        var o = _consult.Exchange(r);
        bind = new ConsultBinding { Request = r, Outcome = o };
        return o.Final;
    }

    private void Bind(PersonaDecisionRecord rec, ConsultBinding bind, string horizon)
    {
        if (rec == null || bind == null) return;
        _consult?.Bind(rec.Id, bind.Request, bind.Outcome, horizon);
    }

    /// <summary>
    /// score の段階。ずんだもんは分布（学習器で寄せた後）から抽選して自信過剰ぶん上へずらす。
    /// めたん（Careful）は PersonaChooser.Careful（期待値・近ければ場面と抽選・たまに隣の段階）。
    /// それ以外は score を丸める（学習器の寄せは期待値の差として足す。2026-10-28 朝までのめたん）。
    /// </summary>
    private int ScoreLevel(PersonaJevAnswer a, int levels, PickOutcome po)
    {
        int level;
        var rw = po?.Rw;
        var probs = rw?.Final ?? a.Probabilities;
        string why;
        // 段階の少ない質問（支援 4 段）は「抽選してから +1」だと最下段が消えて上に固まる（M3 支援のエントロピーが 1 bit を割る）。
        // そこでは自信過剰を分布の傾き exp(0.8·bias·段) で入れ、どの段も残す。
        bool tilt = _p.Sample && levels <= 4 && Mathf.Abs(_p.ScoreBias) > 0.01f;
        if (_p.Sample && probs != null && probs.Count > 0)
        {
            var keys = probs.Keys.OrderBy(k => int.TryParse(k, out var i) ? i : 0).ToList();
            var weights = keys.Select(k =>
            {
                float w = Mathf.Pow(Mathf.Max(1e-4f, probs[k]), 1f / _p.Temperature);
                if (tilt && int.TryParse(k, out var lv)) w *= Mathf.Exp(0.8f * _p.ScoreBias * lv);
                return w;
            }).ToList();
            int.TryParse(keys[SampleIndex(weights)], out level);
            why = tilt ? "sample_tilt" : "sample";
        }
        else if (_p.Careful && probs != null && probs.Count > 0)
        {
            var res = PersonaChooser.Careful(_rng, _p, probs, a.Probabilities, po?.Situ, true, po?.ExploreRate ?? -1f);
            int.TryParse(res.Key, out level);
            why = res.Why;
            if (po != null) po.Shaped = res.Shaped;
        }
        else
        {
            float score = a.Score ?? (levels - 1) / 2f;
            if (rw != null && rw.Changed) score += Expectation(rw.Final) - Expectation(rw.Kev);
            level = Mathf.RoundToInt(score);
            why = "score";
        }
        if (!(tilt && why == "sample_tilt")) level += Mathf.RoundToInt(_p.ScoreBias);
        if (po != null) po.Why = why;
        return Mathf.Clamp(level, 0, levels - 1);
    }

    private static float Expectation(Dictionary<string, float> probs)
    {
        double sum = 0, total = 0;
        foreach (var kv in probs)
        {
            if (!int.TryParse(kv.Key, out var level)) continue;
            sum += level * kv.Value;
            total += kv.Value;
        }
        return total > 0 ? (float)(sum / total) : 0f;
    }

    /// <summary>真偽の判断。ずんだもんは noul を確率として抽選、めたん（Careful）は閾値を引いて PersonaChooser、それ以外は閾値で切る。</summary>
    private bool DecideNoul(float v, float threshold, PickOutcome po)
    {
        if (_p.Sample)
        {
            if (po != null) po.Why = "sample";
            return _rng.NextDouble() < Mathf.Lerp(v, 0.5f, 0.3f);
        }
        if (_p.Careful)
        {
            var r = PersonaChooser.CarefulNoul(_rng, _p, v, threshold, po?.Situ, po?.ExploreRate ?? -1f);
            if (po != null)
            {
                po.Why = r.Why;
                po.Shaped = r.Shaped;
            }
            return r.Key == "true";
        }
        if (po != null) po.Why = "threshold";
        return v >= threshold;
    }

    private static readonly string[] NoulKeys = { "true", "false" };

    /// <summary>noul を "true" / "false" の 2 択の確率にする（学習器に通すため）。</summary>
    private static Dictionary<string, float> NoulProbs(PersonaJevAnswer a)
    {
        float v = Mathf.Clamp01(a?.Noul ?? 0.5f);
        return new Dictionary<string, float> { ["true"] = v, ["false"] = 1f - v };
    }

    /// <summary>学習器で寄せた後の noul（寄せていなければ元の値）。</summary>
    private static float NoulOf(PersonaJevAnswer a, Reweighed rw) =>
        rw != null && rw.Changed && rw.Final.TryGetValue("true", out var t) ? t : a?.Noul ?? 0.5f;

    /// <summary>選択肢を選ぶ。rw があれば学習器で寄せた後の確率で。ずんだもんは温度つき抽選、めたん（Careful）は PersonaChooser、それ以外は最尤。</summary>
    private string PickChoice(string q, PersonaJevAnswer a, ICollection<string> allowed, Reweighed rw, PickOutcome po, out string why)
    {
        why = "kev";
        var probs = rw?.Final ?? a.Probabilities;
        if (probs == null || probs.Count == 0) return a.Choice;
        var list = probs
            .Where(kv => allowed == null || allowed.Contains(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        if (list.Count == 0) return a.Choice;
        if (_p.Sample)
        {
            why = "sample";
            var weights = list.Select(kv => Mathf.Pow(Mathf.Max(1e-4f, kv.Value), 1f / _p.Temperature)).ToList();
            return list[SampleIndex(weights)].Key;
        }
        if (_p.Careful)
        {
            var res = PersonaChooser.Careful(_rng, _p, list.ToDictionary(kv => kv.Key, kv => kv.Value), a.Probabilities, po?.Situ, false,
                po?.ExploreRate ?? -1f);
            why = res.Why;
            if (po != null) po.Shaped = res.Shaped;
            return res.Key;
        }
        why = "argmax";
        return list.OrderByDescending(kv => kv.Value).First().Key;
    }

    private int SampleIndex(IList<float> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        double r = _rng.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= weights[i];
            if (r <= 0) return i;
        }
        return weights.Count - 1;
    }

    private static List<KeyValuePair<string, float>> TopShares(Dictionary<string, float> probs, float minShare, int maxKinds)
    {
        var top = probs.OrderByDescending(kv => kv.Value).Take(maxKinds).Where(kv => kv.Value >= minShare).ToList();
        if (top.Count == 0) top = probs.OrderByDescending(kv => kv.Value).Take(1).ToList();
        return Normalize(top, first: -1f);
    }

    /// <summary>合計 1 に正規化する。first ≥ 0 なら先頭をその割合にして残りで分ける。</summary>
    private static List<KeyValuePair<string, float>> Normalize(List<KeyValuePair<string, float>> list, float first)
    {
        if (list.Count == 0) return list;
        if (first >= 0f)
        {
            if (list.Count == 1) return new List<KeyValuePair<string, float>> { new(list[0].Key, 1f) };
            float rest = (1f - first) / (list.Count - 1);
            return list.Select((kv, i) => new KeyValuePair<string, float>(kv.Key, i == 0 ? first : rest)).ToList();
        }
        float sum = list.Sum(kv => kv.Value);
        return list.Select(kv => new KeyValuePair<string, float>(kv.Key, sum > 0 ? kv.Value / sum : 1f / list.Count)).ToList();
    }

    private void Log(int day, PersonaJevResponse resp, string question, string choice, PersonaJevAnswer a, string line,
        PickOutcome own, PersonaDecisionRecord rec, List<string> recalled, ConsultBinding bind)
    {
        var rw = own?.Rw;
        var moodLabel = _mood.Label;
        var attr = PersonaStyle.Of(question, choice);
        _log?.Write(new PersonaLogEntry
        {
            Mood = _mood.Snapshot(),
            E = string.IsNullOrEmpty(line) ? null : PersonaLines.Expression(_p.Id, BandOf(a), moodLabel, a?.EffectiveConfidence ?? 0f,
                _mood.Conf < _mood.ConfBase - 0.05f),
            Day = day,
            Persona = _p.Id,
            Model = resp?.Model ?? Model,
            Question = question,
            Choice = choice,
            Probabilities = a?.Probabilities,
            Confidence = a?.Confidence ?? (a?.Noul.HasValue == true ? a.EffectiveConfidence : (float?)null),
            Line = line,
            Meme = _memes?.MemeFor(line),
            MemeCue = _memes?.CueFor(line),
            Fallback = false,
            LatencyMs = resp?.LatencyMs,
            Rules = PersonaFeatures.Rules,
            DecisionId = rec?.Id,
            Kind = rec?.Kind,
            Horizon = rec?.Horizon,
            Features = rec?.Features,
            Adjust = rw?.Adjust,
            FinalProbabilities = rw?.Final,
            Recalled = recalled != null && recalled.Count > 0 ? recalled : null,
            Why = own?.Why,
            Situation = own?.Situ,
            Proposal = bind?.Request.Z.Key,
            Advice = bind?.Request.M?.Key,
            Relation = bind?.Outcome.Relation,
            Followed = bind != null && bind.Outcome.Relation is "condition" or "disagree" ? bind.Outcome.Followed : (bool?)null,
            B = attr.B, D = attr.D, Hard = attr.Hard,
            Style = rw?.Style,
            PriorProbabilities = rw?.Prior,
            SkillWeight = rw != null ? (float)Math.Round(rw.SkillWeight, 3) : (float?)null,
            Kl = rw != null ? (float)Math.Round(rw.Kl, 4) : (float?)null,
            EntropyTemp = rw != null && rw.EntropyTemp > 1.001f ? (float)Math.Round(rw.EntropyTemp, 3) : (float?)null,
            CashStress = own != null ? (float)Math.Round(own.CashStress, 3) : (float?)null,
            Beat = own != null && own.Collapse ? "collapse" : null,
        });
    }

    // =====================================================
    // 学習（PersonaLearning）
    // =====================================================

    /// <summary>kev の確率・学習器で寄せた確率・その log 倍率。</summary>
    private sealed class Reweighed
    {
        public Dictionary<string, float> Kev;
        public Dictionary<string, float> Final;
        public Dictionary<string, float> Adjust;
        public bool Changed;
        /// <summary>流儀 S(a)、人格の事前分布 p_kev·exp(S)、KL の上限で縮めた技能の重み・KL・エントロピーの下限で上げた温度。</summary>
        public Dictionary<string, float> Style;
        public Dictionary<string, float> Prior;
        public float SkillWeight = 1f;
        public float Kl;
        public float EntropyTemp = 1f;
    }

    /// <summary>
    /// kev の確率を学習器（技能）と流儀 S(a) で寄せる: log π = log p_kev + w·adjust + S（PersonaStyle.Compose）。
    /// 学習器も流儀も無ければ null（kev の確率をそのまま使う）。
    /// </summary>
    private Reweighed Reweigh(string question, Dictionary<string, float> kevProbs, float[] features, Dictionary<string, float> style = null)
    {
        if (kevProbs == null || kevProbs.Count == 0) return null;
        bool hasStyle = PersonaStyle.AnyNonZero(style);
        Dictionary<string, float> adjust = null;
        if (_model != null && features != null) _model.Adjust(question, kevProbs, features, _p.LearningRate, out adjust);
        if (adjust == null && !hasStyle) return null;
        adjust ??= kevProbs.Keys.ToDictionary(k => k, _ => 0f);
        // 固まりの保険（5.3）: 人格の事前分布 p_kev·exp(S) への KL の上限、自由な質問のエントロピーの下限、安全弁の技能の倍率
        var t = _p.Traits;
        float skillScale = _model?.SkillScale ?? 1f;
        float klEps = t?.KlEps ?? 0f;
        float hMin = t != null && _p.IsFreeQuestion(question) && !ShopQuestions.Contains(question) ? t.EntropyFloorBits : 0f;
        var comp = PersonaStyle.Compose(kevProbs, adjust, hasStyle ? style : null, skillScale, klEps, hMin);
        return new Reweighed
        {
            Kev = kevProbs,
            Final = comp.Final,
            Adjust = adjust,
            Changed = adjust.Values.Any(v => Mathf.Abs(v) > 1e-4f) || hasStyle || comp.EntropyTemp > 1.001f || comp.SkillWeight < 0.999f,
            Style = hasStyle ? style : null,
            Prior = comp.Prior,
            SkillWeight = comp.SkillWeight,
            Kl = comp.Kl,
            EntropyTemp = comp.EntropyTemp,
        };
    }

    /// <summary>判断を 1 件、結果待ちに積む（特徴が無ければ積まない）。</summary>
    private PersonaDecisionRecord Decide(string question, string choice, string kind, string horizon, float[] features, int startWorth)
    {
        if (features == null || string.IsNullOrEmpty(choice)) return null;
        var rec = new PersonaDecisionRecord
        {
            Id = $"{_p.Id}-{DateTime.Now:yyyyMMddHHmmss}-{++_decisionSeq}",
            Time = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            RunTag = PersonaLog.RunTag,
            Question = question,
            Choice = choice,
            Kind = kind,
            Horizon = horizon,
            Features = PersonaFeatures.Compact(features),
            StartWorth = startWorth,
        };
        _pending.Add(rec);
        while (_pending.Count > MaxPending) _pending.RemoveAt(0);
        return rec;
    }

    /// <summary>結果待ちの判断のうち match に合うものに損益を付け、想起の履歴と学習器に足す（ログには resolve の行）。</summary>
    private void ResolvePending(Func<PersonaDecisionRecord, bool> match, Func<PersonaDecisionRecord, int> reward)
    {
        if (_pending.Count == 0 || _recall == null || _model == null) return;
        int resolved = 0;
        var results = new List<(string, float)>();
        for (int i = 0; i < _pending.Count; i++)
        {
            var r = _pending[i];
            if (!match(r)) continue;
            int g = reward(r);
            results.Add((r.Question, g));
            r.Reward = g;
            _recall.Add(r);
            _model.Update(r.Question, r.Choice, r.Features, g);
            _advisor?.LearnFrom(r, g);          // 相談プレイ: めたんも同じ結果から学ぶ（実際に選ばれた手の結果）
            _consult?.Resolved(r.Id, g);
            _log?.Write(new PersonaLogEntry
            {
                Day = AutoPlay.CurrentTurn, Persona = _p.Id, Model = Model, Question = "resolve", Choice = r.Question + "=" + r.Choice,
                Rules = PersonaFeatures.Rules, Ref = r.Id, Reward = g, Horizon = r.Horizon,
            });
            _pending.RemoveAt(i--);
            resolved++;
        }
        if (resolved == 0) return;
        // 気分: 結果のまとまりで 1 回だけ動かす（相談プレイでは店の損益は 2 人の共通の運命なので、めたんも動く）
        _mood.OnResults(results);
        _advisor?._mood.OnResults(results);
        _recall.Save();
        _model.Save();
        _advisor?.SaveLearning();
        _consult?.Flush(AutoPlay.CurrentTurn, "result");
    }

    /// <summary>助言役: 相談で決まった判断の結果を、自分の想起・学習器にも足す。</summary>
    private void LearnFrom(PersonaDecisionRecord r, int reward)
    {
        if (r == null) return;
        _recall?.Add(r);
        _model?.Update(r.Question, r.Choice, r.Features, reward);
    }

    private void SaveLearning()
    {
        _recall?.Save();
        _model?.Save();
    }

    /// <summary>似た場面を英語の短文で（今回聞く質問のうち学習するものだけ。武器の銘柄は除く）。</summary>
    private List<string> Recall(string kind, float[] features, ICollection<string> questions)
    {
        if (_recall == null || features == null || _p.RecallCount <= 0) return new List<string>();
        var learnable = new HashSet<string>(questions.Where(q => q != "weapon"));
        return _recall.FindSimilar(kind, features, learnable, _p.RecallCount)
            .Select(PersonaFeatures.DescribeRecord)
            .ToList();
    }

    /// <summary>資産 = 現金 + 在庫（陳列ぶんを含む）× 今日の価格。</summary>
    private static int Worth(PersonaDaySnapshot s)
    {
        long sum = s.Cash + (s.Shop?.PortfolioValue ?? 0);   // 取引所に入れたお金も資産（入れた日に損と数えない）
        foreach (var i in s.Items) sum += (long)Mathf.Max(0, i.Stock) * Mathf.Max(0, i.Price);
        return (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, sum));
    }

    /// <summary>今の資産（在庫が読めなければ null）。</summary>
    private static int? WorthNow(int money)
    {
        var items = AutoPlayUi.Resolve<ItemModel>();
        if (items?.RuntimeItems == null) return null;
        long sum = money + PortfolioValueNow();
        foreach (var r in items.RuntimeItems)
        {
            if (r == null) continue;
            sum += (long)Mathf.Max(0, r.Stock.Value) * Mathf.Max(0, r.CurrentPrice.Value);
        }
        return (int)Math.Min(int.MaxValue, Math.Max(int.MinValue, sum));
    }

    private static int HeroLevel()
    {
        try
        {
            var hero = AutoPlayUi.Resolve<HeroModel>();
            return hero?.heroData?.level != null ? hero.heroData.level.Value : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>問い合わせに失敗した。局面（day / stream）ごとに最初の 1 回だけログを出し、標準戦略で続ける。</summary>
    private void Fallback(string phase, int day, string error)
    {
        if (_warned.Add(phase))
        {
            Debug.LogWarning($"[PersonaPlay] {_p.LabelJa}: {phase} の問い合わせに失敗したので標準の判断で続けます: {error}");
            AutoPlay.AddLog($"{_p.LabelJa}: {phase} の判断はモデルに聞けなかった（{error}）→ 標準の判断で続行");
        }
        string line = PersonaLines.Pick(_p.Id, "fallback", PersonaLines.Band.Low, null, null, _rng, _memes, _mood.Label);
        _log?.Write(new PersonaLogEntry
        {
            Day = day, Persona = _p.Id, Model = Model, Question = phase, Choice = "default",
            Line = line, Meme = _memes?.MemeFor(line), MemeCue = _memes?.CueFor(line), Fallback = true,
        });
    }
}
#endif
