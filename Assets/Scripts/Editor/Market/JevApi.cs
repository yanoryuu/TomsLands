using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

/// <summary>
/// Jev（TypeSafe AI System One Model）へ渡す質問1件分の定義。
/// type は "noul" / "choice" / "score" のいずれか。
/// </summary>
public sealed class JevQuestion
{
    /// <summary>質問の型。"noul" / "choice" / "score"。</summary>
    [JsonProperty("type")]
    public string Type;

    /// <summary>質問文。文字列のほか任意のJSON構造を渡せる。</summary>
    [JsonProperty("instructions")]
    public object Instructions;

    /// <summary>
    /// 判定基準。型ごとに形式が異なる。
    /// choice はキー→説明のマップ、score は説明の配列、noul は true/false の説明マップ（省略可）。
    /// </summary>
    [JsonProperty("criteria", NullValueHandling = NullValueHandling.Ignore)]
    public object Criteria;

    /// <summary>
    /// 選択式（choice）の質問を作る。options は「選択肢キー → 説明」のマップ（必須・最大255要素）。
    /// </summary>
    public static JevQuestion Choice(string instructions, IDictionary<string, string> options)
    {
        if (options == null || options.Count == 0)
        {
            throw new ArgumentException("choice 質問には選択肢を1件以上指定してください。", nameof(options));
        }

        if (options.Count > 255)
        {
            throw new ArgumentException("choice 質問の選択肢は最大255件までです。", nameof(options));
        }

        var criteria = new Dictionary<string, string>(options.Count);
        foreach (var pair in options)
        {
            criteria[pair.Key] = pair.Value;
        }

        return new JevQuestion
        {
            Type = "choice",
            Instructions = instructions,
            Criteria = criteria,
        };
    }

    /// <summary>
    /// 段階評価（score）の質問を作る。levels はレベル0から順に並べた説明の配列（必須・2〜10要素）。
    /// </summary>
    public static JevQuestion Score(string instructions, IList<string> levels)
    {
        if (levels == null || levels.Count < 2 || levels.Count > 10)
        {
            throw new ArgumentException("score 質問のレベル説明は2〜10件で指定してください。", nameof(levels));
        }

        var criteria = new List<string>(levels.Count);
        for (int i = 0; i < levels.Count; i++)
        {
            criteria.Add(levels[i]);
        }

        return new JevQuestion
        {
            Type = "score",
            Instructions = instructions,
            Criteria = criteria,
        };
    }

    /// <summary>
    /// 真偽度（noul）の質問を作る。whenTrue / whenFalse は任意で、両方 null なら criteria を送らない。
    /// </summary>
    public static JevQuestion Noul(string instructions, string whenTrue = null, string whenFalse = null)
    {
        object criteria = null;
        if (whenTrue != null || whenFalse != null)
        {
            var map = new Dictionary<string, string>(2);
            if (whenTrue != null)
            {
                map["true"] = whenTrue;
            }

            if (whenFalse != null)
            {
                map["false"] = whenFalse;
            }

            criteria = map;
        }

        return new JevQuestion
        {
            Type = "noul",
            Instructions = instructions,
            Criteria = criteria,
        };
    }
}

/// <summary>
/// Jev へ送るリクエスト本体。state に評価対象の状態、questions に質問群を詰める。
/// </summary>
public sealed class JevRequest
{
    /// <summary>使用モデル名。既定は <see cref="JevApi.DefaultModel"/>。</summary>
    [JsonProperty("model")]
    public string Model = JevApi.DefaultModel;

    /// <summary>評価対象の状態。任意のJSON（オブジェクト・文字列・配列）を渡せる。</summary>
    [JsonProperty("state")]
    public object State;

    /// <summary>質問ID → 質問定義。</summary>
    [JsonProperty("questions")]
    public Dictionary<string, JevQuestion> Questions = new Dictionary<string, JevQuestion>();
}

/// <summary>
/// 質問1件に対する回答。質問の型によって存在しないフィールドがあるため nullable で受ける。
/// </summary>
public sealed class JevAnswer
{
    /// <summary>回答の型。"noul" / "choice" / "score"。</summary>
    [JsonProperty("type")]
    public string Type;

    /// <summary>真偽度（0〜1）。noul 以外では null のことがある。</summary>
    [JsonProperty("noul")]
    public float? Noul;

