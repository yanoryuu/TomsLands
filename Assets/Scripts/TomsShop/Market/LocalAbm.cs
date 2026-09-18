using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// トレーダーの性格。人数比を変えることで市場の質（バブルが出やすい／回帰しやすい）が変わる。
/// </summary>
public enum TraderArchetype
{
    /// <summary>順張り。直近の値上がりを追いかける。正のフィードバック＝バブルの源。</summary>
    Momentum,

    /// <summary>逆張り。基準価格から下方乖離した銘柄を買う。負のフィードバック＝平均回帰の源。</summary>
    Contrarian,

    /// <summary>ファンダメンタル。需要の増減に反応する。</summary>
    DemandWatcher,

    /// <summary>マーケットメイカー。直近の行き過ぎを打ち消す方向に注文する。</summary>
    MarketMaker,

    /// <summary>ノイズ。気分で売買する。地の揺らぎを作る。</summary>
    Noise,
}

/// <summary>
/// ABM が1銘柄の判断に必要とする最小情報。RuntimeItemData へ直接依存させないための読み取り専用ビュー。
/// </summary>
public interface IMarketView
{
    /// <summary>現在価格。</summary>
    int CurrentPrice { get; }

    /// <summary>マスターデータ上の基準価格。逆張り勢が「安いか高いか」を判断する基準。</summary>
    int BasePrice { get; }

    /// <summary>現在の需要。</summary>
    float Demand { get; }

    /// <summary>前ターンの需要。差分をファンダメンタル勢が見る。</summary>
    float PreviousDemand { get; }

    /// <summary>在庫数。板の厚みに効く。</summary>
    int Stock { get; }

    /// <summary>直近が末尾の価格履歴。要素数は 1 以上を想定するが、0 でも落ちないこと。</summary>
    IReadOnlyList<int> PriceHistory { get; }

    /// <summary>前ターンにこの銘柄で発生した純注文。群衆行動（ハーディング）の入力に使う。</summary>
    float LastNetOrder { get; }
}

/// <summary>
/// ABM のチューニング用パラメータ。キャリブレーションで探索される対象。
/// </summary>
[System.Serializable]
public sealed class LocalAbmSettings
{
    /// <summary>トレーダー総数。</summary>
    public int traderCount = 30;

    /// <summary>各アーキタイプの人数比。要素順は TraderArchetype の定義順（5要素）。</summary>
    public float[] archetypeWeights = { 0.27f, 0.20f, 0.13f, 0.13f, 0.27f };

    /// <summary>資金分布のパレート指数。小さいほど少数の大口に資金が集中し、ファットテールが強くなる。</summary>
    public float capitalParetoAlpha = 1.3f;

    /// <summary>資金の最小値（パレート分布のスケール）。</summary>
    public float capitalMin = 30f;

    /// <summary>資金の上限（外れ値の暴走を防ぐクランプ）。</summary>
    public float capitalMax = 2000f;

    /// <summary>順張りの感度。</summary>
    public float momentumGain = 2f;

    /// <summary>逆張りの感度。</summary>
    public float valueGain = 2f;

    /// <summary>需要変化への感度。</summary>
    public float demandGain = 10f;

    /// <summary>マーケットメイカーの感度。</summary>
    public float marketMakerGain = 5f;

    /// <summary>群衆行動の強さ。前ターンの純注文へ全員が引きずられる度合い。0 で無効。</summary>
    public float herdingGain = 0.15f;

    /// <summary>
    /// 不感帯の上限。各トレーダーは 0〜この値の個人閾値を持ち、
    /// シグナルがそれに届かないターンは市場に参加しない。
    /// 0 で全員が毎ターン参加（＝実効Nが固定され、分布が正規分布へ寄る）。
    /// </summary>
    public float inactionBandMax = 0.85f;

    /// <summary>価格インパクト係数。大きいほどボラが上がる。</summary>
    public float lambda = 0.03f;

