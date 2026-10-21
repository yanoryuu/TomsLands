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

    [Header("魔王側の配当（勇者敗北時の防衛報酬）")]
    [Tooltip("防衛報酬（レリック補正込み）。純利益（TotalEarnings）には反映済み。0 なら防衛報酬なし")]
    public int DefenseReward;
    [Tooltip("配信したダンジョン（防衛報酬の受取累計をダンジョンごとに記録する）")]
    public DungeonName DungeonKey;

    /// <summary>魔王側に張って当たった（勇者が負け、防衛報酬を受け取った）か。</summary>
    public bool IsDemonPayout => Result == BattleResult.Defeat && DefenseReward > 0;

    [Header("村の広場 Lv5「勝負配信の宣言」")]
    [Tooltip("この配信で宣言した側（None = 宣言なし）")]
    public StreamDeclarationSide DeclarationSide;
    [Tooltip("宣言の決着（帰還処理がフォロワー・注目と回数に反映する）")]
    public StreamDeclarationOutcome DeclarationOutcome;
    [Tooltip("宣言が当たって上乗せした額。純利益（TotalEarnings）には反映済み")]
    public int DeclarationBonus;

    [Header("フラグ")]
    public bool HasResult;
    [Tooltip("配信が不具合（例外）で途中終了したか。配信終了ボタンでの途中終了は false（呪われた帳簿の取り立てに数える）")]
    public bool AbortedByError;

    /// <summary>勝負配信の宣言の決着を書き込む。SetResult の後に呼ぶ。</summary>
    public void SetDeclaration(StreamDeclarationSide side, StreamDeclarationOutcome outcome, int bonus)
    {
        DeclarationSide = side;
        DeclarationOutcome = outcome;
        DeclarationBonus = Mathf.Max(0, bonus);
    }

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
        DefenseReward = 0;
        SetDeclaration(StreamDeclarationSide.None, StreamDeclarationOutcome.None, 0);
        AbortedByError = false;
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

    /// <summary>防衛報酬と配信先を書き込む。SetResult の後に呼ぶ。</summary>
    public void SetDefenseReward(int defenseReward, DungeonName dungeonKey)
    {
        DefenseReward = Mathf.Max(0, defenseReward);
        DungeonKey = dungeonKey;
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
        DefenseReward = 0;
        SetDeclaration(StreamDeclarationSide.None, StreamDeclarationOutcome.None, 0);
        AbortedByError = false;
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
    /// <summary>品出し時の単価（配信中は価格が動くので、売上の計算には Revenue を使う）</summary>
    public int SoldPrice;
    /// <summary>この商品の配信中の売上合計（売れたときの価格 × 個数の合計。補充分も含む）</summary>
    public int Revenue;
}