    /// <summary>選ばれた選択肢のキー。choice 以外では null。</summary>
    [JsonProperty("choice")]
    public string Choice;

    /// <summary>スコア（レベル基準の連続値）。score 以外では null。</summary>
    [JsonProperty("score")]
    public float? Score;

    /// <summary>確信度（0〜1）。noul では返らない。</summary>
    [JsonProperty("confidence")]
    public float? Confidence;

    /// <summary>選択肢・レベルごとの確率分布。noul では返らない。</summary>
    [JsonProperty("probabilities")]
    public Dictionary<string, float> Probabilities;

    /// <summary>レベル番号 → ラベル。score でのみ返る。</summary>
    [JsonProperty("legend")]
    public Dictionary<string, string> Legend;

    /// <summary>
    /// score を -1（最低レベル）〜 +1（最高レベル）へ正規化して返す。
    /// レベル数は Legend の要素数、無ければ Probabilities の要素数、それも無ければ 2 とする。
    /// Score が null の場合は 0 を返し、結果は -1〜+1 にクランプする。
    /// </summary>
    public float NormalizedScore()
    {
        if (!Score.HasValue)
        {
            return 0f;
        }

        int levelCount = 2;
        if (Legend != null && Legend.Count > 0)
        {
            levelCount = Legend.Count;
        }
        else if (Probabilities != null && Probabilities.Count > 0)
        {
            levelCount = Probabilities.Count;
        }

        int maxLevel = levelCount - 1;
        if (maxLevel <= 0)
        {
            return 0f;
        }

        float normalized = (Score.Value / maxLevel) * 2f - 1f;
        if (normalized < -1f)
        {
            return -1f;
        }

        if (normalized > 1f)
        {
            return 1f;
        }

        return normalized;
    }
}

/// <summary>
/// トークン使用量。課金対象は入力トークンのみで、出力トークンは無料。
/// </summary>
public sealed class JevUsage
{
    /// <summary>入力トークン数（課金対象）。</summary>
    [JsonProperty("input_tokens")]
    public int InputTokens;

    /// <summary>出力トークン数（無料）。</summary>
    [JsonProperty("output_tokens")]
    public int OutputTokens;
}

/// <summary>
/// Jev からのレスポンス本体。
/// </summary>
public sealed class JevResponse
{
    /// <summary>実際に使われたモデル名（例: "jev-1.13.0"）。</summary>
    [JsonProperty("model")]
    public string Model;

    /// <summary>質問ID → 回答。</summary>
    [JsonProperty("answers")]
    public Dictionary<string, JevAnswer> Answers;

    /// <summary>トークン使用量。</summary>
    [JsonProperty("usage")]
    public JevUsage Usage;
}

/// <summary>
/// TypeSafe AI "Jev"（System One Model）を叩く最小 HTTP クライアント。
/// 公式 C# SDK が存在しないため生 HTTP + Newtonsoft.Json を使う。Editor 専用でゲームビルドには含まれない。
/// </summary>
public static class JevApi
{
    /// <summary>API エンドポイント。</summary>
    public const string Endpoint = "https://api.typesafe.ai/v1/systemone";

    /// <summary>既定のモデル名。</summary>
    public const string DefaultModel = "jev-latest";

    /// <summary>入力トークン 100万 あたりの価格（USD）。出力トークンは無料。</summary>
    public const double InputPricePerMillionUsd = 0.042;

    /// <summary>APIキーを保持する環境変数名。</summary>
    private const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";

    /// <summary>最大リトライ回数（初回送信を含めた総試行回数）。</summary>
    private const int MaxAttempts = 5;

    /// <summary>例外メッセージに含めるレスポンス本文の最大文字数。</summary>
    private const int MaxBodyLengthInMessage = 600;

    /// <summary>プロセス全体で使い回す HttpClient（ソケット枯渇を避けるため毎回 new しない）。</summary>
    private static readonly HttpClient Http = CreateClient();

    /// <summary>送信時のシリアライズ設定。null のフィールドは送らない。</summary>
    private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// 環境変数 TYPESAFE_API_KEY からキーを取得する。未設定なら null。
    /// ※ファイルからの読み込みフォールバックは意図的に実装しない（漏洩経路を作らないため）。
    /// </summary>
    public static string LoadApiKey()
    {
        var key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.Trim();
    }

