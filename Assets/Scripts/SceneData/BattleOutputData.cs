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

    [Header("フラグ")]
    public bool HasResult;

    /// <summary>
    /// 戦闘終了時に結果を書き込む
    /// </summary>
    public void SetResult(BattleResult result, string weaponId, string armorId, List<BattleOutputSoldItem> soldItems)
    {
        Result = result;
        WeaponId = weaponId;
        ArmorId = armorId;
        SoldItems = new List<BattleOutputSoldItem>(soldItems);
        HasResult = true;
        Debug.Log($"[BattleOutputData] SetResult: result={result}, weapon={weaponId}, armor={armorId}, soldItems={soldItems.Count}");
    }

    public void Clear()
    {
        Result = default;
        WeaponId = "";
        ArmorId = "";
        SoldItems.Clear();
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
    public int SoldQuantity;
    public int SoldPrice;
}

