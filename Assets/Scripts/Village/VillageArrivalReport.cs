/// <summary>
/// ラン終了→村へ帰還したときの収支報告の受け渡し（消費型・セッション内のみ）。
/// Result/GameOver が Set し、VillagePresenter が初回Entryで TryConsume する
/// （ConsumeLastBuzzResult と同じパターン。セーブはしない=選ぶ前に終了したら流れる）。
/// </summary>
public static class VillageArrivalReport
{
    public static bool HasReport { get; private set; }
    public static bool Cleared { get; private set; }
    /// <summary>クリア時=最終純資産 / 破産時=手元に残った現金。</summary>
    public static int Earned { get; private set; }
    /// <summary>村資金へ変換された額。</summary>
    public static int Converted { get; private set; }

    public static void Set(bool cleared, int earned, int converted)
    {
        HasReport = true;
        Cleared = cleared;
        Earned = earned;
        Converted = converted;
    }

    public static bool TryConsume(out bool cleared, out int earned, out int converted)
    {
        cleared = Cleared;
        earned = Earned;
        converted = Converted;
        if (!HasReport) return false;
        HasReport = false;
        return true;
    }
}
