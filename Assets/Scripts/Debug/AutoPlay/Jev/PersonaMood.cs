#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>
/// 気分（Docs/Persona_Formation_Design.md の 4 章。ALMA: 出来事 → 気分 → 性格の基準点へ戻る）。
/// 自信 conf（−1〜1）・しょんぼり sad・ドヤ smug・イラ irk（0〜1）。ランの始まりに基準点へ戻す。
/// - 結果（判断の損益）で動く: g = (損益 − いつもの損益) / いつものばらつき（中央値と MAD、±3 で切る）。
/// - 相談の後日談で動く: 押し切って当てた → smug、押し切って外した → sad・conf 下げ、マウントされた（めたん）→ irk。
/// - 判断ごとに基準点へ減衰（半減期 = 特性の回復の速さから。ずんだもん 2・めたん 6 判断）。smug は判断ごとに半分。
/// - めたんの「崩れ」（irk が高いとき慎重策を捨てる）は 1 ランに 1 回まで。
/// 純粋なロジックだけ（ゲームには触らない）。
/// </summary>
public sealed class PersonaMood
{
    public float Conf, Sad, Smug, Irk;
    private readonly PersonaTraits _t;
    private readonly Dictionary<string, List<float>> _absByQuestion = new();
    /// <summary>このランで崩れたか（めたん。1 ランに 1 回まで）。</summary>
    public bool CollapseUsed;
    /// <summary>崩れている最中の日（その日の判断だけ慎重策を捨てる）。</summary>
    public int CollapseDay = int.MinValue;
    /// <summary>直前の損失の後、損失回避を一時的に強める残り判断数（めたん: 負けた直後は λ × 1.2）。</summary>
    public int LossStreakDecisions;
    /// <summary>最後に起きた出来事（一言・ログ用）。</summary>
    public string LastEvent;

    public const float Threshold = 0.3f;
    /// <summary>めたんが崩れるイラの大きさ。</summary>
    public const float CollapseIrk = 0.2f;

    public PersonaMood(PersonaTraits traits)
    {
        _t = traits ?? PersonaTraits.Zundamon;
        ResetRun();
    }

    public PersonaTraits Traits => _t;
    public float ConfBase => _t.ConfBase;

    public void ResetRun()
    {
        Conf = _t.ConfBase;
        Sad = Smug = Irk = 0f;
        CollapseUsed = false;
        CollapseDay = int.MinValue;
        LossStreakDecisions = 0;
        LastEvent = null;
    }

    /// <summary>いちばん強い気分のラベル（conf_high / sad / smug / irk / neutral）。</summary>
    public string Label
    {
        get
        {
            string best = "neutral";
            float v = Threshold - 1e-4f;
            void Try(string k, float x)
            {
                if (x > v) { v = x; best = k; }
            }
            Try("sad", Sad);
            Try("smug", Smug);
            Try("irk", Irk);
            Try("conf_high", Conf - 0.4f);
            return best;
        }
    }

    /// <summary>判断 1 件ぶん基準点へ戻す。</summary>
    public void Decay()
    {
        float k = Mathf.Pow(2f, -1f / Mathf.Max(0.5f, _t.MoodHalfLife));
        Conf = _t.ConfBase + (Conf - _t.ConfBase) * k;
        Sad *= k;
        Irk *= k;
        Smug *= 0.5f;
        if (LossStreakDecisions > 0) LossStreakDecisions--;
    }

    /// <summary>
    /// 結果のまとまり（ResolvePending 1 回ぶん）。各判断の損益をその質問のいつもの大きさで割って平均し、1 回だけ気分を動かす（g の平均）。
    /// </summary>
    public void OnResults(IEnumerable<(string question, float reward)> results)
    {
        var gs = new List<float>();
        foreach (var (q, r) in results)
        {
            if (string.IsNullOrEmpty(q)) continue;
            // いつもの結果（中央値）との差を、いつものばらつき（中央値からの絶対偏差の中央値）で割る。
            // 配信の損益は売上が入るのでいつもプラス。差で見ないと、いつも「勝ち」になる
            if (!_absByQuestion.TryGetValue(q, out var list)) _absByQuestion[q] = list = new List<float>();
            float g;
            if (list.Count >= 3)
            {
                float center = Median(list);
                float mad = Median(list.Select(v => Mathf.Abs(v - center)).ToList());
                g = (r - center) / Mathf.Max(1000f, mad);
            }
            else g = r / Mathf.Max(10000f, Mathf.Abs(r));
            list.Add(r);
            if (list.Count > 60) list.RemoveAt(0);
            gs.Add(Mathf.Clamp(g, -3f, 3f));
        }
        if (gs.Count == 0) return;
        ApplyG(gs.Average());
    }

