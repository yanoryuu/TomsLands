#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using Debug = UnityEngine.Debug;

// =====================================================================
// 人格プレイ（ずんだもん・四国めたん）用の Jev 互換クライアント。
// リクエスト / 回答の JSON は Editor の JevApi.cs と同じ形（model / state / questions → answers）。
// 送り先はローカルの Ollama（POST /v1/systemone）。API キーは使わない。
// Editor の JevApi（本物の Jev を叩く）とは別物で、そちらの動作は変えない。
// 型名は Editor 側の JevQuestion などと衝突しないよう Persona を付ける。
// =====================================================================

/// <summary>質問1件（JevQuestion と同じ JSON）。type は "noul" / "choice" / "score"。</summary>
public sealed class PersonaJevQuestion
{
    /// <summary>Ollama の choice は選択肢 26 件まで（それを超えると 400）。呼ぶ側で関係の深い順に絞る。</summary>
    public const int MaxChoiceOptions = 26;

    [JsonProperty("type")] public string Type;
    [JsonProperty("instructions")] public object Instructions;
    [JsonProperty("criteria", NullValueHandling = NullValueHandling.Ignore)] public object Criteria;

    /// <summary>選択式。26 件を超えたら先頭から 26 件だけ使う（並べる側で優先順にしておく）。</summary>
    public static PersonaJevQuestion Choice(string instructions, IEnumerable<KeyValuePair<string, string>> options)
    {
        var criteria = new Dictionary<string, string>();
        foreach (var pair in options)
        {
            if (criteria.Count >= MaxChoiceOptions) break;
            if (string.IsNullOrEmpty(pair.Key) || criteria.ContainsKey(pair.Key)) continue;
            criteria[pair.Key] = pair.Value ?? "";
        }
        if (criteria.Count == 0) throw new ArgumentException("choice 質問には選択肢を1件以上指定してください。", nameof(options));
        return new PersonaJevQuestion { Type = "choice", Instructions = instructions, Criteria = criteria };
    }

    /// <summary>段階評価。levels はレベル0から順の説明（2〜10件）。</summary>
    public static PersonaJevQuestion Score(string instructions, IList<string> levels)
    {
        if (levels == null || levels.Count < 2 || levels.Count > 10)
            throw new ArgumentException("score 質問のレベル説明は2〜10件で指定してください。", nameof(levels));
        return new PersonaJevQuestion { Type = "score", Instructions = instructions, Criteria = new List<string>(levels) };
    }

    /// <summary>真偽度。</summary>
    public static PersonaJevQuestion Noul(string instructions, string whenTrue = null, string whenFalse = null)
    {
        Dictionary<string, string> map = null;
        if (whenTrue != null || whenFalse != null)
        {
            map = new Dictionary<string, string>();
            if (whenTrue != null) map["true"] = whenTrue;
            if (whenFalse != null) map["false"] = whenFalse;
        }
        return new PersonaJevQuestion { Type = "noul", Instructions = instructions, Criteria = map };
    }
}

/// <summary>リクエスト本体（JevRequest と同じ JSON）。</summary>
public sealed class PersonaJevRequest
{
    [JsonProperty("model")] public string Model;
    [JsonProperty("state")] public object State;
    [JsonProperty("questions")] public Dictionary<string, PersonaJevQuestion> Questions = new();
}

/// <summary>回答1件（JevAnswer と同じ JSON）。</summary>
public sealed class PersonaJevAnswer
{
    [JsonProperty("type")] public string Type;
    [JsonProperty("noul")] public float? Noul;
    [JsonProperty("choice")] public string Choice;
    [JsonProperty("score")] public float? Score;
    [JsonProperty("confidence")] public float? Confidence;
    [JsonProperty("probabilities")] public Dictionary<string, float> Probabilities;
    [JsonProperty("legend")] public Dictionary<string, string> Legend;

    /// <summary>確信度。noul は confidence を返さないので |noul−0.5|×2 で代用する。</summary>
    public float EffectiveConfidence =>
        Confidence ?? (Noul.HasValue ? Mathf.Clamp01(Mathf.Abs(Noul.Value - 0.5f) * 2f) : 0f);
}

public sealed class PersonaJevUsage
{
    [JsonProperty("input_tokens")] public int InputTokens;
    [JsonProperty("output_tokens")] public int OutputTokens;
}

/// <summary>レスポンス本体（JevResponse と同じ JSON）。</summary>
public sealed class PersonaJevResponse
{
    [JsonProperty("model")] public string Model;
    [JsonProperty("answers")] public Dictionary<string, PersonaJevAnswer> Answers;
    [JsonProperty("usage")] public PersonaJevUsage Usage;

