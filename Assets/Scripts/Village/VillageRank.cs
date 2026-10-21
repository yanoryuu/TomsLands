using UnityEngine;

/// <summary>
/// 村の格（開拓地 → 村 → 町 → 王都。Docs/Village_Meta_Design.md §0-2）。
/// 総合Lv（6施設のレベルの合計）と領主館のレベルから毎回求める（セーブには持たない）。
/// 格が上がると長いランが選べるようになる（かんたん=開拓地・ふつう=村・むずかしい=町）。最後の格「王都」はご褒美だけ。
/// 条件と名前は GameConst.Village（rankNames / rankRequiredTotalLevels / rankRequiredHallLevels / modeRequiredRanks。配信値で調整可）。
/// </summary>
public static class VillageRank
{
    private static readonly string[] FallbackNames = { "開拓地", "村", "町", "王都" };

    private static VillageSettingsData Settings => GameConst.Village;

    /// <summary>格の数（名前と条件の配列の短いほうに合わせる。最低1）。</summary>
    public static int Count
    {
        get
        {
            var s = Settings;
            int names = s.rankNames != null && s.rankNames.Length > 0 ? s.rankNames.Length : FallbackNames.Length;
            int levels = s.rankRequiredTotalLevels != null && s.rankRequiredTotalLevels.Length > 0
                ? s.rankRequiredTotalLevels.Length
                : 1;
            return Mathf.Max(1, Mathf.Min(names, levels));
        }
    }

    /// <summary>いちばん上の格（王都）の番号。</summary>
    public static int TopRank => Count - 1;

    /// <summary>王都（いちばん上の格）の称号。セーブ欄とリザルトに出す。</summary>
    public const string TopTitle = "王都の商人";

    /// <summary>総合Lvと領主館Lvから格を求める（条件を満たすいちばん上の格。条件は下から順に満たす前提にしない）。</summary>
    public static int Compute(int totalLevel, int hallLevel)
    {
        int rank = 0;
        for (int i = 1; i < Count; i++)
        {
            if (Meets(i, totalLevel, hallLevel)) rank = i;
        }
        return rank;
    }

    /// <summary>施設レベルから格を求める。</summary>
    public static int Compute(VillageEffects levels) =>
        levels == null ? 0 : Compute(TotalLevel(levels), levels.HallLevel);

    /// <summary>メタ進行（metaData.json）の施設レベルから格を求める。</summary>
    public static int Compute(MetaProgressModel meta) => Compute(VillageEffects.From(meta));

    /// <summary>セーブ（metaData.json の facilities）から格を求める（タイトルのセーブ欄用。モデルを作らずに読む）。</summary>
    public static int Compute(System.Collections.Generic.IEnumerable<FacilityLevelPlain> facilities)
    {
        if (facilities == null) return 0;
        int total = 0, hall = 0;
        foreach (var f in facilities)
        {
            if (f == null || f.level <= 0) continue;
            switch (f.facilityId)
            {
                case VillageEffects.HallId: hall = f.level; total += f.level; break;
                case VillageEffects.GuildId:
                case VillageEffects.TavernId:
                case VillageEffects.BankId:
                case VillageEffects.ArtisanId:
                case VillageEffects.PlazaId:
                    total += f.level;
                    break;
            }
        }
        return Compute(total, hall);
    }

    /// <summary>6施設のレベルの合計（総合Lv）。</summary>
    public static int TotalLevel(VillageEffects levels) =>
        levels == null
            ? 0
            : levels.HallLevel + levels.GuildLevel + levels.TavernLevel + levels.BankLevel + levels.ArtisanLevel + levels.PlazaLevel;

    public static bool Meets(int rank, int totalLevel, int hallLevel) =>
        totalLevel >= RequiredTotalLevel(rank) && hallLevel >= RequiredHallLevel(rank);

    public static int RequiredTotalLevel(int rank) => Mathf.Max(0, At(Settings.rankRequiredTotalLevels, rank));

