/// <summary>
/// ラン終了→村へ帰還したときの収支報告の受け渡し（消費型・セッション内のみ）。
/// Result/GameOver が Set し、VillagePresenter が初回Entryで TryConsume する
/// （ConsumeLastBuzzResult と同じパターン。セーブはしない=選ぶ前に終了したら流れる）。
/// </summary>
public static class VillageArrivalReport
{
    public static bool HasReport { get; private set; }
    public static bool Cleared { get; private set; }
    /// <summary>クリア時=このランの儲け / 破産時=そのランでいちばん儲かっていたときの儲け。</summary>
    public static int Earned { get; private set; }
    /// <summary>村資金へ変換された額。</summary>
    public static int Converted { get; private set; }
    /// <summary>村資金へ入った額の内訳（無ければ null。演出は内訳なしで流れる）。</summary>
    public static VillageFundsBreakdown Breakdown { get; private set; }

    public static void Set(bool cleared, int earned, int converted, VillageFundsBreakdown breakdown = null)
    {
        HasReport = true;
        Cleared = cleared;
        Earned = earned;
        Converted = converted;
        Breakdown = breakdown;
    }

    public static bool TryConsume(out bool cleared, out int earned, out int converted)
        => TryConsume(out cleared, out earned, out converted, out _);

    public static bool TryConsume(out bool cleared, out int earned, out int converted, out VillageFundsBreakdown breakdown)
    {
        cleared = Cleared;
        earned = Earned;
        converted = Converted;
        breakdown = Breakdown;
        if (!HasReport) return false;
        HasReport = false;
        Breakdown = null;
        return true;
    }
}
