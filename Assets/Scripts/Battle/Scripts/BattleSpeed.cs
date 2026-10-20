using UnityEngine;

/// <summary>
/// 配信画面の「×1 / ×2」切り替え（SUMMARY M-11）。プレイヤーが選んだ倍率は端末単位で覚える（PlayerPrefs）。
/// 戦闘のテンポ（BattleUIView）と時間で回る販売ループ（StreamingSalesModel）の両方を同じ比で縮めるので、
/// ×2 にしても1回の配信で売れる回数・相場の動き方は変わらない（短くなるだけ）。
/// </summary>
public static class BattleSpeed
{
    private const string PrefsKey = "Battle.SpeedMultiplier";
    public const float Normal = 1f;
    public const float Fast = 2f;

    private static float? _cached;

    /// <summary>今の倍率（1 か 2）。</summary>
    public static float Multiplier
    {
        get
        {
            _cached ??= PlayerPrefs.GetFloat(PrefsKey, Normal) >= Fast ? Fast : Normal;
            return _cached.Value;
        }
        private set
        {
            _cached = value;
            PlayerPrefs.SetFloat(PrefsKey, value);
            PlayerPrefs.Save();
        }
    }

    public static bool IsFast => Multiplier >= Fast;

    /// <summary>×1 ⇔ ×2 を切り替えて、新しい倍率を返す。</summary>
    public static float Toggle()
    {
        Multiplier = IsFast ? Normal : Fast;
        return Multiplier;
    }

    /// <summary>ボタンの表示（×1 / ×2）。</summary>
    public static string Label => IsFast ? "×2" : "×1";
}
