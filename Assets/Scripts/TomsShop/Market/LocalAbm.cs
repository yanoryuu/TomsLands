using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// ローカル・エージェントベースモデル（ABM）v2 — 価格変動の「相場層」。
//
// 設計: Docs/Market_Price_v2_Design.md
//
//   価格 = 適正値（需要から決まる・決定論）× 相場の揺らぎ（このファイル）
//
// この層は「チャートの質感」だけを担う。ゲームのルール（需要が高いと高い）は
// 適正値（ShopFairValue）が担い、ここでは逆張り勢が適正値へ引き寄せるだけ。
// 外部APIは使わない。同じシード・同じ入力なら同じ結果になる。
// =====================================================================

/// <summary>
/// トレーダーの性格。人数比を変えることで市場の質（バブルが出やすい／回帰しやすい）が変わる。
/// </summary>
public enum TraderArchetype
{
    /// <summary>順張り。直近の値上がりを追いかける。正のフィードバック＝バブルの源。</summary>
    Momentum,

    /// <summary>逆張り。適正値から乖離した銘柄を戻す方向に売買する。平均回帰・張り付き防止の源。</summary>
    Contrarian,

    /// <summary>ファンダメンタル。適正値の変化（＝需要の変化）を先回りして売買する。</summary>
    DemandWatcher,

    /// <summary>マーケットメイカー。直近1ターンの行き過ぎを打ち消す。短期ノイズの抑制。</summary>
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

    /// <summary>マスターデータ上の基準価格。</summary>
    int BasePrice { get; }

    /// <summary>
    /// 適正値（層1）。需要から決まる「この銘柄が本来あるべき価格」。
    /// 逆張り勢はここへ引き寄せる。<see cref="ShopFairValue.Compute"/> で求める。
    /// </summary>
    float FairValue { get; }

    /// <summary>前ターンの適正値。差分をファンダメンタル勢が見る。</summary>
    float PreviousFairValue { get; }

    /// <summary>現在の需要（0〜1）。板の厚みに効く。</summary>
    float Demand { get; }

    /// <summary>在庫数。既定では板の厚みに関与しない。</summary>
    int Stock { get; }

    /// <summary>直近が末尾の価格履歴。要素数は 1 以上を想定するが、0 でも落ちないこと。</summary>
    IReadOnlyList<int> PriceHistory { get; }

    /// <summary>前ターンにこの銘柄で発生した純注文。群衆行動（ハーディング）の入力に使う。</summary>
    float LastNetOrder { get; }
}

/// <summary>
/// ABM のチューニング用パラメータ。層2（相場）のみ。層1（適正値）は ShopEconomySettings 側。
/// キャリブレーションで探索される対象。
/// </summary>
[System.Serializable]
public sealed class LocalAbmSettings
{
    /// <summary>トレーダー総数。20〜40 が推奨。増やしても質は上がらない（中心極限定理）。</summary>
    public int traderCount = 30;

    /// <summary>
    /// 各アーキタイプの人数比。要素順は TraderArchetype の定義順（5要素）。
    /// 長さが違うセーブを渡した場合、足りない分は 0、余った分は無視する。
    /// </summary>
    public float[] archetypeWeights = { 0.27f, 0.20f, 0.13f, 0.13f, 0.27f };

    /// <summary>資金分布のパレート指数。小さいほど少数の大口に資金が集中する。</summary>
    public float capitalParetoAlpha = 1.3f;

    /// <summary>資金の最小値（パレート分布のスケール）。</summary>
    public float capitalMin = 30f;

    /// <summary>資金の上限（外れ値の暴走を防ぐクランプ）。</summary>
    public float capitalMax = 2000f;

    /// <summary>
    /// ノイズ勢の資金上限（capitalMin に対する倍率）。
    ///
    /// ノイズ勢は「気分で売買する個人投資家」なので大口であってはいけない。
    /// 全アーキタイプ共通のパレート分布から引くと、編成シード次第でノイズ勢に大口が混ざり、
    /// その1人の乱数が他の全員のシグナルを飲み込んで相場の性格を決めてしまう
    /// （v2 実測: シードにより σ が 0.003〜0.040 と10倍以上ぶれた）。
    /// </summary>
    public float noiseCapitalCapRatio = 4f;

    /// <summary>
    /// 適正値への直接の引き寄せ（層1の力）。毎ターン価格を適正値へ この割合だけ（対数空間で）寄せる。
    ///
    ///   rate_total = rate_market × (fair / price)^anchorPull
    ///
    /// 逆張り勢の注文経由でも引き寄せは起きるが、注文は ±1 で飽和するため λ を上げると
    /// 一気に行き過ぎて翌ターン反転する（ノコギリ波・racf1 が大きく負になる）。
    /// この項は 1 未満なので原理的に行き過ぎない。結果、λ でボラだけを独立に調整できる。
    /// 0 で無効（v2 初期の挙動）。想定レンジ 0.05〜0.35。
    /// </summary>
    public float anchorPull = 0.15f;

