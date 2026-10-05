using UnityEngine;

/// <summary>
/// タイトルシーンからTomsShopシーンへ開始モードを受け渡すためのScriptableObject。
/// シーンをまたいでもデータが保持される。
/// </summary>
[CreateAssetMenu(fileName = "StartModeData", menuName = "ScriptableObjects/SceneData/StartModeData")]
public class StartModeData : ScriptableObject
{
    public StartMode Mode = StartMode.NewGame;

    // --- 自動生成フローの選択（タイトルUIで設定） ---
    [Header("ゲームフロー自動生成")]
    public GameModeId SelectedMode = GameModeId.Short;
    public bool UseAutoGeneration = true;

    public void SetNewGame()
    {
        Mode = StartMode.NewGame;
    }

    public void SetContinue()
    {
        Mode = StartMode.Continue;
    }

    /// <summary>タイトルUIからの選択を反映する。</summary>
    public void SetFlowSelection(GameModeId mode, bool useAuto)
    {
        SelectedMode = mode;
        UseAutoGeneration = useAuto;
    }
}

public enum StartMode
{
    NewGame,
    Continue
}