    /// <summary>APIキーが利用可能かどうか。</summary>
    public static bool HasApiKey
    {
        get { return LoadApiKey() != null; }
    }

    /// <summary>
    /// 入力トークン数から概算コスト（USD）を返す。出力トークンは無料。
    /// </summary>
    public static double EstimateCostUsd(long inputTokens)
    {
        if (inputTokens <= 0)
        {
            return 0.0;
        }

        return inputTokens / 1_000_000.0 * InputPricePerMillionUsd;
    }

    /// <summary>
    /// 1リクエスト送信する。429 / 529 / 5xx は指数バックオフで最大5回までリトライする。
    /// タイムアウト（キャンセル要求なしの TaskCanceledException）もリトライ対象。
    /// </summary>
    public static async Task<JevResponse> SendAsync(JevRequest request, CancellationToken ct = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var apiKey = LoadApiKey();
        if (apiKey == null)
        {
            throw new InvalidOperationException(
                "Jev API キーが見つかりません。環境変数 TYPESAFE_API_KEY を設定してください。");
        }

        if (string.IsNullOrEmpty(request.Model))
        {
            request.Model = DefaultModel;
        }

        var json = JsonConvert.SerializeObject(request, SerializerSettings);

        Exception lastError = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (attempt > 0)
            {
                var delayMs = (int)(Math.Pow(2, attempt) * 250);
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }

            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Post, Endpoint))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    message.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    using (var response = await Http.SendAsync(message, ct).ConfigureAwait(false))
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (response.IsSuccessStatusCode)
                        {
                            var parsed = JsonConvert.DeserializeObject<JevResponse>(body);
                            if (parsed == null)
                            {
                                throw new InvalidOperationException(
                                    "Jev のレスポンスを解釈できませんでした: " + Truncate(body));
                            }

                            return parsed;
                        }

                        var status = (int)response.StatusCode;
                        if (IsRetryableStatus(status) && attempt < MaxAttempts - 1)
                        {
                            lastError = new InvalidOperationException(BuildErrorMessage(status, body));
                            continue;
                        }

                        throw new InvalidOperationException(BuildErrorMessage(status, body));
                    }
                }
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient のタイムアウト。リトライ対象として扱う。
                lastError = new TimeoutException("Jev へのリクエストがタイムアウトしました（60秒）。");
                if (attempt >= MaxAttempts - 1)
                {
                    throw lastError;
                }
            }
            catch (HttpRequestException ex)
            {
                // 接続失敗・DNS 失敗など。リトライ対象。
                lastError = ex;
                if (attempt >= MaxAttempts - 1)
                {
                    throw new InvalidOperationException(
                        "Jev への通信に失敗しました: " + Truncate(ex.Message), ex);
                }
            }
        }

        if (lastError != null)
        {
            throw new InvalidOperationException(
                "Jev へのリクエストが規定回数リトライしても成功しませんでした: " + Truncate(lastError.Message),
                lastError);
        }

        throw new InvalidOperationException("Jev へのリクエストが規定回数リトライしても成功しませんでした。");
    }

    /// <summary>リトライすべき HTTP ステータスかどうかを判定する（429 / 529 / 5xx）。</summary>
    private static bool IsRetryableStatus(int status)
    {
        if (status == (int)HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        if (status == 529)
        {
            return true;
        }

        return status >= 500 && status <= 599;
    }

    /// <summary>ステータスコードに応じた日本語のエラーメッセージを組み立てる。本文は切り詰める。</summary>
    private static string BuildErrorMessage(int status, string body)
    {
        string reason;
        switch (status)
        {
            case 401:
                reason = "APIキーが無効です（環境変数 TYPESAFE_API_KEY を確認してください）";
                break;
            case 422:
                reason = "リクエストの検証エラーです";
                break;
            case 429:
                reason = "レート制限に達しました";
                break;
            case 529:
                reason = "サービスが過負荷状態です";
                break;
            default:
                reason = status >= 500 ? "サーバーエラーです" : "リクエストが失敗しました";
                break;
        }

        return "Jev API エラー (HTTP " + status + "): " + reason + " / " + Truncate(body);
    }

    /// <summary>例外メッセージ用に文字列を規定長で切り詰める。</summary>
    private static string Truncate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= MaxBodyLengthInMessage)
        {
            return text;
        }

        return text.Substring(0, MaxBodyLengthInMessage) + "…(以下省略)";
    }
}