    /// <summary>順張りの感度。</summary>
    public float momentumGain = 2f;

    /// <summary>逆張りの感度。適正値への引き寄せの強さ。下限 2 を推奨（0 だとアンカーが消える）。</summary>
    public float valueGain = 4f;

    /// <summary>適正値の変化（需要の変化）への感度。下限 2 を推奨。</summary>
    public float demandGain = 10f;

    /// <summary>マーケットメイカーの感度。</summary>
    public float marketMakerGain = 5f;

    /// <summary>群衆行動の強さ。前ターンの純注文へ全員が引きずられる度合い。0 で無効。</summary>
    public float herdingGain = 0.15f;

    /// <summary>
    /// 不感帯の上限。各トレーダーは 0〜この値の個人閾値を持ち、
    /// シグナルがそれに届かないターンは市場に参加しない。
    /// 参加人数がターンごとに揺れることがファットテールとクラスタリングの源。
    /// </summary>
    public float inactionBandMax = 0.85f;

    /// <summary>
    /// 価格インパクトの指数。0.5 = 平方根則、1.0 = 線形。
    /// 平方根は大口注文の価格変化を圧縮し、尖度を 2 前後に張り付かせる（v1 で実測）。1.0 前後を使う。
    /// </summary>
    public float impactExponent = 1.0f;

    /// <summary>価格インパクト係数。ボラティリティ水準の主ツマミ。</summary>
    public float lambda = 0.3f;

    /// <summary>板の厚みの基準値。</summary>
    public float baseDepth = 800f;

    /// <summary>
    /// 在庫が板の厚みに与える影響。既定 0 = 影響なし。
    /// 線形に効かせるとプレイヤーが仕入れるほど価格が凍るため、既定では切っている。
    /// </summary>
    public float stockDepthWeight;

    /// <summary>同じ値を持つ複製を返す。探索で設定を変異させる際、元を壊さないために使う。</summary>
    public LocalAbmSettings Clone()
    {
        var clone = new LocalAbmSettings
        {
            traderCount = traderCount,
            capitalParetoAlpha = capitalParetoAlpha,
            capitalMin = capitalMin,
            capitalMax = capitalMax,
            noiseCapitalCapRatio = noiseCapitalCapRatio,
            anchorPull = anchorPull,
            momentumGain = momentumGain,
            valueGain = valueGain,
            demandGain = demandGain,
            marketMakerGain = marketMakerGain,
            herdingGain = herdingGain,
            inactionBandMax = inactionBandMax,
            impactExponent = impactExponent,
            lambda = lambda,
            baseDepth = baseDepth,
            stockDepthWeight = stockDepthWeight,
        };

        if (archetypeWeights != null)
        {
            clone.archetypeWeights = new float[archetypeWeights.Length];
            Array.Copy(archetypeWeights, clone.archetypeWeights, archetypeWeights.Length);
        }
        return clone;
    }
}

/// <summary>
/// 仮想トレーダー1体。性格・資金・積極度・記憶長・不感帯だけを持つ軽量オブジェクト。
/// </summary>
public sealed class MarketTrader
{
    public TraderArchetype Archetype { get; }

    /// <summary>資金力。注文量のスケール。</summary>
    public float Capital { get; }

    /// <summary>積極度。0.5〜1.5。</summary>
    public float Aggressiveness { get; }

    /// <summary>何ターン前までの値動きを見るか（1以上）。</summary>
    public int Memory { get; }

    /// <summary>参加に必要なシグナルの大きさ。0 なら毎ターン参加する。</summary>
    public float InactionThreshold { get; }

    public MarketTrader(TraderArchetype archetype, float capital, float aggressiveness, int memory,
        float inactionThreshold = 0f)
    {
        Archetype = archetype;
        Capital = Sanitize(capital, 0f);
        Aggressiveness = Sanitize(aggressiveness, 1f);
        Memory = memory < 1 ? 1 : memory;
        InactionThreshold = Mathf.Max(0f, Sanitize(inactionThreshold, 0f));
    }

