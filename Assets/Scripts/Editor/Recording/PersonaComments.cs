using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
/// - GPU サーバーの LLM（D:\Tools\tokotom-live\llm\GPU_LLM_SPEC.md）: LlmEndpoint が返す（endpoint.json が RUNNING かつ ready）ときは
///   LLM に頼み（別スレッド。Tick は待たない）、3 秒で返らなければそのコメントには返さない。検査に落ちたら返さずに次のコメントへ。
///   どちらも行は書くが line を付けず、rejected に捨てた文を入れる（読み上げ側は line が空の行を読まない）。
///   行の source は "llm" / "template"、LLM に頼んだ行は llmMs（頼んでから返るまで）。配信の終わりに kind = "llm_summary" の行で捨てた割合を書く。
///   LlmEndpoint が null（ファイルが無い・止まっている・準備中）なら今までどおりテンプレート。つながらない（打ち切り以外の失敗）ときもテンプレート。
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
        /// <summary>この配信で LLM に頼んだ数・使った数・捨てた数（配信の終わりに llm_summary の行にする）。</summary>
        [JsonProperty("llm")] public LlmStats Llm = new();
    }

    public sealed class LlmStats
    {
        [JsonProperty("tried")] public int Tried;
        [JsonProperty("used")] public int Used;
        /// <summary>捨てた数（検査に落ちた＋打ち切り）。</summary>
        [JsonProperty("dropped")] public int Dropped;
        [JsonProperty("timeout")] public int Timeout;
        /// <summary>つながらない等（テンプレートで返した）。</summary>
        [JsonProperty("error")] public int Error;
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
        [JsonProperty("to", NullValueHandling = NullValueHandling.Ignore)] public string To;

        public PersonaComment ToComment() => new()
        {
            Id = Id, UnixMs = UnixMs, Author = Author, Text = Text, Cat = Cat, First = First, SuperchatAmount = Amount, To = To,
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
    /// <summary>LLM のつなぎ先（毎回読み直す。null = テンプレート）。</summary>
    public Func<PersonaCommentLlm.Endpoint> LlmEndpoint = () => null;
    /// <summary>LLM に頼む（content を返す。打ち切りは ct）。テストでは差し替える。</summary>
    public Func<PersonaCommentLlm.Endpoint, string, CancellationToken, Task<string>> LlmPost = PersonaCommentLlm.PostAsync;
    public long LlmTimeoutMs = PersonaCommentLlm.TimeoutMs;

    /// <summary>LLM に頼んで返りを待っているコメント（状態ファイルには残さない。ドメインの再読み込みで消えたら返さない）。</summary>
    private sealed class Flight
    {
        public PersonaComment Comment;
        public PersonaCommentFacts Facts;
        public long StartMs;
        public Task<(string content, long ms)> Task;
        public CancellationTokenSource Cts;
    }

    private Flight _flight;
    public bool LlmBusy => _flight != null;

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
            written.AddRange(FlushLlmStats(s));
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

        if (_flight != null)
        {
            // LLM の返りを待っている: 返ったら書く（待っているあいだは次のコメントを取らない）
            var rows = PollFlight(s, now);
            if (rows != null)
            {
                written.AddRange(rows);
                dirty = true;
            }
        }
        else if (s.Pending.Count > 0 && now - s.LastReplyUnixMs >= GapMs && now - LastDecisionWriteMs() >= QuietGapMs)
        {
            // スパチャ → 新しい順
            var pick = s.Pending.OrderByDescending(p => p.Amount != null).ThenByDescending(p => p.UnixMs).First();
            s.Pending.Remove(pick);
            Remember(s, pick.Id);
            dirty = true;
            var c = pick.ToComment();
            var facts = Facts() ?? new PersonaCommentFacts();
            PersonaCommentLlm.Endpoint ep = null;
            try { ep = LlmEndpoint?.Invoke(); }
            catch (Exception) { ep = null; }
            if (ep != null)
            {
                s.Llm.Tried++;
                StartFlight(ep, c, facts, now);
                var rows = PollFlight(s, now);   // すぐ返った（テストの差し替え）ならこの回で書く
                if (rows != null) written.AddRange(rows);
            }
            else
            {
                written.AddRange(WriteTemplate(s, c, facts, now, null, null));
            }
        }
        if (dirty) SaveState();
        return written;
    }

    /// <summary>テンプレートで返す（今までどおり）。llmMs / llmError は LLM がつながらなかったときだけ。</summary>
    private List<Dictionary<string, object>> WriteTemplate(State s, PersonaComment c, PersonaCommentFacts facts, long now, long? llmMs, string llmError)
    {
        string cat = PersonaCommentLines.NormalizeCat(c);
        var lines = PersonaCommentLines.Reply(c, cat, facts, Rng, s.Counts, s.Recent);
        if (lines.Count == 0) return new List<Dictionary<string, object>>();
        var extra = new Dictionary<string, object> { ["source"] = "template" };
        if (llmMs.HasValue) extra["llmMs"] = llmMs.Value;
        if (!string.IsNullOrEmpty(llmError)) extra["llmError"] = llmError;
        return WriteExchange(s, c, cat, lines.Select(l => (l.Speaker, l.Line, l.E ?? "normal")).ToList(), now, extra);
    }

    private void StartFlight(PersonaCommentLlm.Endpoint ep, PersonaComment c, PersonaCommentFacts facts, long now)
    {
        string body = PersonaCommentLlm.BuildRequest(ep.Model, c.Text, c.SuperchatAmount, facts);
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, LlmTimeoutMs)));
        var post = LlmPost;
        var task = Task.Run(async () =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string content = await post(ep, body, cts.Token).ConfigureAwait(false);
            return (content, sw.ElapsedMilliseconds);
        });
        _flight = new Flight { Comment = c, Facts = facts, StartMs = now, Task = task, Cts = cts };
        // すぐ返るもの（テストの差し替え）はこの回で書けるよう少しだけ待つ（本番の HTTP はここでは待たない）
        try { task.Wait(TimeSpan.FromMilliseconds(50)); }
        catch (Exception) { /* 下の PollFlight で扱う */ }
    }

    /// <summary>返りを見る。まだなら null。返った・打ち切ったら書いた行（捨てた行を含む）。</summary>
    private List<Dictionary<string, object>> PollFlight(State s, long now)
    {
        var f = _flight;
        if (f == null) return null;
        var c = f.Comment;
        if (!f.Task.IsCompleted)
        {
            if (now - f.StartMs <= LlmTimeoutMs) return null;
            try { f.Cts.Cancel(); }
            catch (Exception) { /* もう終わっている */ }
            _flight = null;
            s.Llm.Timeout++;
            s.Llm.Dropped++;
            return WriteDropped(s, c, PersonaCommentLines.NormalizeCat(c), null, new List<string> { "打ち切り" }, now - f.StartMs, now);
        }
        _flight = null;
        f.Cts.Dispose();
        if (f.Task.IsFaulted || f.Task.IsCanceled)
        {
            var ex = f.Task.Exception?.GetBaseException();
            bool timeout = f.Task.IsCanceled || ex is OperationCanceledException;
            if (timeout)
            {
                s.Llm.Timeout++;
                s.Llm.Dropped++;
                return WriteDropped(s, c, PersonaCommentLines.NormalizeCat(c), null, new List<string> { "打ち切り" }, Math.Max(LlmTimeoutMs, now - f.StartMs), now);
            }
            // つながらない等（打ち切り以外の失敗）: そのコメントはテンプレートで返す
            s.Llm.Error++;
            return WriteTemplate(s, c, f.Facts, now, now - f.StartMs, ex?.Message ?? "error");
        }
        var (content, ms) = f.Task.Result;
        var reply = PersonaCommentLlm.Evaluate(content, c.Text ?? "", c.SuperchatAmount, f.Facts);
        // 返ってきた cat を使う（受け箱の cat より正しい）。スパチャは額があれば superchat に固定
        string cat = c.SuperchatAmount != null ? "superchat" : reply.Cat ?? PersonaCommentLines.NormalizeCat(c);
        if (!reply.Ok)
        {
            s.Llm.Dropped++;
            return WriteDropped(s, c, cat, reply, reply.Problems, ms, now);
        }
        s.Llm.Used++;
        string lead = PersonaCommentLlm.Lead(c.To, cat);
        var lines = new List<(string speaker, string line, string e)>();
        foreach (var who in new[] { lead, lead == "z" ? "m" : "z" })
        {
            string line = who == "z" ? reply.Z : reply.M;
            if (!string.IsNullOrEmpty(line)) lines.Add((who, line, PersonaCommentLines.DefaultE(who, cat)));
        }
        return WriteExchange(s, c, cat, lines, now, new Dictionary<string, object> { ["source"] = "llm", ["llmMs"] = ms });
    }

    private Dictionary<string, object> BaseRow(long now, int exchange, int seq)
    {
        var ctx = Context();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
        return new Dictionary<string, object>
        {
            ["time"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["unixMs"] = now,
            ["run"] = ctx.run ?? "",
            ["day"] = ctx.day,
            ["phase"] = ctx.phase ?? "",
            ["exchange"] = exchange,
            ["seq"] = seq,
            ["kind"] = "comment",
        };
    }

    private static void AddComment(Dictionary<string, object> row, PersonaComment c, string cat)
    {
        row["commentId"] = c.Id;
        row["commentAuthor"] = c.Author ?? "";
        row["commentText"] = !string.IsNullOrEmpty(c.Text) ? c.Text : c.SuperchatAmount ?? "";
        row["commentCat"] = cat;
    }

    /// <summary>やり取りの行を書く（2 行は 1 回で書く。読み上げ側が途中までしか見ないことがないように）。</summary>
    private List<Dictionary<string, object>> WriteExchange(State s, PersonaComment c, string cat, List<(string speaker, string line, string e)> lines,
        long now, Dictionary<string, object> extra)
    {
        var written = new List<Dictionary<string, object>>();
        if (lines.Count == 0) return written;
        int ex = ++s.Exchange;
        for (int i = 0; i < lines.Count; i++)
        {
            var row = BaseRow(now, ex, i);
            row["speaker"] = lines[i].speaker;
            row["line"] = lines[i].line;
            row["e"] = lines[i].e ?? "normal";
            AddComment(row, c, cat);
            foreach (var kv in extra) row[kv.Key] = kv.Value;
            written.Add(row);
        }
        Append(now, written);
        s.LastReplyUnixMs = now;
        return written;
    }

    /// <summary>
    /// 捨てた返事の行（line は付けない。読み上げ側は line が空の行を読まない）。rejected に捨てた文、problems に理由、dropped = true。
    /// 返事の間隔は空けない（次のコメントへすぐ進む）。
    /// </summary>
    private List<Dictionary<string, object>> WriteDropped(State s, PersonaComment c, string cat, PersonaCommentLlm.Reply reply, List<string> problems,
        long llmMs, long now)
    {
        var parts = new List<(string speaker, string text)>();
        if (reply != null)
        {
            string lead = PersonaCommentLlm.Lead(c.To, cat);
            foreach (var who in new[] { lead, lead == "z" ? "m" : "z" })
            {
                string t = who == "z" ? reply.Z : reply.M;
                if (!string.IsNullOrEmpty(t)) parts.Add((who, t));
            }
        }
        if (parts.Count == 0) parts.Add(("", reply?.Raw ?? ""));
        var all = problems ?? new List<string>();
        var written = new List<Dictionary<string, object>>();
        int ex = ++s.Exchange;
        for (int i = 0; i < parts.Count; i++)
        {
            string who = parts[i].speaker;
            var row = BaseRow(now, ex, i);
            row["speaker"] = who;
            row["rejected"] = parts[i].text;
            // その人の理由と、2 人に掛かる理由（人の印が無いもの）
            row["problems"] = all.Where(p => who == "" || !(p.StartsWith("z:") || p.StartsWith("m:")) || p.StartsWith(who + ":")).ToList();
            row["dropped"] = true;
            row["e"] = "normal";
            AddComment(row, c, cat);
            row["source"] = "llm";
            row["llmMs"] = llmMs;
            written.Add(row);
        }
        Append(now, written);
        return written;
    }

    private void Append(long now, List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var row in rows) sb.Append(JsonConvert.SerializeObject(row)).Append('\n');
        var at = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
        string path = LogPathFor(at.LocalDateTime);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// 配信の終わり（live.flag が消えた）: この配信で LLM に頼んでいれば、捨てた割合を kind = "llm_summary" の行（line なし）で書き、数え直す。
    /// 別の配信になったとき（Tick の streamId が変わった）も同じ行を書く。
    /// </summary>
    public List<Dictionary<string, object>> EndStream()
    {
        var s = Current;
        if (_flight != null)
        {
            try { _flight.Cts.Cancel(); }
            catch (Exception) { /* もう終わっている */ }
            _flight = null;
        }
        var rows = FlushLlmStats(s);
        if (rows.Count > 0) SaveState();
        return rows;
    }

    private List<Dictionary<string, object>> FlushLlmStats(State s)
    {
        var rows = new List<Dictionary<string, object>>();
        var st = s.Llm ?? new LlmStats();
        s.Llm = new LlmStats();
        if (st.Tried <= 0) return rows;
        long now = NowMs();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(now).ToLocalTime();
        double rate = st.Dropped / (double)st.Tried;
        rows.Add(new Dictionary<string, object>
        {
            ["time"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
            ["unixMs"] = now,
            ["kind"] = "llm_summary",
            ["stream"] = s.Stream ?? "",
            ["tried"] = st.Tried,
            ["used"] = st.Used,
            ["dropped"] = st.Dropped,
            ["timeout"] = st.Timeout,
            ["error"] = st.Error,
            ["dropRate"] = Math.Round(rate, 3),
        });
        Append(now, rows);
        Debug.Log($"[PersonaComments] この配信の LLM の返事: 頼んだ {st.Tried} / 使った {st.Used} / 捨てた {st.Dropped}（{rate:P0}、うち打ち切り {st.Timeout}）/ つながらずテンプレート {st.Error}");
        return rows;
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
                Id = c.Id, UnixMs = c.UnixMs, Author = c.Author, Text = c.Text, Cat = c.Cat, First = c.First, Amount = c.SuperchatAmount, To = c.To,
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
                    st.Llm ??= new LlmStats();
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
    private static bool _wasLive;
    private static bool _llmOn = true;
    private static string _llmEndpointPath;
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
            if (!File.Exists(LiveFlagPath))
            {
                // 配信していない: 何もしない（設定ファイルも読まない）。配信が終わった直後だけ、LLM の捨てた割合を書く
                if (_wasLive && _engine != null)
                    foreach (var row in _engine.EndStream()) Debug.Log($"{LogPrefix} 配信の終わり: LLM 捨てた割合 {row["dropRate"]}");
                _wasLive = false;
                return;
            }
            _wasLive = true;
            string inbox = InboxPath(now);
            WriteStatus();
            if (!Active(LiveFlagPath, inbox)) return;
            _engine ??= Create();
            _engine.InboxPath = inbox;
            float gap = SessionState.GetFloat(GapOverrideKey, 0f);
            _engine.GapMs = gap > 0f ? (long)(gap * 1000) : PersonaCommentEngine.MinGapMs;
            string stream = File.GetLastWriteTimeUtc(LiveFlagPath).Ticks.ToString(CultureInfo.InvariantCulture);
            foreach (var row in _engine.Tick(stream))
            {
                if (row.TryGetValue("line", out var line))
                    Debug.Log($"{LogPrefix} {row["commentCat"]} → {row["speaker"]}: {line}（{(row.TryGetValue("source", out var src) ? src : "")}）");
                else if (row.TryGetValue("rejected", out var rej))
                    Debug.Log($"{LogPrefix} {row["commentCat"]} → {row["speaker"]}: 捨てた「{rej}」（{string.Join("・", row.TryGetValue("problems", out var pr) && pr is IEnumerable<string> ps ? ps : Array.Empty<string>())}）");
            }
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
        _inbox = Rooted(config.CommentInboxPath);
        _llmOn = config.CommentLlm;
        _llmEndpointPath = Rooted(config.CommentLlmEndpointPath);
        // 空なら受け箱（…	okotom-live\chat\inbox.jsonl）の隣の llm\endpoint.json（配信側が GPU サーバーの起動・停止のたびに書く）
        if (_llmEndpointPath == null && !string.IsNullOrEmpty(_inbox))
        {
            string chat = Path.GetDirectoryName(_inbox);
            string live = string.IsNullOrEmpty(chat) ? null : Path.GetDirectoryName(chat);
            if (!string.IsNullOrEmpty(live)) _llmEndpointPath = Path.Combine(live, "llm", "endpoint.json");
        }
        return _inbox;
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
        // GPU サーバーの LLM（endpoint.json は毎回読み直す。RUNNING かつ ready のときだけ。それ以外はテンプレート）
        LlmEndpoint = () => _llmOn ? PersonaCommentLlm.ReadEndpoint(_llmEndpointPath) : null,
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
