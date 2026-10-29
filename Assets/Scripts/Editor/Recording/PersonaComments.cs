using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 配信コメントの受け箱を読み、返事を Recordings/comment_log_&lt;yyyyMMdd&gt;.jsonl に書く（D:\Tools\tokotom-live\COMMENT_SPEC.md / COMMENT_LIVE_SIDE.md）。
/// 判断・確率・学習・教訓・マラソンの値には触らない（読むのはゲームの状態だけ。乱数は専用の System.Random）。
/// persona_log / consult_log には書かない（集計に混ざらないように）。
///
/// - 読んだ位置（バイト位置）・返したコメントの id・配信ごとの使った行の数は状態ファイルに残す（ドメインの再読み込み・ランをまたいで同じコメントに 2 回返さない）。
///   受け箱が読んだ位置より小さくなったら（live_chat.py の起動で空になった）頭から読み直す。
/// - 受け箱の unixMs から StaleMs を過ぎたコメントは返さずに捨てる。
/// - 返すのは 1 回に 1 件: 前の返事から MinGapMs 以上たち、判断の一言（persona_log / consult_log）が QuietMs 以上書かれていないとき。
///   やり取りの 2 行は同時に書く（読み上げ側は 30 秒を過ぎた返事を捨てるので、言わせる直前に書く）。
/// </summary>
public sealed class PersonaCommentEngine
{
    public const long StaleMs = 60_000;
    public const long MinGapMs = 40_000;
    public const long QuietMs = 8_000;
    private const int KeepIds = 500;

    public sealed class State
    {
        [JsonProperty("inbox")] public string Inbox = "";
        [JsonProperty("offset")] public long Offset;
        [JsonProperty("lastId")] public string LastId = "";
        [JsonProperty("answered")] public List<string> Answered = new();
        [JsonProperty("pending")] public List<PendingComment> Pending = new();
        [JsonProperty("exchange")] public int Exchange;
        [JsonProperty("lastReplyUnixMs")] public long LastReplyUnixMs;
        /// <summary>配信の印（live.flag の更新時刻）。変わったら使った行の数を数え直す。</summary>
        [JsonProperty("stream")] public string Stream = "";
        [JsonProperty("counts")] public Dictionary<string, int> Counts = new();
        [JsonProperty("recent")] public List<string> Recent = new();
    }

