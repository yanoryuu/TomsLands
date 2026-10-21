using System.Collections.Generic;
using System.Text;

/// <summary>
/// 村の施設を13から6つに統合したときの、既存セーブの読み替え（2026-10）。
/// - 統合した施設（骨董品店・祠→冒険者ギルド／工房区画→職人組合／印刷所・訓練所→広場）:
///   統合先のレベル = max(統合先, 統合元)。統合先のレベルを上げなかった分（すでに統合先の方が高い等）は返金する
/// - 削除した施設（倉庫・街道整備・農場）: 投資した額を返金する
/// - 最大レベルを超えた施設: 超えた分を返金する（2026-10-21 に全施設の最大を Lv5 にしたので、旧セーブで超えるものは無い。
///   以前は領主館の最大を2にしていたが、Lv5 化で領主館 Lv3〜5 が建つようになったため上限を5にそろえた。
///   ここを Lv3 のまま残すと、建てた Lv4・Lv5 が読み込みのたびに Lv3 へ戻されてしまう）
/// 返金額は旧マスターシートの建設費（配信していた値）で数える。
/// 統合元の行は消すので、2回目以降の読み込みでは何もしない（冪等）。
/// </summary>
public static class VillageFacilityMigration
{
    /// <summary>統合元 → 統合先（null = 削除して返金）。</summary>
    private static readonly Dictionary<string, string> MergeTo = new()
    {
        { "antique", VillageEffects.GuildId },
        { "shrine", VillageEffects.GuildId },
        { "workshop", VillageEffects.ArtisanId },
        { "press", VillageEffects.PlazaId },
        { "training", VillageEffects.PlazaId },
        { "warehouse", null },
        { "road", null },
        { "farm", null },
    };

    /// <summary>統合後の各施設の最大レベル（超えた分は返金）。施設データ（Resources_moved/Village）の段数と合わせる。</summary>
    public const int MaxFacilityLevel = 5;

    private static readonly Dictionary<string, int> MaxLevels = new()
    {
        { VillageEffects.HallId, MaxFacilityLevel },
        { VillageEffects.GuildId, MaxFacilityLevel },
        { VillageEffects.TavernId, MaxFacilityLevel },
        { VillageEffects.BankId, MaxFacilityLevel },
        { VillageEffects.ArtisanId, MaxFacilityLevel },
        { VillageEffects.PlazaId, MaxFacilityLevel },
    };

    /// <summary>旧マスターシート villageFacilities の建設費（index 0 = Lv1）。</summary>
    private static readonly Dictionary<string, int[]> OldCosts = new()
    {
        { "hall", new[] { 1000000, 4000000, 10000000 } },
        { "guild", new[] { 1000000, 2500000, 5000000 } },
        { "antique", new[] { 4500000, 2500000 } },
        { "shrine", new[] { 3000000 } },
        { "bank", new[] { 5000000, 12000000, 22000000 } },
        { "warehouse", new[] { 6000000, 14000000, 24000000 } },
        { "road", new[] { 2000000, 6000000, 13000000 } },
        { "press", new[] { 4000000, 9000000 } },
        { "artisan", new[] { 7000000, 16000000 } },
        { "tavern", new[] { 7000000, 16000000 } },
        { "workshop", new[] { 6000000, 15000000 } },
        { "farm", new[] { 3000000, 7000000, 14000000 } },
        { "training", new[] { 5000000, 12000000 } },
    };

    /// <summary>読み替えが必要なら facilityLevels を書き換えて true。refund は村資金へ戻す額。</summary>
    public static bool Apply(List<FacilityLevelPlain> facilityLevels, out int refund, out string log)
    {
        refund = 0;
        log = "";
        if (facilityLevels == null || facilityLevels.Count == 0) return false;

        var sb = new StringBuilder();
        bool changed = false;
        long total = 0;

        // 1) 統合・削除
        for (int i = facilityLevels.Count - 1; i >= 0; i--)
        {
            var entry = facilityLevels[i];
            if (entry == null || !MergeTo.TryGetValue(entry.facilityId, out var dest)) continue;
            changed = true;
            facilityLevels.RemoveAt(i);
            int oldLevel = entry.level;
            if (oldLevel <= 0) continue;

            if (dest == null)
            {
                long paid = CostOf(entry.facilityId, 0, oldLevel);
                total += paid;
                sb.Append($"{entry.facilityId} Lv{oldLevel} を返金 {paid:N0}G / ");
                continue;
            }

            var destEntry = Find(facilityLevels, dest);
            int destLevel = destEntry?.level ?? 0;
            int max = MaxLevels.TryGetValue(dest, out var m) ? m : oldLevel;
            int newLevel = System.Math.Min(System.Math.Max(destLevel, oldLevel), max);
            if (newLevel > destLevel)
            {
                if (destEntry == null) facilityLevels.Add(new FacilityLevelPlain { facilityId = dest, level = newLevel });
                else destEntry.level = newLevel;
                sb.Append($"{entry.facilityId} Lv{oldLevel} → {dest} Lv{newLevel} / ");
            }
            else
            {
                long paid = CostOf(entry.facilityId, 0, oldLevel);
                total += paid;
                sb.Append($"{entry.facilityId} Lv{oldLevel} は {dest} Lv{destLevel} に含まれるため返金 {paid:N0}G / ");
            }
        }

        // 2) 最大レベルを超えた分
        foreach (var entry in facilityLevels)
        {
            if (entry == null || !MaxLevels.TryGetValue(entry.facilityId, out int max) || entry.level <= max) continue;
            long paid = CostOf(entry.facilityId, max, entry.level);
            total += paid;
            sb.Append($"{entry.facilityId} Lv{entry.level} → Lv{max}（返金 {paid:N0}G） / ");
            entry.level = max;
            changed = true;
        }

        refund = (int)System.Math.Min(total, int.MaxValue);
        log = sb.ToString().TrimEnd(' ', '/');
        return changed;
    }

    /// <summary>fromLevel を超えて toLevel まで上げたときの旧建設費の合計。</summary>
    private static long CostOf(string facilityId, int fromLevel, int toLevel)
    {
        if (!OldCosts.TryGetValue(facilityId, out var costs)) return 0;
        long sum = 0;
        for (int lv = fromLevel + 1; lv <= toLevel && lv <= costs.Length; lv++) sum += costs[lv - 1];
        return sum;
    }

    private static FacilityLevelPlain Find(List<FacilityLevelPlain> list, string id)
    {
        foreach (var e in list)
            if (e != null && e.facilityId == id) return e;
        return null;
    }
}
