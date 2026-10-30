using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 配信コメントの返事を GPU サーバーの LLM（Ollama /api/chat、qwen3:14b）で作る（D:\Tools\tokotom-live\llm\GPU_LLM_SPEC.md）。
/// 頼み方・system・例・スキーマ・検査は D:\Tools\tokotom-live\llm\bench.py の SYSTEM / SHOTS / SCHEMA / check_line と同じ（測った形）。
/// 判断・確率・学習・教訓・マラソンの値には触らない（返事の文だけ）。
/// </summary>
public static class PersonaCommentLlm
{
    /// <summary>打ち切り（ユーザーの決定）。これで返らなければ、そのコメントには返さない（テンプレートにも戻さない）。</summary>
    public const long TimeoutMs = 3000;
    public const int MaxLen = 48;
    public const string DefaultModel = "qwen3:14b";

    public static readonly string[] Cats =
    {
        "greet", "cheer", "tease", "bet_hero", "bet_demon", "ask", "superchat", "other",
        "leave", "laugh", "clap", "game", "ai", "toko",
    };

    private static readonly string[] Banned = { "効いて", "効いた", "効いてくる", "回目" };
    private static readonly HashSet<string> AllowedLatin = new() { "ai", "steam", "g", "gg", "w" };

    // =====================================================
    // つなぎ先（endpoint.json。配信側が起動・停止のたびに書く）
    // =====================================================

    public sealed class Endpoint
    {
        public string Url;
        public string Model;
    }

