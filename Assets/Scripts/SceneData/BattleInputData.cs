using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BattleScene へ渡す入力データ。
/// ScriptableObject なのでシーンをまたいでもデータが残る。
/// </summary>
[CreateAssetMenu(fileName = "BattleInputData", menuName = "ScriptableObjects/SceneData/BattleInputData")]
public class BattleInputData : ScriptableObject
{
    [Header("ダンジョン情報")]
    public DungeonName DungeonKey;

    [Tooltip("現在のダンジョンレベル（1～5）")]
    public int DungeonLevel = DungeonLevels.Unfunded;

    [Header("勇者の装備")]
    public List<string> EquippedItemIds = new();

    [Header("持ち込みアイテム（配信設定で選んだもの）")]
    public List<BattleInputItem> SelectedItems = new();

    [Header("ゲームフロー状態")]
    [Tooltip("GameFlowManager の現在インデックス。シーン復帰時に復元するために使用")]
    public int GameFlowIndex;

    /// <summary>
    /// 戦闘前にデータを書き込む
    /// </summary>
    public void Setup(DungeonName dungeonKey, int dungeonLevel, List<string> equippedItemIds, List<BattleInputItem> selectedItems, int gameFlowIndex)
    {
        DungeonKey = dungeonKey;
        DungeonLevel = Mathf.Clamp(dungeonLevel, DungeonLevels.Unfunded, 5); // Lv0 = 未出資（戦闘は Lv1 と同じ・配当なし）
        EquippedItemIds = new List<string>(equippedItemIds);
        SelectedItems = new List<BattleInputItem>(selectedItems);
        GameFlowIndex = gameFlowIndex;
        Debug.Log($"[BattleInputData] Setup: dungeon={dungeonKey}, level={DungeonLevel}, equipped={equippedItemIds.Count}, items={selectedItems.Count}, flowIndex={gameFlowIndex}");
    }

    public void Clear()
    {
        DungeonKey = default;
        DungeonLevel = DungeonLevels.Unfunded;
        EquippedItemIds.Clear();
        SelectedItems.Clear();
        GameFlowIndex = 0;
    }
}

/// <summary>
/// 配信設定で選択したアイテムの軽量データ
/// </summary>
[Serializable]
public class BattleInputItem
{
    public string ItemId;
    public int Quantity;
    public int Price;
}

