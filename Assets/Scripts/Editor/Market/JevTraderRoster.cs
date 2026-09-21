using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

// =====================================================================
// Jev（TypeSafe System One Model）に「仮想トレーダー」を演じさせるための定義層。
//
// 設計の要点:
//   * 1リクエスト = state（全銘柄の板情報）+ トレーダー N 人 × 2問。
//     Jev は同じ state に対して全質問を並列評価するため、人数を増やしても
//     レイテンシはほぼ変わらず、state のトークン代も1回分で済む。
//   * pick  = Choice（銘柄選択）。返ってくる確率分布を「その人の資金配分」として使う。
//     argmax だけを使うと値動きが階段状になるので、必ず分布を使うこと。
//   * stance = Score（5段階）。-1〜+1 に正規化して「買い越し / 売り越し」の符号と強さにする。
//   * confidence を注文量に掛けることで、市場が迷っている局面は自然にボラが下がる。
//
// 言語について:
//   TypeSafe の公式ドキュメントは「英語が主言語、他言語は精度が落ちる」と明記している。
//   そのため state のフィールド名・instructions・criteria はすべて英語で書く。
//   ゲーム内の日本語表示とは無関係（ここは開発時のシミュレーション専用）。
// =====================================================================

/// <summary>
/// Jev に演じさせるトレーダー1人分の定義。
/// </summary>
public sealed class JevTraderPersona
{
    /// <summary>質問IDの前置詞に使う一意なID（例: "t07"）。</summary>
    public string Id;

    /// <summary>性格。ローカルABM側の <see cref="TraderArchetype"/> と対応させる。</summary>
    public TraderArchetype Archetype;

    /// <summary>資金量。注文量の倍率になる。パレート分布で偏らせる。</summary>
    public float Capital;

    /// <summary>銘柄選択（Choice）の指示文。英語。</summary>
    public string PickInstructions;

    /// <summary>ポジションの強さ（Score）の指示文。英語。</summary>
    public string StanceInstructions;

    /// <summary>Choice 質問のID。</summary>
    public string PickQuestionId => Id + "_pick";

    /// <summary>Score 質問のID。</summary>
    public string StanceQuestionId => Id + "_stance";
}

/// <summary>
/// Jev へ渡す銘柄1件分のスナップショット。フィールド名は英語（モデルの主言語）。
/// </summary>
public sealed class JevMarketItem
{
    [JsonProperty("id")] public string Id;
    [JsonProperty("name")] public string Name;
    [JsonProperty("category")] public string Category;
    [JsonProperty("element")] public string Element;
    [JsonProperty("price")] public int Price;
    [JsonProperty("base_price")] public int BasePrice;
    [JsonProperty("recent_prices")] public List<int> RecentPrices;
    [JsonProperty("demand")] public float Demand;
    [JsonProperty("demand_change")] public float DemandChange;
    [JsonProperty("displayed")] public bool Displayed;
    [JsonProperty("stock")] public int Stock;
}

/// <summary>
/// Jev へ渡す市場全体のスナップショット（= state）。
/// </summary>
public sealed class JevMarketState
{
    [JsonProperty("turn")] public int Turn;
    [JsonProperty("market")] public List<JevMarketItem> Market = new List<JevMarketItem>();

    /// <summary>直近に起きた出来事。値動きへ意味的な文脈を与える。</summary>
    [JsonProperty("events")] public List<string> Events = new List<string>();
}

/// <summary>
/// トレーダー群の生成と、Jev リクエストの組み立て／レスポンスの注文フローへの変換。
/// </summary>
public static class JevTraderRoster
{
    /// <summary>
    /// stance（Score）のレベル定義。抽象語ではなく具体的な状況を書くこと、という
    /// 公式ガイダンスに従い、各レベルが単独で意味を成すように書いている。
    /// </summary>
    public static readonly string[] StanceLevels =
    {
        "Liquidating: selling out of every position and stepping away from this market entirely.",
        "Net seller: trimming positions and reducing exposure this turn.",
        "Flat: holding what is already owned and putting no new money to work.",
        "Net buyer: adding to positions with part of the available capital.",
        "All-in: committing the full remaining capital to the market this turn.",
    };

    /// <summary>
    /// 既定のトレーダー構成を生成する。
    /// 人数比はローカルABMの既定値と揃えてあるので、両者の結果を直接比較できる。
    /// 資金はパレート分布で偏らせる（少数の大口がジャンプを生む＝ファットテールの主因）。
    /// </summary>
    /// <param name="traderCount">生成する人数。20〜40 が推奨レンジ。</param>
    /// <param name="seed">シード。同じシードなら常に同じ構成になる。</param>
    /// <param name="paretoAlpha">パレート指数。小さいほど資金が大口へ集中する。</param>
    /// <param name="capitalMin">資金の最小値。</param>
    /// <param name="capitalMax">資金の上限（外れ値クランプ）。</param>
    /// <summary>
    /// 既定のペルソナ比率。要素順は <see cref="TraderArchetype"/> の定義順。
    /// ローカルABM の archetypeWeights 既定値と揃えてある。
    /// </summary>
    public static readonly float[] DefaultArchetypeWeights = { 0.27f, 0.20f, 0.13f, 0.13f, 0.27f };

