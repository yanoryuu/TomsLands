using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相場の「荒れ具合」（0〜1）。直近の値動きの大きさを指数移動平均したもの。
///
/// 2つの役割を兼ねる:
///   1. ABM が λ（ボラティリティ）を変調する入力。荒れている銘柄はさらに荒れやすくなり、
///      静かな銘柄は静かなまま——これが「荒れる時期と凪の時期が固まって現れる」
///      ボラティリティ・クラスタリングを生む。値動きの「大きさ」だけに効き、
///      「向き」には効かないので、トレンドが読めてしまう副作用は出ない。
///   2. UI 表示。プレイヤーに「この銘柄は今どのくらい荒れているか」を伝える。
///
/// <b>状態を持たない。</b>価格履歴だけから計算するので、セーブ/ロードを挟んでも
/// 同じ値になり、保存対象を増やす必要がない。
/// </summary>
public static class MarketHeat
{
    /// <summary>既定の半減期（ターン）。4 なら 4ターン前の値動きが半分の重みになる。</summary>
    public const float DefaultHalfLife = 4f;

    /// <summary>
    /// 既定の基準ボラティリティ。1ターンの値動きがこの大きさのとき Heat が 0.5 になる。
    /// ABM の σ 実測値（約 0.019）に合わせてある。
    /// </summary>
    public const float DefaultReference = 0.02f;

    /// <summary>履歴が足りないときに返す中立値。</summary>
    public const float Neutral = 0.5f;

    /// <summary>
    /// 価格履歴から荒れ具合を求める。末尾が最新であること。
    /// </summary>
    /// <param name="priceHistory">価格履歴（末尾が最新）。<see cref="RuntimeItemData.ShopPriceHistory"/> を想定。</param>
    /// <param name="halfLife">指数移動平均の半減期（ターン）。短いほど直近に反応する。</param>
    /// <param name="reference">Heat が 0.5 になる 1ターンあたりの値動きの大きさ。</param>
    /// <returns>0（完全な凪）〜1（大荒れ）。履歴が 3 点未満なら <see cref="Neutral"/>。</returns>
    public static float Compute(IReadOnlyList<int> priceHistory,
        float halfLife = DefaultHalfLife, float reference = DefaultReference)
    {
        if (priceHistory == null || priceHistory.Count < 3) return Neutral;

        float hl = Mathf.Max(0.5f, halfLife);
        float alpha = 1f - Mathf.Pow(2f, -1f / hl);

        // 古い順に |対数リターン| を EWMA する
        float ewma = 0f;
        bool seeded = false;
        for (int i = 1; i < priceHistory.Count; i++)
        {
            float prev = priceHistory[i - 1];
            float curr = priceHistory[i];
            if (prev <= 0f || curr <= 0f) continue;

            float r = Mathf.Abs(Mathf.Log(curr / prev));
            if (float.IsNaN(r) || float.IsInfinity(r)) continue;

            if (!seeded) { ewma = r; seeded = true; }
            else ewma += alpha * (r - ewma);
        }
        if (!seeded) return Neutral;

        // 飽和写像: ewma == reference で 0.5、0 で 0、大きいほど 1 へ漸近する。
        // 上限を持つので、一時的な暴騰で Heat が振り切れたまま戻らなくなることがない。
        float refv = Mathf.Max(1e-5f, reference);
        float heat = ewma / (ewma + refv);
        return (float.IsNaN(heat) || float.IsInfinity(heat)) ? Neutral : Mathf.Clamp01(heat);
    }

    /// <summary>
    /// Heat を人が読めるラベルにする。UI 表示用。
    /// </summary>
    public static string Describe(float heat)
    {
        if (heat < 0.25f) return "凪";
        if (heat < 0.42f) return "静か";
        if (heat < 0.58f) return "平常";
        if (heat < 0.75f) return "活況";
        return "荒れ";
    }

    /// <summary>
    /// Heat に対応する表示色。凪=青寄り、平常=灰、荒れ=赤寄り。
    /// </summary>
    public static Color ToColor(float heat)
    {
        return heat < 0.5f
            ? Color.Lerp(new Color(0.35f, 0.62f, 0.90f), new Color(0.72f, 0.72f, 0.74f), heat * 2f)
            : Color.Lerp(new Color(0.72f, 0.72f, 0.74f), new Color(0.90f, 0.35f, 0.28f), (heat - 0.5f) * 2f);
    }

    /// <summary>星や棒で段階表示したいとき用。0〜4 の5段階。</summary>
    public static int ToLevel(float heat) => Mathf.Clamp(Mathf.FloorToInt(heat * 5f), 0, 4);
}
