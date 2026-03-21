using UnityEngine;

/// <summary>
/// タイトルシーンからTomsShopシーンへ開始モードを受け渡すためのScriptableObject。
/// シーンをまたいでもデータが保持される。
/// </summary>
[CreateAssetMenu(fileName = "StartModeData", menuName = "ScriptableObjects/SceneData/StartModeData")]
public class StartModeData : ScriptableObject
{
    public StartMode Mode = StartMode.NewGame;

    public void SetNewGame()
    {
        Mode = StartMode.NewGame;
    }

    public void SetContinue()
    {
        Mode = StartMode.Continue;
    }
}

public enum StartMode
{
    NewGame,
    Continue
}

