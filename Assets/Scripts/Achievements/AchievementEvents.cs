using System.Collections.Generic;
using R3;

/// <summary>配信の精算が済んだときの情報（BattleResultHandler から、BattleOutputData を消す直前に通知）。</summary>
public sealed class StreamSettledInfo
{
    public BattleResult Result;
    public int Turn;
    public DungeonName DungeonKey;
    /// <summary>配信したダンジョンのレベル（0 = 未出資、-1 = 不明）。</summary>
    public int DungeonLevel = -1;
    public string WeaponId;
    public string ArmorId;
    /// <summary>配信中に売れた額（介入・防衛報酬を含まない売上だけ）。</summary>
    public int GrossSales;
    public int TotalEarnings;
    /// <summary>通知元の BattleOutputData（自動テストで複数ランを見分ける用）。</summary>
    public BattleOutputData Origin;
}

/// <summary>出店をクリアしてリザルトを集計したときの情報（ResultModel.BuildStatistics から）。</summary>
public sealed class RunClearedInfo
{
    public GameModeId Mode;
    public string Rank;
    public int TotalTurns;
    public int DebtCycle;
}

/// <summary>
/// Model の外（Presenter・ハンドラ）で起きる、実績の判定に要る出来事の受け口。
/// 呼び出し側は Report* を1行足すだけにし、判定は AchievementTracker / AchievementService が行う。
/// 購読者がいなければ何も起きない（エディタの自動テストプレイでも安全）。
/// </summary>
public static class AchievementEvents
{
    private static readonly Subject<StreamSettledInfo> streamSettled = new();
    private static readonly Subject<RunClearedInfo> runCleared = new();

    public static Observable<StreamSettledInfo> StreamSettled => streamSettled;
    public static Observable<RunClearedInfo> RunCleared => runCleared;

    public static void ReportStreamSettled(BattleOutputData output, BattleInputData input, int turn)
    {
        if (output == null) return;
        int gross = 0;
        if (output.SoldItems != null)
            foreach (var s in output.SoldItems)
                if (s != null && s.Revenue > 0) gross = TomsModel.SaturatingAdd(gross, s.Revenue);

        streamSettled.OnNext(new StreamSettledInfo
        {
            Result = output.Result,
            Turn = turn,
            DungeonKey = input != null ? input.DungeonKey : default,
            DungeonLevel = input != null ? input.DungeonLevel : -1, // -1 = 不明（Lv0 は未出資）
            WeaponId = output.WeaponId,
            ArmorId = output.ArmorId,
            GrossSales = gross,
            TotalEarnings = output.TotalEarnings,
            Origin = output,
        });
    }

    public static void ReportRunCleared(TomsModel toms, string rank)
    {
        if (toms == null) return;
        var info = new RunClearedInfo
        {
            Mode = toms.GameMode,
            Rank = rank ?? "",
            TotalTurns = toms.CurrentTurn.Value,
            DebtCycle = toms.DebtCycle.Value,
        };
        AchievementService.Shared.HandleRunCleared(info);
        runCleared.OnNext(info);
    }

    /// <summary>テスト・デバッグで使う一覧（装備 ID の空を除く）。</summary>
    public static List<string> EquippedIds(StreamSettledInfo info)
    {
        var list = new List<string>(2);
        if (!string.IsNullOrEmpty(info.WeaponId)) list.Add(info.WeaponId);
        if (!string.IsNullOrEmpty(info.ArmorId) && info.ArmorId != info.WeaponId) list.Add(info.ArmorId);
        return list;
    }
}