    public sealed class PendingComment
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("unixMs")] public long UnixMs;
        [JsonProperty("author")] public string Author;
        [JsonProperty("text")] public string Text;
        [JsonProperty("cat")] public string Cat;
        [JsonProperty("first")] public bool First;
        [JsonProperty("amount")] public string Amount;

        public PersonaComment ToComment() => new()
        {
            Id = Id, UnixMs = UnixMs, Author = Author, Text = Text, Cat = Cat, First = First, SuperchatAmount = Amount,
        };
    }

    // ---- 外から渡すもの（テストでは差し替える） ----
    public string InboxPath;
    public string StatePath;
    public string OutDir;
    public Func<long> NowMs = () => DateTimeOffset.Now.ToUnixTimeMilliseconds();
    public Func<PersonaCommentFacts> Facts = () => new PersonaCommentFacts();
    /// <summary>判断の一言のログに最後に書いた時刻（unix ms。分からなければ 0）。</summary>
    public Func<long> LastDecisionWriteMs = () => 0;
    /// <summary>行の共通の欄（run / day / phase）。</summary>
    public Func<(string run, int day, string phase)> Context = () => ("", 0, "between");
    public long GapMs = MinGapMs;
    public long QuietGapMs = QuietMs;
    public System.Random Rng = new();

    private State _state;
    public State Current => _state ??= LoadState();

    public string LogPathFor(DateTime local) => Path.Combine(OutDir, $"comment_log_{local:yyyyMMdd}.jsonl");

    /// <summary>
    /// 1 回の確認。streamId = 配信の印（live.flag の更新時刻など）。返事を書いたら書いた行を返す（書かなければ空）。
    /// live でないときは呼ばないこと（呼ぶ側が live.flag と受け箱を確かめる）。
    /// </summary>
    public List<Dictionary<string, object>> Tick(string streamId)
    {
        var written = new List<Dictionary<string, object>>();
        var s = Current;
        bool dirty = false;
        if (s.Stream != (streamId ?? ""))
        {
            s.Stream = streamId ?? "";
            s.Counts.Clear();
            s.Recent.Clear();
            dirty = true;
        }
        dirty |= ReadInbox(s);

        long now = NowMs();
        int before = s.Pending.Count;
        s.Pending.RemoveAll(p => now - p.UnixMs > StaleMs);
        dirty |= s.Pending.Count != before;

        if (s.Pending.Count > 0 && now - s.LastReplyUnixMs >= GapMs && now - LastDecisionWriteMs() >= QuietGapMs)
        {
            // スパチャ → 新しい順
            var pick = s.Pending.OrderByDescending(p => p.Amount != null).ThenByDescending(p => p.UnixMs).First();
            s.Pending.Remove(pick);
            Remember(s, pick.Id);
            dirty = true;
            var c = pick.ToComment();
            string cat = PersonaCommentLines.NormalizeCat(c);
            var lines = PersonaCommentLines.Reply(c, cat, Facts() ?? new PersonaCommentFacts(), Rng, s.Counts, s.Recent);
            if (lines.Count > 0)
            {
                int ex = ++s.Exchange;
                var ctx = Context();
                var at = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
                string text = !string.IsNullOrEmpty(c.Text) ? c.Text : c.SuperchatAmount ?? "";
                var sb = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                {
                    var row = new Dictionary<string, object>
                    {
                        ["time"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                        ["unixMs"] = now,
                        ["run"] = ctx.run ?? "",
                        ["day"] = ctx.day,
                        ["phase"] = ctx.phase ?? "",
                        ["exchange"] = ex,
                        ["seq"] = i,
                        ["kind"] = "comment",
                        ["speaker"] = lines[i].Speaker,
                        ["line"] = lines[i].Line,
                        ["e"] = lines[i].E ?? "normal",
                        ["commentId"] = c.Id,
                        ["commentAuthor"] = c.Author ?? "",
                        ["commentText"] = text,
                        ["commentCat"] = cat,
                    };
                    written.Add(row);
                    sb.Append(JsonConvert.SerializeObject(row)).Append('\n');
                }
                // やり取りの 2 行は 1 回で書く（読み上げ側が途中までしか見ないことがないように）
                string path = LogPathFor(at.LocalDateTime);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                s.LastReplyUnixMs = now;
            }
        }
        if (dirty) SaveState();
        return written;
    }

    private static void Remember(State s, string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        s.Answered.Remove(id);
        s.Answered.Add(id);
        while (s.Answered.Count > KeepIds) s.Answered.RemoveAt(0);
    }

    /// <summary>受け箱の書き足された分を読む（改行で終わった行だけ）。状態が変わったら true。</summary>
    private bool ReadInbox(State s)
    {
        if (string.IsNullOrEmpty(InboxPath) || !File.Exists(InboxPath)) return false;
        bool dirty = false;
        string full = Path.GetFullPath(InboxPath);
        if (!string.Equals(s.Inbox, full, StringComparison.OrdinalIgnoreCase))
        {
            s.Inbox = full;
            s.Offset = 0;
            dirty = true;
        }
        byte[] bytes;
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            if (len < s.Offset)
            {
                // 空にされた（live_chat.py の起動）: 頭から。id の重複は返した id の控えで防ぐ
                s.Offset = 0;
                dirty = true;
            }
            if (len == s.Offset) return dirty;
            fs.Seek(s.Offset, SeekOrigin.Begin);
            bytes = new byte[len - s.Offset];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = fs.Read(bytes, read, bytes.Length - read);
                if (n <= 0) break;
                read += n;
            }
            if (read < bytes.Length) Array.Resize(ref bytes, read);
        }
        catch (IOException)
        {
            return dirty;   // 書き込み中などで開けない: 次の確認で読む
        }
        int lastNl = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNl < 0) return dirty;   // まだ 1 行書き終わっていない
        int start = 0;
        if (s.Offset == 0 && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) start = 3;
        string chunk = Encoding.UTF8.GetString(bytes, start, lastNl + 1 - start);
        s.Offset += lastNl + 1;
        dirty = true;
        long now = NowMs();
        foreach (var raw in chunk.Split('\n'))
        {
            var c = PersonaComment.Parse(raw.TrimEnd('\r'));
            if (c == null) continue;
            if (s.Answered.Contains(c.Id) || s.Pending.Any(p => p.Id == c.Id)) continue;
            s.LastId = c.Id;
            if (now - c.UnixMs > StaleMs)
            {
                Remember(s, c.Id);   // 間に合わなかった: 捨てる（読み直しても返さない）
                continue;
            }
            s.Pending.Add(new PendingComment
            {
                Id = c.Id, UnixMs = c.UnixMs, Author = c.Author, Text = c.Text, Cat = c.Cat, First = c.First, Amount = c.SuperchatAmount,
            });
        }
        return dirty;
    }

    private State LoadState()
    {
        try
        {
            if (!string.IsNullOrEmpty(StatePath) && File.Exists(StatePath))
            {
                var st = JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath));
                if (st != null)
                {
                    st.Answered ??= new List<string>();
                    st.Pending ??= new List<PendingComment>();
                    st.Counts ??= new Dictionary<string, int>();
                    st.Recent ??= new List<string>();
                    return st;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaComments] 状態を読めないので初めからにします: {e.Message}");
        }
        return new State();
    }

    public void SaveState()
    {
        if (string.IsNullOrEmpty(StatePath) || _state == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_state, Formatting.Indented));
            if (File.Exists(StatePath)) File.Replace(tmp, StatePath, null);
            else File.Move(tmp, StatePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaComments] 状態を書けませんでした: {e.Message}");
        }
    }
}

