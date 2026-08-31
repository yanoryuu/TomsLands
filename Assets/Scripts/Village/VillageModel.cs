using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 村（メタ層）のモデル。施設レベルの参照と投資（レベルアップ）を担当する。
/// 状態の実体は MetaProgressModel（metaData.json）が持ち、本クラスはルール（領主館ゲート・費用）を司る。
/// 村と店の経営（ラン）は完全に別フロー: ラン中に本クラスは使われない。
/// </summary>
public class VillageModel
{
    public const string HallId = "hall";

    private readonly List<VillageFacilityData> facilities;
    private readonly MetaProgressModel metaProgress;

    public VillageModel(List<VillageFacilityData> facilities, MetaProgressModel metaProgress)
    {
        this.facilities = facilities ?? new List<VillageFacilityData>();
        this.metaProgress = metaProgress;
    }

    public IReadOnlyList<VillageFacilityData> Facilities => facilities;

    public int VillageFunds => metaProgress.VillageFunds;

    /// <summary>村の総合Lv（全施設レベルの合計）。</summary>
    public int VillageLevel => facilities.Sum(f => GetLevel(f.facilityId));

    public int HallLevel => GetLevel(HallId);

    public int GetLevel(string facilityId) => metaProgress.GetFacilityLevel(facilityId);

    public VillageFacilityData GetFacility(string facilityId) =>
        facilities.FirstOrDefault(f => f.facilityId == facilityId);

    /// <summary>次レベルへの費用（最大レベルなら -1）。</summary>
    public int GetNextCost(VillageFacilityData facility)
    {
        if (facility == null) return -1;
        int next = GetLevel(facility.facilityId) + 1;
        var entry = facility.GetLevel(next);
        return entry?.cost ?? -1;
    }

    /// <summary>
    /// 投資可否の判定。ゲート仕様:
    /// - Lv1の建設: 領主館Lv >= requiredHallLevel（祠=2、他は0）
    /// - Lv2以降: 領主館Lv >= 目標Lv-1（領主館Lv1で全施設のLv2解禁、Lv2でLv3解禁）
    /// - 領主館自身はゲートなし
    /// </summary>
    public (bool ok, string reason) CanInvest(VillageFacilityData facility)
    {
        if (facility == null) return (false, "施設データがありません");

        int current = GetLevel(facility.facilityId);
        int next = current + 1;
        if (next > facility.MaxLevel) return (false, "最大レベル");

        if (facility.facilityId != HallId)
        {
            if (next == 1 && HallLevel < facility.requiredHallLevel)
                return (false, $"領主館Lv{facility.requiredHallLevel}で建設可能");
            if (next >= 2 && HallLevel < next - 1)
                return (false, $"領主館Lv{next - 1}でLv{next}に拡張可能");
        }

        int cost = facility.GetLevel(next).cost;
        if (metaProgress.VillageFunds < cost)
            return (false, "村資金が足りない");

        return (true, "");
    }

    /// <summary>
    /// 投資を実行して施設レベルを1上げる。成功時は即セーブ（metaData.json）。
    /// </summary>
    public bool TryInvest(string facilityId, out string message)
    {
        var facility = GetFacility(facilityId);
        var (ok, reason) = CanInvest(facility);
        if (!ok)
        {
            message = reason;
            return false;
        }

        int next = GetLevel(facilityId) + 1;
        int cost = facility.GetLevel(next).cost;
        if (!metaProgress.TrySpendVillageFunds(cost))
        {
            message = "村資金が足りない";
            return false;
        }

        metaProgress.SetFacilityLevel(facilityId, next);
        metaProgress.SaveData();
        message = $"{facility.facilityName} が Lv{next} になった！";
        Debug.Log($"[Village] 投資: {facilityId} → Lv{next} ({cost}G, 残り村資金 {metaProgress.VillageFunds}G)");
        return true;
    }
}
