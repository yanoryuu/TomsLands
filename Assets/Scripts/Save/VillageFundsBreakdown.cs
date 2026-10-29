using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// ラン終了時に村資金へ入る額の内訳（MetaProgressModel.CalculateVillageFundsBreakdown が作る）。
/// Total は CalculateVillageFunds の戻り値そのもの（同じ計算から出すので食い違わない）。
/// 帰還演出・旧ポップ・破産画面で「なぜ儲けより多く入るのか」を見せるのに使う。
/// </summary>
public sealed class VillageFundsBreakdown
{
    public bool Cleared;
    public GameModeId Mode;
    /// <summary>クリア時=このランの儲け / 破産時=いちばん儲かっていたときの儲け。</summary>
    public int Profit;
    public int Carried;

    // ── クリア ──
    /// <summary>持ち込みの返却（儲けが赤字ならその分を引いた額。0 未満にはならない）。</summary>
    public int CarriedReturn;
    /// <summary>完走ボーナスの固定分（clearBaseFlat）。</summary>
    public int ClearBonusFlat;
    /// <summary>そのランで期日が来た納付金の予定額の合計。</summary>
    public long DebtScheduled;
    public float DebtMultiplier;
    /// <summary>完走ボーナスの納付金のぶん（= ClearBonusBase − ClearBonusFlat）。</summary>
    public int ClearBonusDebt;
    /// <summary>完走ボーナスの土台（領主館の上乗せ前）。</summary>
    public int ClearBonusBase;
    public float HallRate;
    /// <summary>領主館の上乗せ額。</summary>
    public int HallBonus;
    /// <summary>儲けに掛ける率（変換率 × 難易度倍率）。</summary>
    public float ProfitRate;
    /// <summary>儲けから入る額（クリア時）。</summary>
    public int ProfitFunds;

    // ── 破産 ──
    public int ReliefBase;
    public long PaidDebtScheduled;
    public float PaidDebtRate;
    public int ReliefDebt;
    public long Relief;
    /// <summary>儲けに掛ける率（変換率 × 難易度倍率 × 破産時の率）。</summary>
    public float BankruptProfitRate;
    public int BankruptFromProfit;
    /// <summary>救済より最高の儲けから出す額の方が多かった（そちらが入った）。</summary>
    public bool UsedProfit;

    public int Total;

    /// <summary>内訳の1行（左に何の分か、右に額）。</summary>
    public readonly struct Line
    {
        public readonly string Label;
        public readonly string Amount;

        public Line(string label, string amount)
        {
            Label = label;
            Amount = amount;
        }

        public override string ToString() => $"{Label}　{Amount}";
    }

    /// <summary>画面に出す内訳の行（0 の行は出さない）。</summary>
    public List<Line> BuildLines()
    {
        var lines = new List<Line>();
        if (Cleared)
        {
            if (ClearBonusFlat > 0) lines.Add(new Line("完走ボーナス", Plus(ClearBonusFlat)));
            if (ClearBonusDebt > 0)
                lines.Add(new Line($"納付金のぶん<size=80%>（納付 {DebtScheduled:N0}G × {Rate(DebtMultiplier)}）</size>", Plus(ClearBonusDebt)));
            if (HallBonus > 0)
                lines.Add(new Line($"領主館 +{Mathf.RoundToInt(HallRate * 100f)}%", Plus(HallBonus)));
            if (Profit > 0)
            {
                string rate = Mathf.Approximately(ProfitRate, 1f)
                    ? ""
                    : $"<size=80%> × {Rate(ProfitRate)}（{SaveSlotView.GetModeLabel(Mode)}）</size>";
                lines.Add(new Line($"儲け {Profit:N0}G{rate}", Plus(ProfitFunds)));
            }
            else if (Profit < 0 && Carried <= 0)
            {
                lines.Add(new Line("儲け<size=80%>（赤字なので入らない）</size>", Plus(0)));
            }
            if (CarriedReturn > 0)
            {
                string note = Profit < 0 ? $"<size=80%>（赤字 -{-(long)Profit:N0}G を引く）</size>" : "";
                lines.Add(new Line($"持ち込みの返却{note}", Plus(CarriedReturn)));
            }
        }
        else if (UsedProfit)
        {
            lines.Add(new Line($"最高の儲け {Profit:N0}G<size=80%> × {Rate(BankruptProfitRate)}</size>", Plus(BankruptFromProfit)));
        }
        else
        {
            if (ReliefBase > 0) lines.Add(new Line("救済", Plus(ReliefBase)));
            if (ReliefDebt > 0)
            {
                string note = Mathf.Approximately(PaidDebtRate, 1f)
                    ? ""
                    : $"<size=80%>（納付 {PaidDebtScheduled:N0}G × {Rate(PaidDebtRate)}）</size>";
                lines.Add(new Line($"納付した額{note}", Plus(ReliefDebt)));
            }
        }
        return lines;
    }

    /// <summary>内訳を改行でつないだ文字列（旧ポップ・破産画面用）。</summary>
    public string BuildText(string separator = "\n") => string.Join(separator, BuildLines());

    /// <summary>1行に収める短い形（「救済 +50,000G・納付した額 +44,000G」。小さい文字の指定は外す）。</summary>
    public string BuildInlineText()
    {
        var parts = new List<string>();
        var lines = BuildLines();
        foreach (var line in lines)
        {
            // 1行だけなら額は合計と同じなので書かない（「最高の儲け 2,000,000G × 0.25」）
            string text = lines.Count == 1 ? line.Label : $"{line.Label} {line.Amount}";
            parts.Add(text.Replace("<size=80%>", "").Replace("</size>", ""));
        }
        return string.Join("・", parts);
    }

    private static string Plus(long v) => $"+{v:N0}G";

    private static string Rate(float r) => r.ToString("0.###", CultureInfo.InvariantCulture);
}
