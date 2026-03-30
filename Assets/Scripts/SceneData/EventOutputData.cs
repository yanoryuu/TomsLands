using UnityEngine;

/// <summary>
/// EventScene から返すイベント結果データ。
/// ScriptableObject なのでシーンをまたいでもデータが残る。
/// </summary>
[CreateAssetMenu(fileName = "EventOutputData", menuName = "ScriptableObjects/SceneData/EventOutputData")]
public class EventOutputData : ScriptableObject
{
    [Header("フラグ")]
    public bool HasResult;

    [Header("ゲームフロー状態")]
    public int GameFlowIndex;

    /// <summary>
    /// イベント完了時に結果を書き込む
    /// </summary>
    public void SetResult(int gameFlowIndex)
    {
        GameFlowIndex = gameFlowIndex;
        HasResult = true;
        Debug.Log($"[EventOutputData] SetResult: flowIndex={gameFlowIndex}");
    }

    public void Clear()
    {
        HasResult = false;
        GameFlowIndex = 0;
    }
}

