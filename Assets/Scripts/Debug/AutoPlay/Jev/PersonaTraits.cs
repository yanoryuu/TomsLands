#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// 人格の特性ベクトル（固定。Docs/Persona_Formation_Design.md の 3 章、人物像は tools/jikkyo_video/CHARACTERS.md）。
/// Big Five（O C E A N）＋正直さ H と、そこから導いた判断のつまみ（危険選好・損失回避・衝動性・回復の速さ…）。
/// PersonaProfile の定数（温度・自信過剰の段数・難しい道の確率・現金の下限・学習の強さ・派手な施設の倍率）はここから導く。
/// tools/persona_play/replay_selection.py の TRAITS / derive() に同じ写しがある（数字を変えたら両方直す）。
/// </summary>
public sealed class PersonaTraits
{
    // ---- Big Five ＋ H（0〜1） ----
    public float O, C, E, A, N, H;

    // ---- 導出した特性（表の値。式は向きだけ: risk ≈ 0.2 + 0.35E + 0.3O − 0.35C − 0.1N、impulsivity ≈ 0.5E + 0.5(1−C)） ----
    /// <summary>危険選好（大きく張る度合い）。</summary>
    public float Risk;
    /// <summary>損失回避 λ（損を何倍重く見るか）。</summary>
    public float LossAversion = 1f;
    /// <summary>衝動性（選ぶ温度の元）。</summary>
    public float Impulsivity;
    /// <summary>回復の速さ（気分の半減期の逆）。</summary>
    public float Recovery;
    /// <summary>自尊（勝ったら言いたい・言われたら悔しい）。</summary>
    public float Pride;
    /// <summary>助言の受け入れ（人の意見で手を変える）。</summary>
    public float Receptivity;
    /// <summary>学ぶ速さ（技能を判断に混ぜる強さ）。</summary>
    public float SkillRate;
    /// <summary>ミーム好き。</summary>
    public float MemeAffinity;
    /// <summary>自信の基準点（気分 conf がここへ戻る）。</summary>
    public float ConfBase;

    // ---- 固まりの保険（5.3） ----
    /// <summary>人格の事前分布への KL の上限（nat）。</summary>
    public float KlEps;
    /// <summary>自由な質問のエントロピーの下限（bit）。</summary>
    public float EntropyFloorBits;

    // =====================================================
    // 導出（P1: いまの定数と同じ値になる。小数 2 桁に丸めて float の誤差を消す）
    // =====================================================

    private static float R2(float v) => Mathf.Round(v * 100f) / 100f;

    /// <summary>大胆さの段（0 = めたん、1 = ずんだもん）。(risk − 0.30) / 0.55。</summary>
    private float Boldness => Mathf.Clamp01((Risk - 0.30f) / 0.55f);

    /// <summary>衝動性が半分以上なら確率から抽選、未満なら慎重な選び方。</summary>
    public bool Sample => Impulsivity >= 0.5f;

    /// <summary>抽選の温度 τ = 0.4 + 1.5 × 衝動性（抽選しない人格は 1）。</summary>
    public float Temperature => Sample ? R2(0.4f + 1.5f * Impulsivity) : 1f;

    /// <summary>予算・勢いの段を上へずらす数（自信過剰）。</summary>
    public float ScoreBias => Mathf.Round(Boldness);

    /// <summary>難しい道（情報を買わない・魔王軍に張る）をわざと選ぶ確率。</summary>
    public float HardRouteChance => R2(0.3f * Boldness);

    /// <summary>現金の下限（G）。損失回避 λ が 1 を超えるぶんだけ 1 万G 単位で。</summary>
    public int CashFloor => Mathf.RoundToInt(Mathf.Max(0f, LossAversion - 1f) / 1.25f) * 10000;

    /// <summary>学習器の λ（技能を混ぜる強さ）= 1.4 × 学ぶ速さ − 0.04。</summary>
    public float LearningRate => Mathf.Max(0f, R2(1.4f * SkillRate - 0.04f));

    /// <summary>派手な施設に投資する確率の倍率 = 1 + 1.2 × (外向性 − 0.4)。</summary>
    public float FlashyBoost => Mathf.Max(1f, R2(1f + 1.2f * (E - 0.4f)));

    /// <summary>気分の半減期（判断の数）: 回復 0.85 → 2、0.40 → 6。</summary>
    public float MoodHalfLife => Mathf.Max(1f, Mathf.Round((2f + (0.85f - Recovery) * (4f / 0.45f)) * 10f) / 10f);

    public static readonly PersonaTraits Zundamon = new()
    {
        O = 0.80f, C = 0.20f, E = 0.90f, A = 0.70f, N = 0.70f, H = 0.90f,
        Risk = 0.85f, LossAversion = 1.0f, Impulsivity = 0.80f, Recovery = 0.85f, Pride = 0.80f,
        Receptivity = 0.30f, SkillRate = 0.10f, MemeAffinity = 0.85f, ConfBase = 0.5f,
        KlEps = 0.15f, EntropyFloorBits = 1.0f,
    };

    public static readonly PersonaTraits Metan = new()
    {
        O = 0.55f, C = 0.90f, E = 0.40f, A = 0.30f, N = 0.50f, H = 0.45f,
        Risk = 0.30f, LossAversion = 2.25f, Impulsivity = 0.15f, Recovery = 0.40f, Pride = 0.65f,
        Receptivity = 0.50f, SkillRate = 0.60f, MemeAffinity = 0.10f, ConfBase = 0f,
        KlEps = 0.35f, EntropyFloorBits = 0.5f,
    };
}
#endif
