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

    private readonly BattlePriceSettings _settings;

    // ─────────────────────────────────────────
    //  プロパティ
    // ─────────────────────────────────────────

    /// <summary>現在の配信熱（0〜100）。UI購読用。</summary>
    public ReactiveProperty<float> Heat { get; }

    public StreamingHeatModel(BattlePriceSettings settings = null)
    {
        _settings = settings;
        Heat = new ReactiveProperty<float>(Mathf.Clamp(GetInitialHeat(), MinHeat, MaxHeat));
    }

    // ─────────────────────────────────────────
    //  熱量変更
    // ─────────────────────────────────────────

    /// <summary>熱量を加算する（負値で減算）。自動的に [0, 100] にクランプ。</summary>
    public void AddHeat(float delta)
    {
        Heat.Value = Mathf.Clamp(Heat.Value + delta, MinHeat, MaxHeat);
    }

    /// <summary>ターン終了時の自然減衰。毎ターン呼ぶ。</summary>
    public void ApplyTurnDecay()
    {
        AddHeat(-GetHeatTurnDecay());
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
        if (h < GetColdTierMax()) return GetColdPriceMultiplier();
        if (h < GetNormalTierMax()) return GetNormalPriceMultiplier();
        if (h < GetHotTierMax()) return GetHotPriceMultiplier();
        return GetSuperHotPriceMultiplier();
    }

    // ─────────────────────────────────────────
    //  表示用
    // ─────────────────────────────────────────

    public string GetTierLabel()
    {
        float h = Heat.Value;
        if (h < GetColdTierMax()) return "冷め気味...";
        if (h < GetNormalTierMax()) return "普通";
        if (h < GetHotTierMax()) return "盛り上がり中！";
        return "超人気！";
    }

    /// <summary>Heat 値に対応するバーカラー（青→緑→橙→赤 のグラデーション）。</summary>
    public Color GetTierColor()
    {
        float h = Heat.Value;

        // 冷え(青) ～ 普通(緑)
        float coldTierMax = GetColdTierMax();
        float normalTierMax = GetNormalTierMax();
        float hotTierMax = GetHotTierMax();

        if (h < coldTierMax)
            return Color.Lerp(
                new Color(0.30f, 0.50f, 1.00f),
                new Color(0.30f, 0.85f, 0.40f),
                coldTierMax > 0f ? h / coldTierMax : 1f);

        // 普通(緑) ～ 盛り上がり(橙)
        if (h < normalTierMax)
            return Color.Lerp(
                new Color(0.30f, 0.85f, 0.40f),
                new Color(1.00f, 0.60f, 0.10f),
                Mathf.InverseLerp(coldTierMax, normalTierMax, h));

        // 盛り上がり(橙) ～ 超人気(赤)
        if (h < hotTierMax)
            return Color.Lerp(
                new Color(1.00f, 0.60f, 0.10f),
                new Color(1.00f, 0.20f, 0.20f),
                Mathf.InverseLerp(normalTierMax, hotTierMax, h));

        return new Color(1.00f, 0.10f, 0.10f);
    }

    private float GetInitialHeat() => _settings != null ? _settings.initialHeat : 30f;
    private float GetHeatTurnDecay() => _settings != null ? _settings.heatTurnDecay : 5f;
    private float GetColdTierMax() => _settings != null ? _settings.coldTierMax : 25f;
    private float GetNormalTierMax() => _settings != null ? _settings.normalTierMax : 50f;
    private float GetHotTierMax() => _settings != null ? _settings.hotTierMax : 75f;
    private float GetColdPriceMultiplier() => _settings != null ? _settings.coldPriceMultiplier : 0.97f;
    private float GetNormalPriceMultiplier() => _settings != null ? _settings.normalPriceMultiplier : 1.00f;
    private float GetHotPriceMultiplier() => _settings != null ? _settings.hotPriceMultiplier : 1.02f;
    private float GetSuperHotPriceMultiplier() => _settings != null ? _settings.superHotPriceMultiplier : 1.05f;
}