    /// <summary>板の厚みの基準値。</summary>
    public float baseDepth = 200f;

    /// <summary>
    /// 同じ値を持つ複製を返す。キャリブレーションで設定を変異させる際、元を壊さないために使う。
    /// </summary>
    public LocalAbmSettings Clone()
    {
        var clone = new LocalAbmSettings
        {
            traderCount = traderCount,
            capitalParetoAlpha = capitalParetoAlpha,
            capitalMin = capitalMin,
            capitalMax = capitalMax,
            momentumGain = momentumGain,
            valueGain = valueGain,
            demandGain = demandGain,
            marketMakerGain = marketMakerGain,
            herdingGain = herdingGain,
            inactionBandMax = inactionBandMax,
            lambda = lambda,
            baseDepth = baseDepth,
        };

        if (archetypeWeights != null)
        {
            clone.archetypeWeights = new float[archetypeWeights.Length];
            Array.Copy(archetypeWeights, clone.archetypeWeights, archetypeWeights.Length);
        }
        else
        {
            clone.archetypeWeights = null;
        }

        return clone;
    }
}

/// <summary>
/// 仮想トレーダー1体。性格・資金・積極度・記憶長だけを持つ軽量オブジェクト。
/// </summary>
public sealed class MarketTrader
{
    /// <summary>このトレーダーの性格。</summary>
    public TraderArchetype Archetype { get; }

    /// <summary>資金力。注文量のスケールになる。</summary>
    public float Capital { get; }

    /// <summary>積極度。0.5〜1.5 を想定。</summary>
    public float Aggressiveness { get; }

    /// <summary>何ターン前までの値動きを見るか（1以上）。</summary>
    public int Memory { get; }

    /// <summary>
    /// このトレーダーが市場に参加するのに必要なシグナルの大きさ（不感帯）。
    /// 0 なら毎ターン必ず参加する。個人差があることで参加人数がターンごとに揺れ、
    /// ボラティリティ・クラスタリングが生まれる。
    /// </summary>
    public float InactionThreshold { get; }

    /// <summary>
    /// トレーダーを1体生成する。値はすべて生成時に確定し、以後不変。
    /// </summary>
    public MarketTrader(TraderArchetype archetype, float capital, float aggressiveness, int memory,
        float inactionThreshold = 0f)
    {
        Archetype = archetype;
        Capital = SanitizeFloat(capital, 0f);
        Aggressiveness = SanitizeFloat(aggressiveness, 1f);
        Memory = memory < 1 ? 1 : memory;
        InactionThreshold = Mathf.Max(0f, SanitizeFloat(inactionThreshold, 0f));
    }

    /// <summary>
    /// この銘柄への注文量を返す。正が買い、負が売り。
    /// </summary>
    public float DecideOrder(IMarketView view, LocalAbmSettings settings, System.Random rng)
    {
        if (view == null || settings == null)
        {
            return 0f;
        }

        float signal;
        switch (Archetype)
        {
            case TraderArchetype.Momentum:
                signal = RecentReturn(view, Memory) * settings.momentumGain;
                break;

            case TraderArchetype.Contrarian:
            {
                float baseP = Mathf.Max(1f, view.BasePrice);
                float gap = (view.CurrentPrice / baseP) - 1f;
                signal = -gap * settings.valueGain;
                break;
            }

            case TraderArchetype.DemandWatcher:
                signal = (view.Demand - view.PreviousDemand) * settings.demandGain;
                break;

            case TraderArchetype.MarketMaker:
                signal = -RecentReturn(view, 1) * settings.marketMakerGain;
                break;

            case TraderArchetype.Noise:
                signal = rng != null ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
                break;

            default:
                signal = 0f;
                break;
        }

        signal = SanitizeFloat(signal, 0f);

        // 不感帯（inaction band）。
        // シグナルが自分の閾値に届かないターンは、そのトレーダーは市場に参加しない。
        // 閾値は個人差があるため「その時アクティブな人数」がターンごとに変動し、
        // 実効的な N が揺れる → ボラティリティ・クラスタリングとファットテールの源になる。
        // 全員が常に参加していると N が固定され、中心極限定理で分布が正規化してしまう。
        if (InactionThreshold > 0f && Mathf.Abs(signal) < InactionThreshold)
        {
            return 0f;
        }

        float order = Capital * Aggressiveness * Mathf.Clamp(signal, -1f, 1f);

        // 群衆行動：ノイズ勢以外は前ターンの純注文に引きずられる。
        if (Archetype != TraderArchetype.Noise && settings.herdingGain != 0f)
        {
            float herdInput = Mathf.Clamp(SanitizeFloat(view.LastNetOrder, 0f) / 1000f, -1f, 1f);
            order += Capital * Aggressiveness * herdInput * settings.herdingGain;
        }

        return SanitizeFloat(order, 0f);
    }

