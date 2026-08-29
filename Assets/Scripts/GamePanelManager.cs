using UnityEngine;

public class GamePanelManager : MonoBehaviour
{
    [SerializeField] private GameObject tomsShopPanel;
    [SerializeField] private GameObject heroPanel;
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private GameObject blackSmithPanel;
    [SerializeField] private GameObject toolShopPanel;
    [SerializeField] private GameObject infoBrokerPanel;
    [SerializeField] private GameObject endPhasePanel;
    [SerializeField] private GameObject commonPanel;
    [SerializeField] private GameObject turnEndSummaryPanel;
    [SerializeField] private GameObject dungeonLevelUpPanel;
    [SerializeField] private GameObject eventPanel;
    [SerializeField] private GameObject advertisementPanel;
    [SerializeField] private GameObject prophetPanel;
    [SerializeField] private GameObject shopUpgradePanel;
    [SerializeField] private GameObject machineShopPanel;

    /// <summary>
    /// 全パネルを非表示にする
    /// </summary>
    private void HideAll()
    {
        tomsShopPanel.SetActive(false);
        if (heroPanel != null) heroPanel.SetActive(false);
        blackSmithPanel.SetActive(false);
        toolShopPanel.SetActive(false);
        infoBrokerPanel.SetActive(false);
        mapPanel.SetActive(false);
        endPhasePanel.SetActive(false);
        settingPanel.SetActive(false);
        commonPanel.SetActive(false);
        turnEndSummaryPanel.SetActive(false);
        dungeonLevelUpPanel.SetActive(false);
        eventPanel.SetActive(false);
        if (advertisementPanel != null) advertisementPanel.SetActive(false);
        if (prophetPanel != null) prophetPanel.SetActive(false);
        if (shopUpgradePanel != null) shopUpgradePanel.SetActive(false);
        if (machineShopPanel != null) machineShopPanel.SetActive(false);
    }

    /// <summary>パネルを表示し、開き演出（フェードイン）を再生する。</summary>
    private static void ShowWithFx(GameObject panel)
    {
        if (panel == null) return;
        panel.SetActive(true);
        UIFx.PanelOpen(panel);
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
                ShowWithFx(endPhasePanel);
                break;
            //音量などのシステム的な各所設定
            case GamePhase.Setting:
                ShowWithFx(settingPanel);
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
                ShowWithFx(tomsShopPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Hero:
                ShowWithFx(heroPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.BlackSmith:
                // 鍛冶屋はCommonView(Turn/所持金/メニュー)と自前UIが重なるため出さない。
                // 所持金は鍛冶屋専用表示（BlackSmithView.playerMoneyText）が担う。
                ShowWithFx(blackSmithPanel);
                break;
            case TomsShopGamePhase.ToolShop:
                ShowWithFx(toolShopPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Broker:
                ShowWithFx(infoBrokerPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Map:
                ShowWithFx(mapPanel);
                break;
            case TomsShopGamePhase.DungeonLevelUp:
                ShowWithFx(dungeonLevelUpPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.TurnEndSummary:
                ShowWithFx(turnEndSummaryPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Advertisement:
                ShowWithFx(advertisementPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.Prophet:
                ShowWithFx(prophetPanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.ShopUpgrade:
                ShowWithFx(shopUpgradePanel);
                commonPanel.SetActive(true);
                break;
            case TomsShopGamePhase.MachineShop:
                ShowWithFx(machineShopPanel);
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
            ShowWithFx(eventPanel);
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
