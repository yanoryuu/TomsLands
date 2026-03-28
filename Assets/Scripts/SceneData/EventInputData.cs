using UnityEngine;

/// <summary>
/// EventScene へ渡す入力データ。
/// ScriptableObject なのでシーンをまたいでもデータが残る。
/// </summary>
[CreateAssetMenu(fileName = "EventInputData", menuName = "ScriptableObjects/SceneData/EventInputData")]
public class EventInputData : ScriptableObject
{
    [Header("イベントID")]
    public string EventId;

    [Header("イベントデータ（パース済み）")]
    public string EventTitle;
    public string EventDescription;

    [Header("コマンドデータ（JSON文字列で保持）")]
    public string CommandsJson;

    [Header("ゲームフロー状態")]
    [Tooltip("GameFlowManager の現在インデックス。シーン復帰時に復元するために使用")]
    public int GameFlowIndex;

    /// <summary>
    /// イベント遷移前にデータを書き込む
    /// </summary>
    public void Setup(string eventId, string title, string description, string commandsJson, int gameFlowIndex)
    {
        EventId = eventId;
        EventTitle = title;
        EventDescription = description;
        CommandsJson = commandsJson;
        GameFlowIndex = gameFlowIndex;
        Debug.Log($"[EventInputData] Setup: id={eventId}, title={title}, flowIndex={gameFlowIndex}");
    }

    public void Clear()
    {
        EventId = "";
        EventTitle = "";
        EventDescription = "";
        CommandsJson = "";
        GameFlowIndex = 0;
    }
}