    /// <summary>state == RUNNING かつ ready == true で url があるときだけ返す（それ以外・読めないときは null = テンプレート）。</summary>
    public static Endpoint ReadEndpoint(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                text = sr.ReadToEnd();
            return ParseEndpoint(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Endpoint ParseEndpoint(string json)
    {
        try
        {
            var o = JObject.Parse(json);
            if (!string.Equals((string)o["state"], "RUNNING", StringComparison.OrdinalIgnoreCase)) return null;
            if (o["ready"]?.Type != JTokenType.Boolean || !(bool)o["ready"]) return null;
            string url = o["url"]?.Type == JTokenType.String ? ((string)o["url"]).Trim() : null;
            if (string.IsNullOrEmpty(url) || IsLocalKev(url)) return null;
            string model = o["model"]?.Type == JTokenType.String ? (string)o["model"] : null;
            return new Endpoint { Url = url.TrimEnd('/'), Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>この PC の kev の Ollama（11434）か。そこには頼まない（判断の LLM と取り合わない）。</summary>
    public static bool IsLocalKev(string url) => Regex.IsMatch(url ?? "", @"//(127\.0\.0\.1|localhost|\[::1\]):11434\b", RegexOptions.IgnoreCase);

    // =====================================================
    // 頼み方（bench.py の chat と同じ形）
    // =====================================================

    private const string SystemTemplate =
        "あなたは YouTube ライブ配信のコメント係です。自作ゲーム「トコのトムトムランド」（武器屋が勇者に武器を売り、勇者のダンジョン配信の勝ち負けに賭けて儲ける。魔王側に張ってもよい。Steam にある）を、AI のずんだもんと四国めたんが遊んでいます。\n" +
        "視聴者のコメントを 1 件読み、種類を決めて、二人の短い返事を書きます。\n" +
        "\n" +
        "## 種類（cat）\n" +
        "greet=挨拶・初見 / cheer=応援・褒め / tease=からかい・皮肉・心配 / bet_hero=勇者に張れ / bet_demon=魔王に張れ / ask=ゲームの状態への質問（所持金・日・返済・村・目標） / superchat=スパチャ（入力に金額があるときだけ） / leave=見送り・おつ・おやすみ / laugh=笑い（草・w） / clap=拍手・おめでとう・ナイス / game=どんなゲームか・どこで買えるか・やってみたい / ai=AIなのか・中の人 / toko=トコちゃんの話 / other=それ以外の雑談\n" +
        "\n" +
        "## 人物\n" +
        "- ずんだもん: 一人称「ぼく」、語尾は必ず「〜のだ」「〜なのだ」。素直で、根拠の変な自信家（「ぼくの勘なのだ」）。失敗しても 2 行で立ち直る。皮肉・見下しはしない。\n" +
        "- 四国めたん: 語尾「〜わ」「〜のよ」「〜かしら」。「のだ」は使わない。先に調べる慎重派で金欠。短く刺すツンデレ。トコちゃんには甘い。長い説教はしない。\n" +
        "\n" +
        "## 決まり\n" +
        "- 返事は 1 人 1 文、{max_len} 文字以内、日本語だけ。コメントの本文や名前を返事に入れない。URL を書かない。\n" +
        "- ゲームの数字は下の「今の状態」にあるものだけを使う。無い数字を作らない。\n" +
        "- 「勇者に張れ」「魔王に張れ」には「考えておく」程度に流す（約束しない）。\n" +
        "- ずんだもんの返事は必ず「のだ」「なのだ」で終える。めたんの返事は「わ」「のよ」「かしら」などで終え、です・ます調にしない。\n" +
        "- 数字は、所持金・日・返済を聞かれたときだけ言う。挨拶や応援に数字を並べない。\n" +
        "- 「効いて」「効いた」「回目」は使わない。\n" +
        "- 出力は JSON だけ: {\"cat\": 種類, \"z\": ずんだもんの返事, \"m\": めたんの返事}\n" +
        "\n" +
        "## 今の状態\n" +
        "{facts}";

    private static readonly (string q, string a)[] Shots =
    {
        ("こんばんは！", "{\"cat\": \"greet\", \"z\": \"こんばんはなのだ！ ゆっくりしていくのだ\", \"m\": \"こんばんは。ちょうどいいところよ\"}"),
        ("ずんだもん今日も金欠？", "{\"cat\": \"tease\", \"z\": \"金欠じゃないのだ！ お金がちょっとお出かけしてるだけなのだ\", \"m\": \"所持金は45,370G。金欠かどうかは返済しだいね\"}"),
        ("めたんがんばれ", "{\"cat\": \"cheer\", \"z\": \"ぼくもがんばるのだ！\", \"m\": \"ありがとう。……がんばるわ\"}"),
    };

    public static string SystemText(string facts) => SystemTemplate.Replace("{max_len}", MaxLen.ToString(CultureInfo.InvariantCulture)).Replace("{facts}", facts ?? "");

    private static string G(long v) => v.ToString("N0", CultureInfo.InvariantCulture) + "G";

    /// <summary>
    /// 今の状態（bench.facts_text の形。分からない値は書かない）。
    /// ラン中: 「営業中・12日目・所持金 45,370G・次の返済まで 3日（30,000G）・村資金 6,743,099G・直前の配信の損益 プラス8,200G」
    /// 合間: 「ランの合間（村）・村資金 6,743,099G・ラン 91 回済み」
    /// </summary>
    public static string FactsText(PersonaCommentFacts f)
    {
        f ??= new PersonaCommentFacts();
        var parts = new List<string>();
        if (f.InRun)
        {
            parts.Add("営業中");
            if (f.Day > 0) parts.Add($"{f.Day}日目");
            if (f.Money >= 0) parts.Add($"所持金 {G(f.Money)}");
            if (f.DebtDaysLeft >= 1 && f.DebtAmount > 0) parts.Add($"次の返済まで {f.DebtDaysLeft}日（{G(f.DebtAmount)}）");
            if (f.VillageFunds >= 0) parts.Add($"村資金 {G(f.VillageFunds)}");
            if (f.LastStreamDelta.HasValue && f.LastStreamDelta.Value != 0)
                parts.Add($"直前の配信の損益 {(f.LastStreamDelta.Value > 0 ? "プラス" : "マイナス")}{G(Math.Abs((long)f.LastStreamDelta.Value))}");
        }
        else
        {
            parts.Add("ランの合間（村）");
            if (f.VillageFunds >= 0) parts.Add($"村資金 {G(f.VillageFunds)}");
            if (f.Runs > 0) parts.Add($"ラン {f.Runs} 回済み");
        }
        return string.Join("・", parts);
    }

    private static JObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JObject
        {
            ["cat"] = new JObject { ["type"] = "string", ["enum"] = new JArray(Cats.Cast<object>().ToArray()) },
            ["z"] = new JObject { ["type"] = "string" },
            ["m"] = new JObject { ["type"] = "string" },
        },
        ["required"] = new JArray("cat", "z", "m"),
    };

    /// <summary>/api/chat に送る JSON（1 回で分類と 2 人分）。</summary>
    public static string BuildRequest(string model, string commentText, string superchat, PersonaCommentFacts facts)
    {
        var messages = new JArray { new JObject { ["role"] = "system", ["content"] = SystemText(FactsText(facts)) } };
        foreach (var (q, a) in Shots)
        {
            messages.Add(new JObject { ["role"] = "user", ["content"] = "コメント: " + q });
            messages.Add(new JObject { ["role"] = "assistant", ["content"] = a });
        }
        string user = "コメント: " + (commentText ?? "");
        if (!string.IsNullOrEmpty(superchat)) user += "\nスパチャ: " + superchat;
        messages.Add(new JObject { ["role"] = "user", ["content"] = user });
        var body = new JObject
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? DefaultModel : model,
            ["stream"] = false,
            ["think"] = false,
            ["format"] = Schema(),
            ["options"] = new JObject { ["num_ctx"] = 4096, ["temperature"] = 0.6, ["num_predict"] = 200 },
            ["messages"] = messages,
        };
        return body.ToString(Formatting.None);
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>POST {url}/api/chat。返ってきた message.content（JSON の文字列）を返す。打ち切りは ct で掛ける。</summary>
    public static async Task<string> PostAsync(Endpoint ep, string body, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, ep.Url.TrimEnd('/') + "/api/chat")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var res = await Http.SendAsync(msg, ct).ConfigureAwait(false);
        string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)res.StatusCode}");
        var o = JObject.Parse(text);
        return (string)o["message"]?["content"] ?? "";
    }

    // =====================================================
    // 返事の読み取りと検査（bench.check_line と同じ）
    // =====================================================

    public sealed class Reply
    {
        public string Cat;
        public string Z = "";
        public string M = "";
        /// <summary>LLM が返したそのままの文（JSON として読めなかったときの記録用）。</summary>
        public string Raw = "";
        /// <summary>落ちた理由（空なら合格）。"z:長い50" のように人を付ける。</summary>
        public List<string> Problems = new();
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>LLM の返した content を読み、検査する。commentText = コメントの本文、superchat = スパチャの額（無ければ null）。</summary>
    public static Reply Evaluate(string content, string commentText, string superchat, PersonaCommentFacts facts)
    {
        var r = new Reply { Raw = content ?? "" };
        JObject o = null;
        try { o = JObject.Parse(content ?? ""); }
        catch (Exception) { /* 下で落とす */ }
        if (o == null)
        {
            r.Problems.Add("JSON でない");
            return r;
        }
        r.Cat = o["cat"]?.Type == JTokenType.String ? (string)o["cat"] : null;
        if (r.Cat != null && Array.IndexOf(Cats, r.Cat) < 0) r.Cat = null;
        r.Z = (o["z"]?.Type == JTokenType.String ? (string)o["z"] : "").Trim();
        r.M = (o["m"]?.Type == JTokenType.String ? (string)o["m"] : "").Trim();
        foreach (var p in CheckLine("z", r.Z, commentText, superchat, facts)) r.Problems.Add("z:" + p);
        foreach (var p in CheckLine("m", r.M, commentText, superchat, facts)) r.Problems.Add("m:" + p);
        if (r.Z.Length == 0 && r.M.Length == 0) r.Problems.Add("返事なし");
        if (r.Z.Length > 0 && r.Z == r.M) r.Problems.Add("2 人が同じ文");
        return r;
    }

    private static string Nfkc(string s) => (s ?? "").Normalize(NormalizationForm.FormKC);

    /// <summary>文字数（Python の len と同じくコードポイントで数える）。</summary>
    private static int CodePoints(string s) => s.Count(c => !char.IsLowSurrogate(c));

    private static readonly Regex DesuMasu = new(@"(です|ます|ません|でした|ました)(?:[。！？!?、…]|$|か|ね|よ)");
    private static readonly Regex BrokenEnding = new(@"(ね|わ|よ|かしら)のよ|ますかしら|のだわ|わのだ|かしらのだ|のだよね");
    private static readonly Regex Kana3 = new(@"[\u3040-\u30FF\u4E00-\u9FFF]{3,}");
    private static readonly Regex OnlySteam = new(@"(steam|スチーム)\s*(で|に)?\s*(しか|だけ)");
    private static readonly Regex NotOnSteam = new(@"(steam|スチーム).{0,8}(出て(い)?ない|出てません|無い|ない|ありません|売ってない|買えない|まだ)");
    private static readonly Regex OnlyOnStream = new(@"(youtube|ライブ|配信)(で)?(しか|だけ)(遊べ|でき)");
    private static readonly Regex NotReleased = new(@"(発売前|未発売|まだ出て|リリース前)");

    /// <summary>返事 1 つの検査。問題のリスト（空なら合格）。空の返事は検査しない（その人は黙る）。</summary>
    public static List<string> CheckLine(string who, string s, string commentText, string superchat, PersonaCommentFacts facts)
    {
        var bad = new List<string>();
        if (string.IsNullOrEmpty(s)) return bad;
        int len = CodePoints(s);
        if (len > MaxLen) bad.Add("長い" + len);
        if (who == "z" && !s.Contains("のだ")) bad.Add("ずんだもんの語尾");
        if (who == "m" && s.Contains("のだ")) bad.Add("めたんがのだ");
        if (who == "m" && s.Contains("ぼく")) bad.Add("めたんがぼく");
        if (DesuMasu.IsMatch(s)) bad.Add("です・ます");
        if (BrokenEnding.IsMatch(s)) bad.Add("語尾の崩れ");
        string n = Nfkc(s);
        if (Regex.IsMatch(n, "[wｗ]{2,}") || !Kana3.IsMatch(s)) bad.Add("文になっていない");
        foreach (var w in Banned)
            if (s.Contains(w)) bad.Add("禁止語:" + w);
        if (!PersonaLines.IsClean(s)) bad.Add("禁止語");
        if (Regex.IsMatch(s, @"https?://|www\.")) bad.Add("URL");
        if (s.Contains("<think>") || s.Contains("</think>")) bad.Add("思考");
        var inComment = new HashSet<string>(Regex.Matches(Nfkc(commentText), "[A-Za-z]+").Cast<Match>().Select(m => m.Value.ToLowerInvariant()));
        if (Regex.Matches(n, "[A-Za-z]+").Cast<Match>().Select(m => m.Value.ToLowerInvariant()).Any(w => !AllowedLatin.Contains(w) && !inComment.Contains(w)))
            bad.Add("英語");
        var ok = AllowedNumbers(facts, superchat);
        var made = Regex.Matches(n.Replace(",", ""), @"[0-9]+(?:\.[0-9]+)?").Cast<Match>().Select(m => m.Value)
            .Where(x => !ok.Contains(x) && x.Length >= 2).ToList();
        if (made.Count > 0) bad.Add("作り話の数字:" + string.Join("/", made));
        string t = commentText ?? "";
        if (CodePoints(t) >= 6 && s.Contains(t)) bad.Add("コメントの丸写し");
        string lower = n.ToLowerInvariant();
        if ((!OnlySteam.IsMatch(lower) && NotOnSteam.IsMatch(lower)) || OnlyOnStream.IsMatch(lower) || NotReleased.IsMatch(lower))
            bad.Add("事実と逆");
        return bad;
    }

    /// <summary>言ってよい数字（今の状態に出したものと、その「4.5万」「4万5370」の言い方、スパチャの額）。</summary>
    public static HashSet<string> AllowedNumbers(PersonaCommentFacts f, string superchat)
    {
        f ??= new PersonaCommentFacts();
        var nums = new HashSet<string>();
        void Add(long v) => nums.Add(v.ToString(CultureInfo.InvariantCulture));
        void Money(long v)
        {
            if (v < 0) return;
            Add(v);
            Add(v / 10000);
            nums.Add(Math.Round(v / 10000.0, 1, MidpointRounding.ToEven).ToString("0.#", CultureInfo.InvariantCulture));
            Add(v % 10000);
        }
        if (f.InRun)
        {
            if (f.Day > 0) Add(f.Day);
            if (f.Money >= 0) Money(f.Money);
            if (f.DebtDaysLeft >= 1 && f.DebtAmount > 0)
            {
                Add(f.DebtDaysLeft);
                Money(f.DebtAmount);
            }
            if (f.LastStreamDelta.HasValue && f.LastStreamDelta.Value != 0) Money(Math.Abs((long)f.LastStreamDelta.Value));
        }
        else if (f.Runs > 0) Add(f.Runs);
        if (f.VillageFunds >= 0) Money(f.VillageFunds);
        if (!string.IsNullOrEmpty(superchat))
            foreach (Match m in Regex.Matches(Nfkc(superchat).Replace(",", ""), "[0-9]+")) nums.Add(m.Value);
        return nums;
    }

    /// <summary>先に話す人: 名指しがあればその人、無ければ ask / toko はめたん、それ以外はずんだもん。</summary>
    public static string Lead(string to, string cat) => to is "z" or "m" ? to : cat is "ask" or "toko" ? "m" : "z";
}