    public static int RequiredHallLevel(int rank) => Mathf.Max(0, At(Settings.rankRequiredHallLevels, rank));

    public static string Name(int rank)
    {
        rank = Mathf.Clamp(rank, 0, TopRank);
        var names = Settings.rankNames;
        string name = names != null && rank < names.Length ? names[rank] : null;
        if (string.IsNullOrEmpty(name)) name = rank < FallbackNames.Length ? FallbackNames[rank] : $"格{rank + 1}";
        return name;
    }

    public static bool IsTop(int rank) => rank >= TopRank;

    /// <summary>格になる条件の文（「総合Lv10・領主館Lv1」）。</summary>
    public static string ConditionText(int rank)
    {
        int total = RequiredTotalLevel(rank);
        int hall = RequiredHallLevel(rank);
        if (total >= MaxTotalLevel && hall <= 0) return $"総合Lv{total}（全施設Lv{MaxTotalLevel / FacilityCount}）";
        return hall > 0 ? $"総合Lv{total}・領主館Lv{hall}" : $"総合Lv{total}";
    }

    // 6施設 × 最大Lv5
    private const int FacilityCount = 6;
    private static int MaxTotalLevel => FacilityCount * Mathf.Max(1, VillageFacilityMigration.MaxFacilityLevel);

    // ───────── ランの長さの解放 ─────────

    /// <summary>このランの長さが選べるようになる格。</summary>
    public static int RequiredRank(GameModeId mode) => Mathf.Max(0, At(Settings.modeRequiredRanks, (int)mode));

    /// <summary>
    /// このランの長さを選べるか。村の格が足りていれば解放。足りなくても、その長さを一度クリアしていれば解放済みにする
    /// （bestProfits / runRecords。格の仕組みより前のセーブで長いランを遊んでいた人のため）。
    /// </summary>
    public static bool IsModeUnlocked(GameModeId mode, MetaProgressModel meta)
    {
        int rank = Compute(meta);
        return rank >= RequiredRank(mode) || HasCleared(mode, meta);
    }

    /// <summary>この長さのランを一度でもクリアしたか。</summary>
    public static bool HasCleared(GameModeId mode, MetaProgressModel meta)
    {
        if (meta == null) return false;
        if (meta.TryGetBestProfit(mode, out _)) return true;
        foreach (var r in meta.RunRecords)
        {
            if (r != null && r.cleared && r.mode == (int)mode) return true;
        }
        return false;
    }

    /// <summary>未解放のランのボタンに出す条件（「村が『町』になると解放（総合Lv10・領主館Lv1）」）。</summary>
    public static string ModeUnlockText(GameModeId mode)
    {
        int rank = RequiredRank(mode);
        return $"村が『{Name(rank)}』になると解放（{ConditionText(rank)}）";
    }

    /// <summary>selected が選べなければ、選べるうちで selected より短いいちばん長いランに丸める（かんたんは常に選べる）。</summary>
    public static GameModeId ClampToUnlocked(GameModeId selected, MetaProgressModel meta)
    {
        for (int m = (int)selected; m > 0; m--)
        {
            if (IsModeUnlocked((GameModeId)m, meta)) return (GameModeId)m;
        }
        return GameModeId.Short;
    }

    /// <summary>この格で選べるようになるランの長さ（無ければ false。王都はご褒美だけ）。</summary>
    public static bool TryGetUnlockedMode(int rank, out GameModeId mode)
    {
        var table = Settings.modeRequiredRanks;
        if (table != null)
        {
            for (int m = 0; m < table.Length; m++)
            {
                if (table[m] == rank && rank > 0)
                {
                    mode = (GameModeId)m;
                    return true;
                }
            }
        }
        mode = GameModeId.Short;
        return false;
    }

    private static int At(int[] table, int index)
    {
        if (table == null || index < 0 || index >= table.Length) return 0;
        return table[index];
    }
}
