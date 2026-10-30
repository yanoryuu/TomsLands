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
    /// 配信開始時（配信の日の朝に配信を決めた時点）のクリア見込み（ClearProbabilityCalculator の表示％）。-1 = 分からない。
    /// 計測ログ（BalanceLog の clearPct・estimateTier）に使う。アセットには保存しない。
    /// </summary>
    [NonSerialized] public float PreStreamClearPct = -1f;

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
        PreStreamClearPct = -1f; // 呼んだ側が見込みを計算して入れる（GameFlowManager.PrepareBattleInput）
        Debug.Log($"[BattleInputData] Setup: dungeon={dungeonKey}, level={DungeonLevel}, equipped={equippedItemIds.Count}, items={selectedItems.Count}, flowIndex={gameFlowIndex}");
    }

    public void Clear()
    {
        DungeonKey = default;
        DungeonLevel = DungeonLevels.Unfunded;
        EquippedItemIds.Clear();
        SelectedItems.Clear();
        GameFlowIndex = 0;
        PreStreamClearPct = -1f;
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