/// <summary>
/// 配信コメントへの返事のエディタ側（EditorApplication.update。Play 中もランの合間も動く）。
/// Library/PersonaMarathon/live.flag があり、UserSettings/PersonaPlay.json の commentInboxPath が在るファイルを指すときだけ動く。
/// ゲームの状態は読むだけ（Play 中は所持金・日・返済・村資金・直前の配信の損益、合間は最後に見た村の情報）。
/// </summary>
[InitializeOnLoad]
public static class PersonaCommentResponder
{
    private const string LogPrefix = "[PersonaComments]";
    private const string VillageKey = "PersonaComments.Village";
    /// <summary>確かめるときだけ: 返事の間隔（秒）を縮める（SessionState。0 以下なら既定の 40 秒）。</summary>
    public const string GapOverrideKey = "PersonaComments.MinGapSec";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    public static string LiveFlagPath => Path.Combine(ProjectRoot, "Library", "PersonaMarathon", "live.flag");
    public static string StatePath => Path.Combine(ProjectRoot, "Library", "PersonaMarathon", "comment_state.json");

    private static PersonaCommentEngine _engine;
    private static double _nextPoll;
    private static double _nextConfig;
    private static string _inbox;
    private static DateTime _configTime;
    private static string _lastPersonaLog, _lastConsultLog;

    static PersonaCommentResponder()
    {
        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += () => { _statusTimer?.Dispose(); _statusTimer = null; };
    }

