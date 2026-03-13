using UnityEngine;

public class GamePanelManager : MonoBehaviour
{
    [SerializeField] private GameObject titlePanel;
    [SerializeField] private GameObject preparationPanel;
    [SerializeField] private GameObject streamingPanel;
    [SerializeField] private GameObject tomsShopPanel;
    [SerializeField] private GameObject streamingSettingPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private GameObject blackSmithPanel;
    [SerializeField] private GameObject toolShopPanel;
    [SerializeField] private GameObject infoBrokerPanel;
    [SerializeField] private GameObject endPhasePanel;
    [SerializeField] private GameObject streamingResultPanel;
    [SerializeField] private GameObject commonPanel;

    /// <summary>
    /// 全パネルを非表示にする
    /// </summary>
    private void HideAll()
    {
        titlePanel.SetActive(false);
        preparationPanel.SetActive(false);
        tomsShopPanel.SetActive(false);
        blackSmithPanel.SetActive(false);
        toolShopPanel.SetActive(false);
        infoBrokerPanel.SetActive(false);
        mapPanel.SetActive(false);
        streamingSettingPanel.SetActive(false);
        streamingPanel.SetActive(false);
        streamingResultPanel.SetActive(false);
        endPhasePanel.SetActive(false);
        settingPanel.SetActive(false);
        commonPanel.SetActive(false);
    }

    /// <summary>
    /// メインフェーズ切替時に呼ばれる。大枠のパネルを制御。
    /// TomsShop/Streamingに入った場合、サブフェーズ側でさらにパネルが切り替わる。
    /// </summary>
    public void ShowPanel(GamePhase gamePhase)
    {
        HideAll();

        switch (gamePhase)
        {
            //タイトル
            case GamePhase.Title:
                titlePanel.SetActive(true);
                break;
            //ゲーム開始前の準備（前回の報酬で初めのブースト選択など
            case GamePhase.Preparation:
                preparationPanel.SetActive(true);
                break;
            //トムの店（サブフェーズ側で細かいパネルを切り替える）
            case GamePhase.TomsShop:
                // サブフェーズのShowTomsShopPanelで個別パネルを表示
                break;
            //配信（サブフェーズ側で細かいパネルを切り替える）
            case GamePhase.Streaming:
                // サブフェーズのShowStreamingPanelで個別パネルを表示
                break;
            //リザルト画面
            case GamePhase.Result:
                endPhasePanel.SetActive(true);
                break;
            //音量などのシステム的な各所設定
            case GamePhase.Setting:
                settingPanel.SetActive(true);
                break;
        }
    }

    /// <summary>
    /// トムの店サブフェーズ切替時に呼ばれる
    /// </summary>
    public void ShowTomsShopPanel(TomsShopGamePhase subPhase)
    {
        HideAll();

        switch (subPhase)
        {
            case TomsShopGamePhase.Shop:
                tomsShopPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.BlackSmith:
                blackSmithPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.ToolShop:
                toolShopPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Broker:
                infoBrokerPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Map:
                mapPanel.SetActive(true);
                break;
        }
    }

    /// <summary>
    /// 配信サブフェーズ切替時に呼ばれる
    /// </summary>
    public void ShowStreamingPanel(StreamingGamePhase subPhase)
    {
        HideAll();

        switch (subPhase)
        {
            case StreamingGamePhase.StreamingSetting:
                streamingSettingPanel.SetActive(true);
                break;
            case StreamingGamePhase.Streaming:
                streamingPanel.SetActive(true);
                break;
            case StreamingGamePhase.StreamingResult:
                streamingResultPanel.SetActive(true);
                break;
        }
    }
}
