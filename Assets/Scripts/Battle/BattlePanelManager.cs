using UnityEngine;

/// <summary>
/// FightScene 内のパネル表示/非表示を一元管理するクラス。
/// TomsShop の GamePanelManager と同じパターン。
/// StreamingGamePhase に応じて適切なパネルを切り替える。
/// </summary>
public class BattlePanelManager : MonoBehaviour
{
    [Header("配信準備パネル（品出し設定画面）")]
    [SerializeField] private GameObject streamingSettingPanel;

    [Header("配信中パネル（戦闘 + 売買画面）")]
    [SerializeField] private GameObject streamingPanel;

    [Header("配信リザルトパネル（結果画面）")]
    [SerializeField] private GameObject streamingResultPanel;

    /// <summary>
    /// 全パネルを非表示にする
    /// </summary>
    private void HideAll()
    {
        if (streamingSettingPanel != null) streamingSettingPanel.SetActive(false);
        if (streamingPanel != null) streamingPanel.SetActive(false);
        if (streamingResultPanel != null) streamingResultPanel.SetActive(false);
    }

    /// <summary>
    /// 指定された StreamingGamePhase に応じてパネルを切り替える。
    /// </summary>
    public void ShowPanel(StreamingGamePhase phase)
    {
        HideAll();

        switch (phase)
        {
            case StreamingGamePhase.StreamingSetting:
                if (streamingSettingPanel != null) streamingSettingPanel.SetActive(true);
                break;

            case StreamingGamePhase.Streaming:
                if (streamingPanel != null) streamingPanel.SetActive(true);
                break;

            case StreamingGamePhase.StreamingResult:
                if (streamingResultPanel != null) streamingResultPanel.SetActive(true);
                break;
        }

        Debug.Log($"[BattlePanelManager] Panel switched to: {phase}");
    }
}