    public static List<JevTraderPersona> CreateDefault(
        int traderCount = 30, int seed = 12345,
        float paretoAlpha = 1.3f, float capitalMin = 30f, float capitalMax = 2000f,
        float[] archetypeWeights = null)
    {
        traderCount = Mathf.Max(1, traderCount);
        var rng = new System.Random(seed);

        var archetypes = BuildArchetypeSequence(archetypeWeights, traderCount);

        var list = new List<JevTraderPersona>(traderCount);
        for (int i = 0; i < traderCount; i++)
        {
            var archetype = archetypes[i];

            // パレート分布: capital = min / u^(1/alpha)
            double u = rng.NextDouble();
            if (u <= 0.0) u = 1e-6;
            float capital = Mathf.Clamp(
                capitalMin / Mathf.Pow((float)u, 1f / Mathf.Max(0.1f, paretoAlpha)),
                capitalMin, capitalMax);

            list.Add(new JevTraderPersona
            {
                Id = "t" + i.ToString("D2"),
                Archetype = archetype,
                Capital = capital,
                PickInstructions = PickInstructionsFor(archetype),
                StanceInstructions = StanceInstructionsFor(archetype),
            });
        }
        return list;
    }

    /// <summary>
    /// 人数比から、各トレーダーへ割り当てる性格の並びを作る。
    /// 丸め誤差は最後の性格で吸収し、必ず traderCount 人ちょうどになるようにする。
    /// weights が null / 長さ不一致 / 合計 0 以下なら既定比率へフォールバックする。
    /// </summary>
    private static TraderArchetype[] BuildArchetypeSequence(float[] weights, int traderCount)
    {
        int kinds = System.Enum.GetValues(typeof(TraderArchetype)).Length;

        float total = 0f;
        if (weights != null && weights.Length == kinds)
        {
            foreach (var w in weights) total += Mathf.Max(0f, w);
        }
        if (total <= 0f)
        {
            weights = DefaultArchetypeWeights;
            total = 0f;
            foreach (var w in weights) total += w;
        }

        // 丸め誤差の余りは Noise が吸収する。
        // 末尾に吸わせると、列挙子を追加したときに吸収先が移って挙動が変わる。
        // Noise に固定して LocalAbm 側と揃える。
        const int absorber = (int)TraderArchetype.Noise;
        var counts = new int[kinds];
        int assigned = 0;
        for (int i = 0; i < kinds; i++)
        {
            if (i == absorber) continue;
            counts[i] = Mathf.FloorToInt(traderCount * Mathf.Max(0f, weights[i]) / total);
            assigned += counts[i];
        }
        counts[absorber] = Mathf.Max(0, traderCount - assigned);

        var sequence = new TraderArchetype[traderCount];
        int index = 0;
        for (int i = 0; i < kinds; i++)
        {
            for (int n = 0; n < counts[i] && index < traderCount; n++)
            {
                sequence[index++] = (TraderArchetype)i;
            }
        }
        // 端数で埋まらなかった分（理論上発生しないが保険）
        while (index < traderCount) sequence[index++] = TraderArchetype.Noise;

        return sequence;
    }

    private const string PickSuffix =
        " Looking at `market`, which single item is your highest-conviction position this turn? " +
        "Answer with the item you would trade most heavily, whether you are buying it or selling it.";

    private static string PickInstructionsFor(TraderArchetype archetype)
    {
        switch (archetype)
        {
            case TraderArchetype.Momentum:
                return "You are a short-term momentum trader in a marketplace for fantasy adventuring gear. " +
                       "You chase recent price acceleration in `recent_prices` and you deliberately ignore " +
                       "whether an item looks expensive against its `base_price`." + PickSuffix;
            case TraderArchetype.Contrarian:
                return "You are a contrarian value investor in a marketplace for fantasy adventuring gear. " +
                       "You buy items trading far below their `base_price` and you avoid items that have " +
                       "just spiked in `recent_prices`." + PickSuffix;
            case TraderArchetype.DemandWatcher:
                return "You are a fundamental analyst in a marketplace for fantasy adventuring gear. " +
                       "You track customer `demand`, whether `demand_change` is positive, and whether the " +
                       "item is `displayed` on the shop floor." + PickSuffix;
            case TraderArchetype.MarketMaker:
                return "You are a market maker in a marketplace for fantasy adventuring gear. " +
                       "You profit from providing liquidity and from mean reversion: you lean against the " +
                       "most extreme recent move, buying what has fallen hardest and selling what has " +
                       "risen hardest." + PickSuffix;
            default:
                return "You are an inexperienced retail trader in a marketplace for fantasy adventuring gear. " +
                       "You go for whatever looks exciting or famous and you pay little attention to " +
                       "`recent_prices` or `base_price`." + PickSuffix;
        }
    }

