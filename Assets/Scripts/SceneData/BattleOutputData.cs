using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleScene から返す戦闘結果データ。
/// ScriptableObject なのでシーンをまたいでもデータが残る。
/// </summary>
[CreateAssetMenu(fileName = "BattleOutputData", menuName = "ScriptableObjects/SceneData/BattleOutputData")]
public class BattleOutputData : ScriptableObject
{
    [Header("戦闘結果")]
    public BattleResult Result;

    [Header("装備ID（ボーナス/ペナルティ用）")]
    public string WeaponId;
    public string ArmorId;

    [Header("販売結果")]
    public List<BattleOutputSoldItem> SoldItems = new();
    public int TotalEarnings;

    [Header("撃破結果")]
    public int DefeatedMobCount;
    public int DefeatedBossCount;

    [Header("配信（介入・視聴者）")]
    [Tooltip("介入で払った総額（返金前）。純利益（TotalEarnings）には反映済み")]
    public int InterventionSpending;
    [Tooltip("未実行のまま配信が終わった介入の返金額。純利益には反映済み")]
    public int InterventionRefund;
    [Tooltip("この配信の最大同接")]
    public int PeakViewers;
    [Tooltip("視聴者スパチャの件数（収入には含めない）")]
    public int ViewerSuperChatCount;
    [Tooltip("必殺技の回数（プレイヤー・視聴者の赤スパ起点の合計。予約ベース）")]
    public int SpecialMoveCount;

    /// <summary>リザルトの「介入」1行に出す額（純支出 = 支出 − 返金）。</summary>
    public int InterventionNetSpending => InterventionSpending - InterventionRefund;

    [Header("フラグ")]
    public bool HasResult;

    /// <summary>
    /// 戦闘終了時に結果を書き込む
    /// </summary>
    public void SetResult(BattleResult result, string weaponId, string armorId, List<BattleOutputSoldItem> soldItems, int totalEarnings, int defeatedMobCount = 0, int defeatedBossCount = 0)
    {
        Result = result;
        WeaponId = weaponId;
        ArmorId = armorId;
        SoldItems = new List<BattleOutputSoldItem>(soldItems);
        TotalEarnings = totalEarnings;
        DefeatedMobCount = Mathf.Max(0, defeatedMobCount);
        DefeatedBossCount = Mathf.Max(0, defeatedBossCount);
        // 配信集計は SetStreamingStats で上書きする（呼ばれなければ 0 のまま）
        InterventionSpending = 0;
        InterventionRefund = 0;
        PeakViewers = 0;
        ViewerSuperChatCount = 0;
        SpecialMoveCount = 0;
        HasResult = true;
        Debug.Log($"[BattleOutputData] SetResult: result={result}, weapon={weaponId}, armor={armorId}, soldItems={soldItems.Count}, totalEarnings={totalEarnings}, mobs={DefeatedMobCount}, bosses={DefeatedBossCount}");
    }

    /// <summary>配信（介入・視聴者）の集計を書き込む。SetResult の後に呼ぶ。</summary>
    public void SetStreamingStats(int interventionSpending, int interventionRefund, int peakViewers, int viewerSuperChatCount, int specialMoveCount)
    {
        InterventionSpending = Mathf.Max(0, interventionSpending);
        InterventionRefund = Mathf.Max(0, interventionRefund);
        PeakViewers = Mathf.Max(0, peakViewers);
        ViewerSuperChatCount = Mathf.Max(0, viewerSuperChatCount);
        SpecialMoveCount = Mathf.Max(0, specialMoveCount);
        Debug.Log($"[BattleOutputData] SetStreamingStats: intervention={InterventionSpending} refund={InterventionRefund} peakViewers={PeakViewers} superChats={ViewerSuperChatCount} specials={SpecialMoveCount}");
    }

    public void Clear()
    {
        Result = default;
        WeaponId = "";
        ArmorId = "";
        SoldItems.Clear();
        TotalEarnings = 0;
        DefeatedMobCount = 0;
        DefeatedBossCount = 0;
        InterventionSpending = 0;
        InterventionRefund = 0;
        PeakViewers = 0;
        ViewerSuperChatCount = 0;
        SpecialMoveCount = 0;
        HasResult = false;
    }
}

/// <summary>
/// 戦闘中に売れたアイテムの結果
/// </summary>
[Serializable]
public class BattleOutputSoldItem
{
    public string ItemId;
    /// <summary>実際に売れた総数（バトル中の補充分も含む。リザルト表示用）</summary>
    public int SoldQuantity;
    /// <summary>ショップ在庫から減らす数（持ち込み数が上限。補充分は店在庫に無いため含めない）</summary>
    public int SoldFromStock;
    public int SoldPrice;
}