    /// <summary>
    /// 直近リターン。価格履歴の末尾（最新）と、そこから memory 本さかのぼった価格を比較する。
    /// 履歴が足りない場合や過去価格が 0 以下の場合は 0 を返す。
    /// </summary>
    private static float RecentReturn(IMarketView view, int memory)
    {
        var history = view.PriceHistory;
        if (history == null)
        {
            return 0f;
        }

        if (memory < 1)
        {
            memory = 1;
        }

        int count = history.Count;
        int pastIndex = count - 1 - memory;
        if (count < 2 || pastIndex < 0)
        {
            return 0f;
        }

        float past = history[pastIndex];
        if (past <= 0f)
        {
            return 0f;
        }

        float last = history[count - 1];
        return SanitizeFloat((last / past) - 1f, 0f);
    }

    /// <summary>NaN / Infinity をフォールバック値へ置き換える。</summary>
    private static float SanitizeFloat(float value, float fallback)
    {
        return (float.IsNaN(value) || float.IsInfinity(value)) ? fallback : value;
    }
}

/// <summary>
/// トレーダー群を保持し、1ターン分の価格変動率を算出する市場。
/// 乱数はコンストラクタで与えたシードから作る System.Random のみを使うため、
/// 同じシード・同じ設定・同じ入力なら常に同じ結果になる。
/// </summary>
public sealed class LocalAbmMarket
{
    private readonly System.Random _rng;
    private readonly List<MarketTrader> _traders;

    /// <summary>この市場のパラメータ。</summary>
    public LocalAbmSettings Settings { get; }

    /// <summary>生成済みトレーダー群。</summary>
    public IReadOnlyList<MarketTrader> Traders => _traders;

    /// <summary>直近の Step で算出した純注文（銘柄別ではなく最後に評価したもの）。デバッグ表示用。</summary>
    public float LastNetOrder { get; private set; }

    /// <summary>
    /// 指定シードでトレーダー群を生成する。同じシード・同じ設定なら常に同じ構成になる。
    /// </summary>
    public LocalAbmMarket(LocalAbmSettings settings, int seed)
    {
        Settings = settings ?? new LocalAbmSettings();
        _rng = new System.Random(seed);
        _traders = new List<MarketTrader>();

        int archetypeCount = Enum.GetValues(typeof(TraderArchetype)).Length;
        int traderCount = Settings.traderCount > 0 ? Settings.traderCount : 1;

        int[] counts = BuildArchetypeCounts(Settings.archetypeWeights, archetypeCount, traderCount);

        float alpha = Settings.capitalParetoAlpha > 0.0001f ? Settings.capitalParetoAlpha : 1f;
        float capitalMin = Settings.capitalMin > 0f ? Settings.capitalMin : 1f;
        float capitalMax = Mathf.Max(capitalMin, Settings.capitalMax);

        for (int i = 0; i < archetypeCount; i++)
        {
            for (int n = 0; n < counts[i]; n++)
            {
                // u は (0, 1]。NextDouble() は [0, 1) なので 1 - x で 0 を除外する。
                float u = (float)(1.0 - _rng.NextDouble());
                if (u <= 0f)
                {
                    u = 1e-6f;
                }

                float capital = capitalMin / Mathf.Pow(u, 1f / alpha);
                if (float.IsNaN(capital) || float.IsInfinity(capital))
                {
                    capital = capitalMin;
                }
                capital = Mathf.Clamp(capital, capitalMin, capitalMax);

                float aggressiveness = 0.5f + (float)_rng.NextDouble();
                int memory = _rng.Next(1, 5);

                // 不感帯は 0〜inactionBandMax の一様乱数。個人差が参加人数の揺らぎを生む。
                float threshold = (float)_rng.NextDouble() * Mathf.Max(0f, Settings.inactionBandMax);

                _traders.Add(new MarketTrader((TraderArchetype)i, capital, aggressiveness, memory, threshold));
            }
        }
    }

