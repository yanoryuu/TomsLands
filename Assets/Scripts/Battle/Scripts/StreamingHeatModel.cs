using R3;
using UnityEngine;

/// <summary>
/// 配信熱モデル。バトルの盛り上がりを 0〜100 の Heat 値で管理し、
/// アイテム価格への倍率・UI 表示用ラベル・カラーを提供する。
/// バトル終了時に破棄され、TomsShop には影響しない。
/// </summary>
public class StreamingHeatModel
{
    // ─────────────────────────────────────────
    //  定数
    // ─────────────────────────────────────────
    public const float MinHeat = 0f;
    public const float MaxHeat = 100f;

    // ティア境界値
    private const float TierColdMax   = 25f;
    private const float TierNormalMax = 50f;
    private const float TierHotMax    = 75f;
    // 75〜100 = 超人気

    // ─────────────────────────────────────────
    //  プロパティ
    // ─────────────────────────────────────────

    /// <summary>現在の配信熱（0〜100）。UI購読用。</summary>
    public ReactiveProperty<float> Heat { get; } = new ReactiveProperty<float>(30f);

    // ─────────────────────────────────────────
    //  熱量変更
    // ─────────────────────────────────────────

    /// <summary>熱量を加算する（負値で減算）。自動的に [0, 100] にクランプ。</summary>
    public void AddHeat(float delta)
    {
        Heat.Value = Mathf.Clamp(Heat.Value + delta, MinHeat, MaxHeat);
    }

    /// <summary>ターン終了時の自然減衰。毎ターン呼ぶ。</summary>
    public void ApplyTurnDecay(float decay = 5f)
    {
        AddHeat(-decay);
    }

    // ─────────────────────────────────────────
    //  価格倍率
    // ─────────────────────────────────────────

    /// <summary>
    /// 現在の Heat に基づく 1 ターンあたりの価格倍率を返す。
    /// 毎ターン複利で積み上がる（例: 1.02 を 10 ターン → 約 1.22 倍）。
    /// </summary>
    public float GetPriceMultiplier()
    {
        float h = Heat.Value;
        if (h < TierColdMax)   return 0.97f; // 冷え冷え → 価格じわ下がり
        if (h < TierNormalMax) return 1.00f; // 普通     → 変化なし
        if (h < TierHotMax)    return 1.02f; // 盛り上がり → じわ上昇
        return 1.05f;                         // 超人気   → 急上昇
    }

    // ─────────────────────────────────────────
    //  表示用
    // ─────────────────────────────────────────

    public string GetTierLabel()
    {
        float h = Heat.Value;
        if (h < TierColdMax)   return "冷め気味...";
        if (h < TierNormalMax) return "普通";
        if (h < TierHotMax)    return "盛り上がり中！";
        return "超人気！";
    }

    /// <summary>Heat 値に対応するバーカラー（青→緑→橙→赤 のグラデーション）。</summary>
    public Color GetTierColor()
    {
        float h = Heat.Value;

        // 冷え(青) ～ 普通(緑)
        if (h < TierColdMax)
            return Color.Lerp(
                new Color(0.30f, 0.50f, 1.00f),
                new Color(0.30f, 0.85f, 0.40f),
                h / TierColdMax);

        // 普通(緑) ～ 盛り上がり(橙)
        if (h < TierNormalMax)
            return Color.Lerp(
                new Color(0.30f, 0.85f, 0.40f),
                new Color(1.00f, 0.60f, 0.10f),
                (h - TierColdMax) / (TierNormalMax - TierColdMax));

        // 盛り上がり(橙) ～ 超人気(赤)
        if (h < TierHotMax)
            return Color.Lerp(
                new Color(1.00f, 0.60f, 0.10f),
                new Color(1.00f, 0.20f, 0.20f),
                (h - TierNormalMax) / (TierHotMax - TierNormalMax));

        return new Color(1.00f, 0.10f, 0.10f);
    }
}
