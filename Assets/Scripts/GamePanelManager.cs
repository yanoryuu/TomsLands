using UnityEngine;

public class GamePanelManager : MonoBehaviour
{
    [SerializeField] private GameObject tomsShopPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private GameObject blackSmithPanel;
    [SerializeField] private GameObject toolShopPanel;
    [SerializeField] private GameObject infoBrokerPanel;
    [SerializeField] private GameObject endPhasePanel;
    [SerializeField] private GameObject commonPanel;
    [SerializeField] private GameObject turnEndSummaryPanel;
    [SerializeField] private GameObject eventPanel;

    /// <summary>
    /// 全パネルを非表示にする
    /// </summary>
    private void HideAll()
    {
        tomsShopPanel.SetActive(false);
        blackSmithPanel.SetActive(false);
        toolShopPanel.SetActive(false);
        infoBrokerPanel.SetActive(false);
        mapPanel.SetActive(false);
        endPhasePanel.SetActive(false);
        settingPanel.SetActive(false);
        commonPanel.SetActive(false);
        turnEndSummaryPanel.SetActive(false);
        eventPanel.SetActive(false);
    }

    /// <summary>
    /// メインフェーズ切替時に呼ばれる。大枠のパネルを制御。
    /// TomsShopに入った場合、サブフェーズ側でさらにパネルが切り替わる。
    /// </summary>
    public void ShowPanel(GamePhase gamePhase)
    {
        HideAll();

        switch (gamePhase)
        {
            //トムの店（サブフェーズ側で細かいパネルを切り替える）
            case GamePhase.TomsShop:
                // サブフェーズのShowTomsShopPanelで個別パネルを表示
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
            case TomsShopGamePhase.TurnEndSummary:
                turnEndSummaryPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
        }
    }

    /// <summary>
    /// イベントポップアップパネルを表示する
    /// </summary>
    public void ShowEventPanel()
    {
        if (eventPanel != null)
        {
            eventPanel.SetActive(true);
        }
    }

    /// <summary>
    /// イベントポップアップパネルを非表示にする
    /// </summary>
    public void HideEventPanel()
    {
        if (eventPanel != null)
        {
            eventPanel.SetActive(false);
        }
    }
}