    private static void Update()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < _nextPoll) return;
        _nextPoll = now + 1.0;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;   // Play に入る・抜ける途中
        try
        {
            if (!File.Exists(LiveFlagPath)) return;   // 配信していない: 何もしない（設定ファイルも読まない）
            string inbox = InboxPath(now);
            WriteStatus();
            if (!Active(LiveFlagPath, inbox)) return;
            _engine ??= Create();
            _engine.InboxPath = inbox;
            float gap = SessionState.GetFloat(GapOverrideKey, 0f);
            _engine.GapMs = gap > 0f ? (long)(gap * 1000) : PersonaCommentEngine.MinGapMs;
            string stream = File.GetLastWriteTimeUtc(LiveFlagPath).Ticks.ToString(CultureInfo.InvariantCulture);
            foreach (var row in _engine.Tick(stream))
                Debug.Log($"{LogPrefix} {row["commentCat"]} → {row["speaker"]}: {row["line"]}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{LogPrefix} 返事の確認で失敗: {e.Message}");
            _nextPoll = now + 10.0;
        }
    }

    /// <summary>動くか: live.flag があり、受け箱のパスが空でなく、そのファイルがある。</summary>
    public static bool Active(string liveFlag, string inbox) =>
        !string.IsNullOrEmpty(liveFlag) && File.Exists(liveFlag) && !string.IsNullOrWhiteSpace(inbox) && File.Exists(inbox);

    private static string InboxPath(double now)
    {
        if (now < _nextConfig) return _inbox;
        _nextConfig = now + 5.0;
        string file = Path.Combine(ProjectRoot, PersonaPlayConfig.RelativePath);
        if (!File.Exists(file)) return _inbox = null;
        var t = File.GetLastWriteTimeUtc(file);
        if (t == _configTime && _inbox != null) return _inbox;
        _configTime = t;
        var config = PersonaPlayConfig.Load();
        _statusPath = Rooted(config.LiveStatusPath);
        return _inbox = Rooted(config.CommentInboxPath);
    }

    private static string Rooted(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.IsPathRooted(path) ? path : Path.Combine(ProjectRoot, path);

    // =====================================================
    // 配信画面の状態（live_status.json。値が変わったとき・変わらなくても 10 秒に 1 回）
    // =====================================================

    private const long StatusRefreshMs = 10_000;
    private static string _statusPath;
    private static string _lastStatusKey;
    private static long _lastStatusMs;

    private static void WriteStatus()
    {
        if (string.IsNullOrEmpty(_statusPath)) return;
        var f = GatherFacts();
        var (run, _, phase) = RowContext();
        if (!EditorApplication.isPlaying) phase = "between";   // ランの合間（Play を抜けている）
        if (string.IsNullOrEmpty(run)) run = PersonaLog.RunTag ?? "";
        int day = f.InRun ? f.Day : -1;
        int gold = f.InRun ? f.Money : -1;
        string key = $"{run}|{phase}|{day}|{gold}|{f.VillageFunds}";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (key == _lastStatusKey && now - _lastStatusMs < StatusRefreshMs) return;

        var row = new Dictionary<string, object> { ["unixMs"] = now, ["run"] = run, ["phase"] = phase, ["day"] = day };
        if (gold >= 0) row["gold"] = gold;
        if (f.VillageFunds >= 0) row["villageFund"] = f.VillageFunds;
        lock (StatusLock)
        {
            WriteStatusFile(_statusPath, row);
            _lastStatusRow = row;
            _lastStatusKey = key;
            _lastStatusMs = now;
        }
        _statusLiveFlag = LiveFlagPath;
        _statusTimer ??= new System.Threading.Timer(_ => RefreshStatus(), null, 5000, 5000);
    }

    private static readonly object StatusLock = new();
    private static Dictionary<string, object> _lastStatusRow;
    private static string _statusLiveFlag;
    private static System.Threading.Timer _statusTimer;

    /// <summary>
    /// ランの合間はエディタが裏に回ると update がほとんど来ないので、最後の値の時刻だけを別スレッドで書き直す
    /// （配信側は 60 秒より古いファイルを使わない）。Unity の API は触らない。
    /// </summary>
    private static void RefreshStatus()
    {
        try
        {
            lock (StatusLock)
            {
                if (_lastStatusRow == null || string.IsNullOrEmpty(_statusPath) || !File.Exists(_statusLiveFlag)) return;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now - _lastStatusMs < StatusRefreshMs) return;
                _lastStatusRow["unixMs"] = now;
                WriteStatusFile(_statusPath, _lastStatusRow);
                _lastStatusMs = now;
            }
        }
        catch (Exception) { /* 次の回に書く */ }
    }

    private static void WriteStatusFile(string path, Dictionary<string, object> row)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(row), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    private static PersonaCommentEngine Create() => new()
    {
        StatePath = StatePath,
        OutDir = Path.Combine(ProjectRoot, "Recordings"),
        Facts = GatherFacts,
        LastDecisionWriteMs = LastDecisionWrite,
        Context = RowContext,
    };

    // =====================================================
    // ゲームの状態（読むだけ）
    // =====================================================

    private sealed class VillageCache
    {
        [JsonProperty("funds")] public int Funds = -1;
        [JsonProperty("runs")] public int Runs = -1;
    }

    private static PersonaCommentFacts GatherFacts()
    {
        var f = new PersonaCommentFacts();
        VillageCache cache = null;
        try { cache = JsonConvert.DeserializeObject<VillageCache>(SessionState.GetString(VillageKey, "")); }
        catch (Exception) { /* 無ければ村の情報なし */ }
        cache ??= new VillageCache();
        if (EditorApplication.isPlaying)
        {
            try
            {
                var meta = AutoPlayUi.Resolve<MetaProgressModel>();
                if (meta != null)
                {
                    cache.Funds = meta.VillageFunds;
                    cache.Runs = meta.TotalRuns;
                    SessionState.SetString(VillageKey, JsonConvert.SerializeObject(cache));
                }
            }
            catch (Exception) { /* 村が読めなくても返事はできる */ }
            try
            {
                var toms = AutoPlayUi.Resolve<TomsModel>();
                if (toms != null && AutoPlay.IsRunning && AutoPlay.CurrentTurn > 0)
                {
                    f.InRun = true;
                    f.Day = AutoPlay.CurrentTurn;
                    f.Money = toms.PlayerMoney.Value;
                    try
                    {
                        f.DebtAmount = toms.GetNextDebtAmount();
                        int interval = GameConst.DebtPaymentInterval;
                        if (interval > 0) f.DebtDaysLeft = (toms.DebtCycle.Value + 1) * interval - AutoPlay.CurrentTurn;
                    }
                    catch (Exception) { /* 返済が読めなくても返事はできる */ }
                    f.LastStreamDelta = LastStreamDelta(f.Day);
                }
            }
            catch (Exception) { f.InRun = false; }
        }
        f.VillageFunds = cache.Funds;
        f.Runs = cache.Runs;
        return f;
    }

    /// <summary>今のランの persona_log の末尾から、直前（2 日以内）の配信の損益を探す。</summary>
    private static int? LastStreamDelta(int day)
    {
        string path = (AutoPlay.Strategy as PersonaStrategy)?.LogFilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - 96 * 1024);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = fs.Read(buf, 0, buf.Length);
            var lines = Encoding.UTF8.GetString(buf, 0, read).Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf("\"question\":\"stream_result\"", StringComparison.Ordinal) < 0) continue;
                var e = JsonConvert.DeserializeObject<PersonaLogEntry>(lines[i]);
                if (e?.Reward == null || e.Day < day - 2 || e.Day > day) return null;
                return Mathf.RoundToInt(e.Reward.Value);
            }
        }
        catch (Exception) { /* 読めなければ配信の話はしない */ }
        return null;
    }

    /// <summary>判断の一言のログ（今のランの persona_log / consult_log）に最後に書いた時刻。</summary>
    private static long LastDecisionWrite()
    {
        if (EditorApplication.isPlaying && AutoPlay.Strategy is PersonaStrategy ps)
        {
            if (!string.IsNullOrEmpty(ps.LogFilePath)) _lastPersonaLog = ps.LogFilePath;
            if (!string.IsNullOrEmpty(ps.ConsultLogPath)) _lastConsultLog = ps.ConsultLogPath;
        }
        long last = 0;
        foreach (var p in new[] { _lastPersonaLog, _lastConsultLog })
        {
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
            long t = new DateTimeOffset(File.GetLastWriteTimeUtc(p), TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (t > last) last = t;
        }
        return last;
    }

    private static (string run, int day, string phase) RowContext()
    {
        if (!EditorApplication.isPlaying) return ("", 0, "between");
        bool inRun = AutoPlay.IsRunning && AutoPlay.CurrentTurn > 0 && AutoPlayUi.Resolve<TomsModel>() != null;
        return (PersonaLog.RunTag ?? "", inRun ? AutoPlay.CurrentTurn : 0, inRun ? "day" : "village");
    }
}
