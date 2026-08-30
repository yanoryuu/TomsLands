using System;
using UnityEngine;

/// <summary>
/// 店レベルのレベルテーブル。
/// 店レベルは「同時に陳列できる銘柄数（水平方向）」を規定する。
/// ※ 鍛冶屋レベルは「何を仕入れられるか（requiredLevel の垂直解放）」で役割分離。
/// machineSlots はマシン設置（店カスタマイズ）用の設置枠数。
/// </summary>
[CreateAssetMenu(fileName = "ShopLevelSettings", menuName = "ScriptableObjects/ShopLevelSettings")]
public class ShopLevelSettings : ScriptableObject
{
    [Serializable]
    public class ShopLevelEntry
    {
        [Tooltip("同時に陳列できる銘柄数（SKU数）の上限")]
        public int maxDisplayKinds = 3;
        [Tooltip("【廃止】1銘柄あたりの陳列個数の上限。個数制限は撤廃した（制限は銘柄数のみ）。旧アセット/配信データ互換のためフィールドだけ残す")]
        public int maxDisplayStockPerItem = 5;
        [Tooltip("このレベルへ上がるための費用（Lv1 の行は 0）")]
        public int levelUpCost = 0;
        [Tooltip("マシン設置枠の数（店カスタマイズ用）")]
        public int machineSlots = 0;
    }

    [Tooltip("index 0 = Lv1。配列の長さが最大レベル。")]
    public ShopLevelEntry[] levels =
    {
        new ShopLevelEntry { maxDisplayKinds = 3,  maxDisplayStockPerItem = 5,  levelUpCost = 0,      machineSlots = 1 },
        new ShopLevelEntry { maxDisplayKinds = 5,  maxDisplayStockPerItem = 8,  levelUpCost = 4000,   machineSlots = 2 },
        new ShopLevelEntry { maxDisplayKinds = 7,  maxDisplayStockPerItem = 12, levelUpCost = 10000,  machineSlots = 3 },
        new ShopLevelEntry { maxDisplayKinds = 9,  maxDisplayStockPerItem = 16, levelUpCost = 22000,  machineSlots = 4 },
        new ShopLevelEntry { maxDisplayKinds = 12, maxDisplayStockPerItem = 24, levelUpCost = 45000,  machineSlots = 6 },
    };

    public int MaxLevel => levels != null ? levels.Length : 1;

    /// <summary>1始まりのレベルからエントリを取得（範囲外はクランプ）。</summary>
    public ShopLevelEntry GetEntry(int level)
    {
        if (levels == null || levels.Length == 0) return new ShopLevelEntry();
        int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
        return levels[index];
    }

    /// <summary>現在レベル → 次レベルへの費用。最大レベルなら -1。</summary>
    public int GetLevelUpCost(int currentLevel)
    {
        if (currentLevel >= MaxLevel) return -1;
        return GetEntry(currentLevel + 1).levelUpCost;
    }
}