    /// <summary>この銘柄への注文量を返す。正が買い、負が売り。</summary>
    public float DecideOrder(IMarketView view, LocalAbmSettings settings, System.Random rng)
    {
        if (view == null || settings == null) return 0f;

        float signal;
        switch (Archetype)
        {
            case TraderArchetype.Momentum:
                signal = RecentReturn(view, Memory) * settings.momentumGain;
                break;

            case TraderArchetype.Contrarian:
            {
                // 基準価格ではなく「適正値」との乖離を見る。ここが v2 の要。
                float fair = Mathf.Max(1f, view.FairValue);
                float gap = (view.CurrentPrice / fair) - 1f;
                signal = -gap * settings.valueGain;
                break;
            }

            case TraderArchetype.DemandWatcher:
            {
                // 適正値の変化率＝需要の変化。上がったなら買って先回りする。
                float prev = Mathf.Max(1f, view.PreviousFairValue);
                float change = (view.FairValue / prev) - 1f;
                signal = change * settings.demandGain;
                break;
            }

            case TraderArchetype.MarketMaker:
                signal = -RecentReturn(view, 1) * settings.marketMakerGain;
                break;

            default: // Noise
                signal = rng != null ? (float)(rng.NextDouble() * 2.0 - 1.0) : 0f;
                break;
        }

        signal = Sanitize(signal, 0f);

        // 不感帯: シグナルが自分の閾値に届かないターンは市場に参加しない。
        // 参加人数の揺らぎが実効 N を変え、ボラティリティ・クラスタリングを生む。
        if (InactionThreshold > 0f && Mathf.Abs(signal) < InactionThreshold) return 0f;

        float order = Capital * Aggressiveness * Mathf.Clamp(signal, -1f, 1f);

        // 群衆行動: ノイズ勢以外は前ターンの純注文に引きずられる。
        if (Archetype != TraderArchetype.Noise && settings.herdingGain != 0f)
        {
            float herd = Mathf.Clamp(Sanitize(view.LastNetOrder, 0f) / 1000f, -1f, 1f);
            order += Capital * Aggressiveness * herd * settings.herdingGain;
        }

        return Sanitize(order, 0f);
    }

    /// <summary>直近リターン。末尾（最新）と memory 本前の価格を比較する。履歴不足なら 0。</summary>
    private static float RecentReturn(IMarketView view, int memory)
    {
        var history = view.PriceHistory;
        if (history == null) return 0f;
        if (memory < 1) memory = 1;

        int count = history.Count;
        int pastIndex = count - 1 - memory;
        if (count < 2 || pastIndex < 0) return 0f;

        float past = history[pastIndex];
        if (past <= 0f) return 0f;

        return Sanitize((history[count - 1] / past) - 1f, 0f);
    }

    internal static float Sanitize(float v, float fallback) =>
        (float.IsNaN(v) || float.IsInfinity(v)) ? fallback : v;
}

/// <summary>
/// トレーダー群を保持し、1銘柄・1ターン分の価格変動率を算出する市場。
///
/// 乱数は「編成用」と「ターン用」に分ける。
///   編成用: コンストラクタの seed から資金・不感帯・記憶長を決める。ターンに依存しない。
///   ターン用: <see cref="BeginTurn"/> で Hash(seed, turn) から作り直す。
/// ターン内の消費は決定論なので、セーブ/ロードを挟んでも同じ価格系列になり、乱数位置を保存する必要がない。
/// </summary>
public sealed class LocalAbmMarket
{
    public LocalAbmSettings Settings { get; }
    public IReadOnlyList<MarketTrader> Traders => _traders;

    /// <summary>直近の Step で算出した純注文。デバッグ表示・群衆行動の入力用。</summary>
    public float LastNetOrder { get; private set; }

    /// <summary>編成に使ったシード。</summary>
    public int Seed { get; }

    private readonly List<MarketTrader> _traders;
    private System.Random _turnRng;

    public LocalAbmMarket(LocalAbmSettings settings, int seed)
    {
        Settings = settings ?? new LocalAbmSettings();
        Seed = seed;
        _traders = BuildRoster(Settings, new System.Random(seed));
        _turnRng = new System.Random(TurnSeed(seed, 0));
    }

    /// <summary>ターン開始時に呼ぶ。ノイズ勢の乱数をこのターン専用に作り直す。</summary>
    public void BeginTurn(int turnIndex)
    {
        _turnRng = new System.Random(TurnSeed(Seed, turnIndex));
    }

