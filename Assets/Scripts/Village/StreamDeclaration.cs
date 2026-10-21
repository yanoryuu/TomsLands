using UnityEngine;

/// <summary>勝負配信の宣言でどちらが勝つと言ったか（tomsData.json に int で保存する。並べ替えない）。</summary>
public enum StreamDeclarationSide
{
    None = 0,
    Hero = 1,
    Demon = 2,
}

/// <summary>宣言した配信の決着。</summary>
public enum StreamDeclarationOutcome
{
    /// <summary>この配信では宣言していない。</summary>
    None = 0,
    /// <summary>当たり（売上・防衛報酬 ×1.5、フォロワー・注目が増える）。</summary>
    Hit = 1,
    /// <summary>外れ（フォロワー・注目が減る）。</summary>
    Miss = 2,
    /// <summary>配信終了ボタンで途中終了した。宣言は無効で、回数は使ったまま（決着から逃げる手にしない）。</summary>
    Void = 3,
    /// <summary>配信が不具合で途中終了した。宣言は無効で、回数を戻す（次の配信でまた使える）。</summary>
    Returned = 4,
}

/// <summary>
/// 村の広場 Lv5「勝負配信の宣言」（Docs/Village_Meta_Design.md §0-6）。
/// 1ランに1回、配信の前（配信前の寄り道 PreStreamPresenter）に「勇者が勝つ／魔王が勝つ」を宣言できる。
/// 当たれば配信の売上と防衛報酬が ×1.5（BattleSceneStarter が配信の純利益に足す）、フォロワーと注目が大きく増える。
/// 外れればフォロワーと注目が減る（帰還処理 BattleResultHandler が反映する）。
/// 使ったかどうか・決着待ちの宣言はランのセーブ（tomsData.json）に置く。
/// </summary>
public static class StreamDeclaration
{
    public const string OfferTitle = "勝負配信の宣言";
    public const string ChooseSideTitle = "どちらが勝つ？";
    public const string UseButton = "宣言する";
    public const string SkipButton = "今回は使わない";
    public const string HeroButton = "勇者が勝つ";
    public const string DemonButton = "魔王が勝つ";
    public const string CancelButton = "やめる";

    private static VillageSettingsData Settings => GameConst.Village;

    /// <summary>広場が Lv5 で、このランでまだ宣言を使っていないか。</summary>
    public static bool CanOffer(TomsModel toms) =>
        toms != null && !toms.StreamDeclarationUsed && VillageEffects.Current.StreamDeclarationUnlocked;

    /// <summary>宣言する（配信ノードの GameFlow インデックスで、どの配信の宣言かを覚える）。保存もする。</summary>
    public static void Declare(TomsModel toms, StreamDeclarationSide side, int flowIndex)
    {
        if (toms == null || side == StreamDeclarationSide.None || toms.StreamDeclarationUsed) return;
        toms.StreamDeclarationUsed = true;
        toms.StreamDeclarationSide = side;
        toms.StreamDeclarationFlowIndex = flowIndex;
        toms.SavePlayerMoney();
        Debug.Log($"[StreamDeclaration] 宣言: {Label(side)}（flowIndex={flowIndex}）");
    }

    /// <summary>この配信（GameFlow インデックス）に向けた決着待ちの宣言（無ければ None）。</summary>
    public static StreamDeclarationSide ActiveFor(TomsModel toms, int flowIndex) =>
        toms != null && toms.StreamDeclarationSide != StreamDeclarationSide.None && toms.StreamDeclarationFlowIndex == flowIndex
            ? toms.StreamDeclarationSide
            : StreamDeclarationSide.None;

    /// <summary>決着を判定する。途中終了は無効（不具合での途中終了なら回数を戻す）。</summary>
    public static StreamDeclarationOutcome Judge(StreamDeclarationSide side, BattleResult result, bool abortedByError)
    {
        if (side == StreamDeclarationSide.None) return StreamDeclarationOutcome.None;
        if (result == BattleResult.Abort)
            return abortedByError ? StreamDeclarationOutcome.Returned : StreamDeclarationOutcome.Void;
        bool heroWon = result == BattleResult.Victory;
        return (side == StreamDeclarationSide.Hero) == heroWon ? StreamDeclarationOutcome.Hit : StreamDeclarationOutcome.Miss;
    }

    /// <summary>当たったときの上乗せ（配信の売上 + 防衛報酬）×（倍率 − 1）。</summary>
    public static int Bonus(int rawSales, int defenseReward)
    {
        long baseAmount = (long)Mathf.Max(0, rawSales) + Mathf.Max(0, defenseReward);
        double bonus = baseAmount * (double)(VillageEffects.DeclarationMultiplier - 1f);
        return bonus >= int.MaxValue ? int.MaxValue : (int)System.Math.Round(bonus);
    }

    public static int HitFollowers => Mathf.Max(0, Settings.plazaDeclarationHitFollowers);
    public static int HitAttention => Mathf.Max(0, Settings.plazaDeclarationHitAttention);
    public static int MissFollowers => Mathf.Max(0, Settings.plazaDeclarationMissFollowers);
    public static int MissAttention => Mathf.Max(0, Settings.plazaDeclarationMissAttention);

    /// <summary>
    /// 配信の決着を反映する（帰還処理から1回だけ呼ぶ）。フォロワー・注目を動かし、決着待ちの宣言を消して保存する。
    /// 不具合での途中終了（Returned）は回数を戻す。
    /// </summary>
    public static void Resolve(TomsModel toms, ShopStatusModel status, StreamDeclarationOutcome outcome)
    {
        if (toms == null || outcome == StreamDeclarationOutcome.None) return;
        switch (outcome)
        {
            case StreamDeclarationOutcome.Hit:
                status?.ChangeFollowers(HitFollowers);
                status?.ChangeAttention(HitAttention);
                break;
            case StreamDeclarationOutcome.Miss:
                status?.ChangeFollowers(-MissFollowers);
                status?.ChangeAttention(-MissAttention);
                break;
            case StreamDeclarationOutcome.Returned:
                toms.StreamDeclarationUsed = false;
                break;
        }
        toms.StreamDeclarationSide = StreamDeclarationSide.None;
        toms.StreamDeclarationFlowIndex = 0;
        toms.SavePlayerMoney();
        Debug.Log($"[StreamDeclaration] 決着: {outcome}");
    }

    public static string Label(StreamDeclarationSide side) => side switch
    {
        StreamDeclarationSide.Hero => HeroButton,
        StreamDeclarationSide.Demon => DemonButton,
        _ => "",
    };

    /// <summary>宣言の説明（当たり／外れの中身）。ポップアップの本文に使う。</summary>
    public static string EffectLines()
    {
        float mult = VillageEffects.DeclarationMultiplier;
        return $"当たり: 売上と防衛報酬 ×{mult:0.##}・フォロワー +{HitFollowers:N0}・注目 +{HitAttention:N0}\n"
               + $"外れ: フォロワー -{MissFollowers:N0}・注目 -{MissAttention:N0}";
    }
}