    /// <summary>
    /// 1銘柄・1ターン分を進め、価格変動率（1.0 = 据え置き）を返す。
    /// 内部で全トレーダーの注文を合計し、OrderFlowPriceEngine.ToPriceRate へ渡す。
    /// </summary>
    public float Step(IMarketView view)
    {
        if (view == null)
        {
            return 1f;
        }

        float netOrder = 0f;
        for (int i = 0; i < _traders.Count; i++)
        {
            netOrder += _traders[i].DecideOrder(view, Settings, _rng);
        }

        if (float.IsNaN(netOrder) || float.IsInfinity(netOrder))
        {
            netOrder = 0f;
        }

        // √N 正規化。
        // 独立に近い N 人の注文の合計は標準偏差が √N に比例して増えるため、
        // 素の合計をそのまま使うと「人数を増やす＝ボラが上がる」だけの比較になってしまう。
        // √N で割るとボラ水準が人数に依存しなくなり、λ が人数と独立なパラメータになる。
        // これにより「人数を変えたとき分布の"形"（尖度）がどう変わるか」だけを取り出せる。
        netOrder /= Mathf.Sqrt(Mathf.Max(1, _traders.Count));

        float depth = OrderFlowPriceEngine.Depth(view.Stock, view.Demand, Settings.baseDepth);
        float rate = OrderFlowPriceEngine.ToPriceRate(netOrder, depth, Settings.lambda);

        LastNetOrder = netOrder;

        if (float.IsNaN(rate) || float.IsInfinity(rate))
        {
            return 1f;
        }

        return rate;
    }

    /// <summary>
    /// 重み配列からアーキタイプごとの人数を決める。合計は必ず traderCount に一致させる。
    /// 重みが null・長さ不一致・合計が 0 以下なら均等割りにフォールバックする。
    /// </summary>
    private static int[] BuildArchetypeCounts(float[] weights, int archetypeCount, int traderCount)
    {
        var counts = new int[archetypeCount];

        bool usable = weights != null && weights.Length == archetypeCount;
        float total = 0f;
        if (usable)
        {
            for (int i = 0; i < archetypeCount; i++)
            {
                float w = weights[i];
                if (float.IsNaN(w) || float.IsInfinity(w) || w < 0f)
                {
                    w = 0f;
                }
                total += w;
            }

            if (total <= 0f)
            {
                usable = false;
            }
        }

        int assigned = 0;
        for (int i = 0; i < archetypeCount - 1; i++)
        {
            float ratio = usable ? Mathf.Max(0f, weights[i]) / total : 1f / archetypeCount;
            int c = Mathf.FloorToInt(traderCount * ratio);
            if (c < 0)
            {
                c = 0;
            }
            if (assigned + c > traderCount)
            {
                c = traderCount - assigned;
            }
            counts[i] = c;
            assigned += c;
        }

        // 丸め誤差の余りは最後のアーキタイプで吸収し、合計を traderCount に揃える。
        counts[archetypeCount - 1] = traderCount - assigned;
        return counts;
    }
}
