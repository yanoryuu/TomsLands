/// <summary>
/// 画面に出す時間の書き方。単位は「日」にそろえる（「ターン」「Turn」「Day」は画面に出さない）。
/// 次の配信までの日数は GameFlowManager.GetDaysUntilNextStream（今日が配信日なら 0）を渡す。
/// 配信の中の戦闘の手番（バトルログの「ターン」）はここを通さない。
/// </summary>
public static class DayText
{
    /// <summary>「3日目」。</summary>
    public static string DayOf(int day) => $"{day}日目";

    /// <summary>「あと3日」。</summary>
    public static string Remaining(int days) => $"あと{days}日";

    /// <summary>「3日」（期間・日数）。</summary>
    public static string Days(int days) => $"{days}日";

    /// <summary>次の配信まで: 0 は「本日配信」、1 以上は「あと3日」、配信が無い（負）ときは none。</summary>
    public static string UntilStream(int daysUntil, string none = "—")
        => daysUntil == 0 ? "本日配信" : daysUntil > 0 ? Remaining(daysUntil) : none;
}
