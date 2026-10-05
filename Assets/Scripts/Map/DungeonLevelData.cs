using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ダンジョンの1フェーズ分の敵構成。
/// フェーズ内の敵は最大3体まで同時に出現し、倒すと残りが順次補充される。
/// フェーズ内の敵を全て倒すと次のフェーズへ進む。
/// </summary>
[Serializable]
public class DungeonPhaseData
{
    [Tooltip("このフェーズで出現する敵（リスト順に最大3体ずつ出現、倒すと補充）。最終フェーズには isBoss の敵＝ボスを含められる")]
    public List<EnemyData> enemies = new List<EnemyData>();
}

/// <summary>
/// ダンジョンの各レベルにおける敵データ・報酬・レベルアップ費用を保持するクラス。
/// </summary>
[Serializable]
public class DungeonLevelData
{
    [Tooltip("フェーズ構成。全フェーズをクリアするとダンジョンクリア。未設定の場合は旧方式（monsters/bossName）から自動変換して戦闘する")]
    public List<DungeonPhaseData> phases = new List<DungeonPhaseData>();

    [Tooltip("【旧方式・表示用】このレベルで出現するモンスター一覧（ダンジョン情報画面・クリア確率計算で使用。戦闘は phases を優先）")]
    public List<EnemyData> monsters = new List<EnemyData>();

    [Tooltip("【旧方式】このレベルのボス名（phases 未設定時の自動変換で使用）")]
    public string bossName;

    [Tooltip("勇者が敗北した時（魔王軍勝利時）にプレイヤーが受け取る報酬ゴールド")]
    public int rewardGold;

    [Tooltip("このレベルから次のレベルへ上げるために必要なゴールド（最大レベルでは0でOK）")]
    public int levelUpCost;
}