    private const string StanceSuffix =
        " Considering the whole market in `market` and anything reported in `events`, " +
        "how aggressive is your net position this turn?";

    private static string StanceInstructionsFor(TraderArchetype archetype)
    {
        switch (archetype)
        {
            case TraderArchetype.Momentum:
                return "You are a short-term momentum trader who scales in hard while prices are rising " +
                       "and cuts fast when the trend breaks." + StanceSuffix;
            case TraderArchetype.Contrarian:
                return "You are a contrarian value investor who buys weakness and sells strength, and who " +
                       "sits out when prices sit near their `base_price`." + StanceSuffix;
            case TraderArchetype.DemandWatcher:
                return "You are a fundamental analyst who commits capital only when customer demand is " +
                       "genuinely expanding." + StanceSuffix;
            case TraderArchetype.MarketMaker:
                return "You are a market maker who stays close to flat overall and only leans against " +
                       "extreme dislocations." + StanceSuffix;
            default:
                return "You are an inexperienced retail trader whose conviction swings with the mood of " +
                       "the market." + StanceSuffix;
        }
    }

    /// <summary>
    /// 銘柄リストから Choice の criteria（選択肢キー → 説明）を作る。
    /// 数値は state 側に置き、ここでは銘柄の同定に必要な最小限だけを書く。
    /// </summary>
    public static Dictionary<string, string> BuildChoiceCriteria(IReadOnlyList<JevMarketItem> items)
    {
        var criteria = new Dictionary<string, string>(items.Count);
        foreach (var item in items)
        {
            // Choice の選択肢は最大255。銘柄数がそれを超える運用は想定しない。
            criteria[item.Id] = $"{item.Element}-element {item.Category} \"{item.Name}\"";
        }
        return criteria;
    }

    /// <summary>
    /// state とトレーダー群から、1ターン分の Jev リクエストを組み立てる。
    /// 全質問が同じ state に対して並列評価される。
    /// </summary>
    public static JevRequest BuildRequest(JevMarketState state, IReadOnlyList<JevTraderPersona> personas)
    {
        var criteria = BuildChoiceCriteria(state.Market);
        var request = new JevRequest { State = state };

        foreach (var persona in personas)
        {
            request.Questions[persona.PickQuestionId] =
                JevQuestion.Choice(persona.PickInstructions, criteria);
            request.Questions[persona.StanceQuestionId] =
                JevQuestion.Score(persona.StanceInstructions, StanceLevels);
        }
        return request;
    }

    /// <summary>
    /// レスポンスを銘柄別の純注文へ変換する。正が買い越し、負が売り越し。
    ///
    ///   netOrder[j] = Σ_i  capital_i × stance_i × confidence_i × P_i(j)
    ///
    /// P_i(j) は Choice の確率分布。argmax ではなく分布全体を使うことで、
    /// 30人でも滑らかで連続的な注文フローになる。
    /// </summary>
    /// <param name="response">Jev のレスポンス。</param>
    /// <param name="personas">リクエストに使ったトレーダー群。</param>
    /// <param name="itemIds">銘柄ID一覧。結果の辞書はこのキーで初期化される。</param>
    public static Dictionary<string, float> ToNetOrders(
        JevResponse response, IReadOnlyList<JevTraderPersona> personas, IReadOnlyList<string> itemIds)
    {
        var netOrders = new Dictionary<string, float>(itemIds.Count);
        foreach (var id in itemIds) netOrders[id] = 0f;

        if (response?.Answers == null) return netOrders;

        foreach (var persona in personas)
        {
            if (!response.Answers.TryGetValue(persona.PickQuestionId, out var pick)) continue;
            if (!response.Answers.TryGetValue(persona.StanceQuestionId, out var stance)) continue;

            float direction = stance.NormalizedScore();             // -1 〜 +1
            float conviction = stance.Confidence ?? 1f;             // 分布の尖り具合
            float size = persona.Capital * direction * conviction;
            if (Mathf.Approximately(size, 0f)) continue;

            if (pick.Probabilities != null && pick.Probabilities.Count > 0)
            {
                // 分布をそのまま資金配分として使う（推奨経路）
                foreach (var kv in pick.Probabilities)
                {
                    if (netOrders.ContainsKey(kv.Key)) netOrders[kv.Key] += size * kv.Value;
                }
            }
            else if (!string.IsNullOrEmpty(pick.Choice) && netOrders.ContainsKey(pick.Choice))
            {
                // 保険: 確率分布が無い場合のみ argmax へ全額
                netOrders[pick.Choice] += size;
            }
        }
        return netOrders;
    }
}
