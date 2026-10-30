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

    private void Awake()
    {
        // 配信中パネルのパッド/キー操作（介入カード・狙う敵・スロット持ち上げ）。シーン配置不要
        if (streamingPanel != null && !TryGetComponent<BattleGamepadController>(out _))
            gameObject.AddComponent<BattleGamepadController>().Initialize(streamingPanel);
    }

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
                // #1 ぽよんと開く（品出し画面は UI の矩形なので拡縮できる。表示状態は呼んだ時点で決まる）
                if (streamingSettingPanel != null) UIMotion.Open(streamingSettingPanel);
                break;

            case StreamingGamePhase.Streaming:
                if (streamingPanel != null) streamingPanel.SetActive(true);
                break;

            case StreamingGamePhase.StreamingResult:
                // 配信中・リザルトのルートは UI ではない Transform（戦闘のワールド物も含む）なので拡縮しない。
                // リザルトの開く演出は BattleResultView が自分の矩形で行う
                if (streamingResultPanel != null) streamingResultPanel.SetActive(true);
                break;
        }

        // 勇者の HP/MP は配信中パネルの外（シーン直下に生成）の重ね描き Canvas なので、配信中以外は隠す
        bool streaming = phase == StreamingGamePhase.Streaming;
        foreach (var character in FindObjectsByType<CharacterView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            character.SetOverlayStatusVisible(streaming);

        // パッド/キー操作中なら、新しく出たパネルへフォーカスを移す（マウス中は何もしない）
        var shown = phase switch
        {
            StreamingGamePhase.StreamingSetting => streamingSettingPanel,
            StreamingGamePhase.Streaming => streamingPanel,
            StreamingGamePhase.StreamingResult => streamingResultPanel,
            _ => null,
        };
        if (shown != null) UIFocusManager.NotifyPanelShown(shown);

        Debug.Log($"[BattlePanelManager] Panel switched to: {phase}");
    }
}