    /// <summary>
    /// 1銘柄・1ターン分を進め、価格変動率（1.0 = 据え置き）を返す。
    /// </summary>
    public float Step(IMarketView view)
    {
        if (view == null) return 1f;

        float netOrder = 0f;
        for (int i = 0; i < _traders.Count; i++)
        {
            netOrder += _traders[i].DecideOrder(view, Settings, _turnRng);
        }
        netOrder = MarketTrader.Sanitize(netOrder, 0f);

        // √N 正規化: 人数を増やしてもボラ水準が変わらないようにする（λ を人数と独立にする）
        netOrder /= Mathf.Sqrt(Mathf.Max(1, _traders.Count));

        float depth = OrderFlowPriceEngine.Depth(view.Stock, view.Demand, Settings.baseDepth, Settings.stockDepthWeight);
        float rate = OrderFlowPriceEngine.ToPriceRate(
            netOrder, depth, Settings.lambda, OrderFlowPriceEngine.DefaultMaxLogMove, Settings.impactExponent);

        // 層1の引き寄せ: 価格を適正値へ anchorPull の割合だけ（対数空間で）寄せる。
        // 指数が 1 未満なので行き過ぎず、長期の上下限張り付きを原理的に防ぐ。
        if (Settings.anchorPull > 0f && view.FairValue > 0f && view.CurrentPrice > 0)
        {
            float k = Mathf.Clamp01(Settings.anchorPull);
            rate *= Mathf.Pow(view.FairValue / view.CurrentPrice, k);
        }

        LastNetOrder = netOrder;
        return (float.IsNaN(rate) || float.IsInfinity(rate)) ? 1f : rate;
    }

    // ------------------------------------------------------------
    private static int TurnSeed(int seed, int turn)
    {
        unchecked
        {
            int h = seed * 31 + turn;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }

    private static List<MarketTrader> BuildRoster(LocalAbmSettings s, System.Random rng)
    {
        int kinds = Enum.GetValues(typeof(TraderArchetype)).Length;
        int traderCount = s.traderCount > 0 ? s.traderCount : 1;
        int[] counts = BuildArchetypeCounts(s.archetypeWeights, kinds, traderCount);

        float alpha = s.capitalParetoAlpha > 0.0001f ? s.capitalParetoAlpha : 1f;
        float capitalMin = s.capitalMin > 0f ? s.capitalMin : 1f;
        float capitalMax = Mathf.Max(capitalMin, s.capitalMax);

        var list = new List<MarketTrader>(traderCount);
        for (int i = 0; i < kinds; i++)
        {
            for (int n = 0; n < counts[i]; n++)
            {
                // u は (0,1]。NextDouble() は [0,1) なので 1-x で 0 を除外する。
                float u = (float)(1.0 - rng.NextDouble());
                if (u <= 0f) u = 1e-6f;

                float capital = capitalMin / Mathf.Pow(u, 1f / alpha);
                capital = Mathf.Clamp(MarketTrader.Sanitize(capital, capitalMin), capitalMin, capitalMax);

                // ノイズ勢は個人投資家。大口を許すと1人の乱数が市場を支配する。
                if ((TraderArchetype)i == TraderArchetype.Noise && s.noiseCapitalCapRatio > 0f)
                {
                    capital = Mathf.Min(capital, capitalMin * s.noiseCapitalCapRatio);
                }

                float aggressiveness = 0.5f + (float)rng.NextDouble();
                int memory = 1 + rng.Next(4);
                float threshold = (float)rng.NextDouble() * Mathf.Max(0f, s.inactionBandMax);

                list.Add(new MarketTrader((TraderArchetype)i, capital, aggressiveness, memory, threshold));
            }
        }
        return list;
    }

    /// <summary>
    /// 人数比から各アーキタイプの人数を決める。合計は必ず traderCount。
    /// 丸め誤差は Noise が吸収する（末尾に吸わせると列挙子追加で挙動が変わる）。
    /// 長さが違う weights は、足りない分を 0、余った分を無視して扱う。
    /// </summary>
    private static int[] BuildArchetypeCounts(float[] weights, int kinds, int traderCount)
    {
        var w = new float[kinds];
        float total = 0f;
        if (weights != null)
        {
            for (int i = 0; i < kinds && i < weights.Length; i++)
            {
                float v = weights[i];
                w[i] = (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) ? 0f : v;
                total += w[i];
            }
        }
        if (total <= 0f)
        {
            for (int i = 0; i < kinds; i++) w[i] = 1f;
            total = kinds;
        }

        int absorber = Mathf.Min((int)TraderArchetype.Noise, kinds - 1);
        var counts = new int[kinds];
        int assigned = 0;
        for (int i = 0; i < kinds; i++)
        {
            if (i == absorber) continue;
            int c = Mathf.FloorToInt(traderCount * w[i] / total);
            c = Mathf.Clamp(c, 0, traderCount - assigned);
            counts[i] = c;
            assigned += c;
        }
        counts[absorber] = traderCount - assigned;
        return counts;
    }
}