    /// <summary>送ってから返るまでの実時間（ミリ秒。JSON には無い）。</summary>
    [JsonIgnore] public long LatencyMs;
}

/// <summary>
/// プロジェクト直下 UserSettings/PersonaPlay.json の設定（.gitignore 済み）。無ければ既定値で作る。
/// 環境変数は使わない（TYPESAFE_* は本物の Jev 用なので触らない）。
/// </summary>
public sealed class PersonaPlayConfig
{
    public const string RelativePath = "UserSettings/PersonaPlay.json";

    [JsonProperty("baseUrl")] public string BaseUrl = "http://localhost:11434";
    [JsonProperty("path")] public string Path = "/v1/systemone";
    /// <summary>1リクエストの待ち時間の上限（秒）。初回はモデルの読み込みで 10〜40 秒かかる。</summary>
    [JsonProperty("timeoutSec")] public float TimeoutSec = 60f;
    /// <summary>人格 ID → 判断モデル（Ollama のタグ）。</summary>
    [JsonProperty("models")] public Dictionary<string, string> Models = new()
    {
        ["zundamon"] = "kev:4b-q8_0",
        ["metan"] = "kev:9b-q4_K_M",
    };
    /// <summary>state に載せる過去の教訓の数（人格ごとの上限。ずんだもんは人格側でさらに絞る）。</summary>
    [JsonProperty("lessonsInState")] public int LessonsInState = 5;
    /// <summary>
    /// 一言に混ぜるミーム辞書（プロジェクト直下からの相対パスか絶対パス）。空・無い・読めないならミームなし（PersonaMemeLexicon）。
    /// 更新は Meme 側の digest.mjs --export で tools/persona_play/memes/ に書き出してここを差し替える。
    /// </summary>
    [JsonProperty("memeLexiconPath")] public string MemeLexiconPath = "tools/persona_play/memes/memes_2026-10.json";

    [JsonIgnore] public string Endpoint => (BaseUrl ?? "").TrimEnd('/') + "/" + (Path ?? "").TrimStart('/');

    public string ModelFor(string personaId) =>
        Models != null && personaId != null && Models.TryGetValue(personaId, out var m) && !string.IsNullOrEmpty(m) ? m : null;

    /// <summary>プロジェクト（ビルドなら exe の Data フォルダの親）直下。</summary>
    public static string ProjectRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));

    public static PersonaPlayConfig Load()
    {
        string file = System.IO.Path.Combine(ProjectRoot, RelativePath);
        try
        {
            if (File.Exists(file))
            {
                var loaded = JsonConvert.DeserializeObject<PersonaPlayConfig>(File.ReadAllText(file));
                if (loaded != null) return loaded;
            }
            var fresh = new PersonaPlayConfig();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
            File.WriteAllText(file, JsonConvert.SerializeObject(fresh, Formatting.Indented));
            Debug.Log($"[PersonaPlay] 設定ファイルを作りました: {file}");
            return fresh;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PersonaPlay] 設定ファイルを読めないので既定値を使います: {e.Message}");
            return new PersonaPlayConfig();
        }
    }
}

/// <summary>
/// ローカルの Jev 互換エンドポイントへ 1 リクエスト送る。リトライはしない（呼ぶ側が既定の戦略で代替する）。
/// メインスレッドから同期待ちしないこと（await で使う）。
/// </summary>
public static class PersonaJevClient
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly JsonSerializerSettings SerializerSettings = new() { NullValueHandling = NullValueHandling.Ignore };

    private static HttpClient CreateClient()
    {
        // タイムアウトは CancellationToken で人格ごとに掛けるので、ここは長めの上限だけ
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>送るのと同じ JSON（ログ・テスト用）。</summary>
    public static string Serialize(PersonaJevRequest request) => JsonConvert.SerializeObject(request, SerializerSettings);

    public static async Task<PersonaJevResponse> SendAsync(PersonaJevRequest request, PersonaPlayConfig config, CancellationToken ct)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrEmpty(request.Model)) throw new InvalidOperationException("model が空です（UserSettings/PersonaPlay.json の models を確認）");

        string json = Serialize(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Mathf.Max(1f, config.TimeoutSec)));
        var sw = Stopwatch.StartNew();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var response = await Http.SendAsync(message, timeout.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Truncate(body)}");
            var parsed = JsonConvert.DeserializeObject<PersonaJevResponse>(body);
            if (parsed?.Answers == null) throw new InvalidOperationException("answers が空: " + Truncate(body));
            parsed.LatencyMs = sw.ElapsedMilliseconds;
            return parsed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{config.Endpoint} が {config.TimeoutSec:0} 秒で返らなかった");
        }
    }

    private static string Truncate(string text) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= 400 ? text : text.Substring(0, 400) + "…";
}
#endif