    /// <summary>1 つの結果の大きさ g（−3〜3）で動かす。</summary>
    public void ApplyG(float g)
    {
        float n = _t.N;
        Conf = Mathf.Clamp(Conf + 0.25f * g * (1f + n), -1f, 1f);
        if (g < -0.1f)
        {
            Sad = Mathf.Clamp01(Sad + 0.4f * n * Mathf.Min(1f, Mathf.Abs(g)));
            Smug = 0f;                                        // 天罰
            LossStreakDecisions = 8;
            LastEvent = "loss";
        }
        else if (g > 0.1f)
        {
            Sad *= 0.5f;
            if (Irk > 0f && g > 0.5f) Irk = 0f;               // 当てたらイラは消える
            LastEvent = "win";
        }
    }

    /// <summary>相談の後日談（kind = told / mount / thanks / blame）。zundamon = 遊ぶ側か。</summary>
    public void OnFollowUp(string kind, bool player)
    {
        if (player)
        {
            switch (kind)
            {
                case "mount":   // 押し切って当てた
                    Smug = 0.6f + 0.4f * _t.Pride;
                    Conf = Mathf.Clamp(Conf + 0.2f, -1f, 1f);
                    break;
                case "told":    // 押し切って外した・だから言ったでしょ
                    Sad = Mathf.Clamp01(Sad + 0.3f);
                    Conf = Mathf.Clamp(Conf - 0.3f, -1f, 1f);
                    Smug = 0f;
                    break;
                case "thanks":
                    Conf = Mathf.Clamp(Conf - 0.1f, -1f, 1f);
                    break;
                case "blame":
                    Conf = Mathf.Clamp(Conf + 0.1f, -1f, 1f);
                    break;
            }
        }
        else
        {
            switch (kind)
            {
                case "mount":   // ずんだもんにマウントされた
                    Irk = Mathf.Clamp01(Irk + 0.5f * _t.Pride);
                    Conf = Mathf.Clamp(Conf - 0.1f, -1f, 1f);
                    break;
                case "told":
                    Conf = Mathf.Clamp(Conf + 0.2f, -1f, 1f);
                    Smug = Mathf.Clamp01(Smug + 0.3f);
                    Irk = 0f;
                    break;
                case "thanks":
                    Conf = Mathf.Clamp(Conf + 0.2f, -1f, 1f);
                    Irk = 0f;
                    break;
                case "blame":
                    Conf = Mathf.Clamp(Conf - 0.2f, -1f, 1f);
                    break;
            }
        }
        LastEvent = kind;
    }

    /// <summary>めたんの崩れ: イラが高く、このランでまだ崩れていなければ、この日だけ慎重策を捨てる（1 ランに 1 回）。</summary>
    public bool CollapseActive(int day)
    {
        if (CollapseDay == day) return true;
        // イラはマウント 1 回で 0.5 × 自尊（めたん 0.33）。判断ごとの減衰ですぐ 0.3 を割るので、崩れの判定は 0.2 から
        if (CollapseUsed || _t.LossAversion <= 1f || Irk < CollapseIrk) return false;
        CollapseUsed = true;
        CollapseDay = day;
        return true;
    }

    /// <summary>崩れて外した: イラは消える。</summary>
    public void EndCollapse()
    {
        Irk = 0f;
        CollapseDay = int.MinValue;
    }

    /// <summary>ログ用（conf / sad / smug / irk / label）。</summary>
    public Dictionary<string, object> Snapshot() => new()
    {
        ["conf"] = Math.Round(Conf, 3),
        ["sad"] = Math.Round(Sad, 3),
        ["smug"] = Math.Round(Smug, 3),
        ["irk"] = Math.Round(Irk, 3),
        ["label"] = Label,
        ["lossStreak"] = LossStreakDecisions,   // 負けの後の残り判断数（指標 M4 で「負けた後」を数える）
        ["last"] = LastEvent,
    };

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "conf {0:0.00} sad {1:0.00} smug {2:0.00} irk {3:0.00} ({4})", Conf, Sad, Smug, Irk, Label);

    private static float Median(List<float> list)
    {
        var s = list.OrderBy(v => v).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : 0.5f * (s[n / 2 - 1] + s[n / 2]);
    }
}
#endif
