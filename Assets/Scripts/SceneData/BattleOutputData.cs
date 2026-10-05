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
        HasResult = true;
        Debug.Log($"[BattleOutputData] SetResult: result={result}, weapon={weaponId}, armor={armorId}, soldItems={soldItems.Count}, totalEarnings={totalEarnings}, mobs={DefeatedMobCount}, bosses={DefeatedBossCount}");
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

