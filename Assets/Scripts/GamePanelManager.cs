using UnityEngine;

public class GamePanelManager : MonoBehaviour
{
    [SerializeField] private GameObject tomsShopPanel;
    [SerializeField] private GameObject heroPanel;
    [SerializeField] private GameObject mapPanel;
    // 旧・未使用: 設定は加算シーン Setting.unity を開く方式に移った（CommonPresenter）。GamePhase.Setting へ遷移する所は無い
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private GameObject blackSmithPanel;
    // 旧・未使用: 道具屋は作らない（2026-10-20）。中身の無い ToolShop.prefab を指している
    [SerializeField] private GameObject toolShopPanel;
    [SerializeField] private GameObject infoBrokerPanel;
    // 旧・未使用: ラン終了は ResultScene へ抜ける（GameFlowManager.TransitionToResult）。GamePhase.Result へ遷移する所は無い
    [SerializeField] private GameObject endPhasePanel;
    [SerializeField] private GameObject commonPanel;
    [SerializeField] private GameObject turnEndSummaryPanel;
    [SerializeField] private GameObject dungeonLevelUpPanel;
    [SerializeField] private GameObject eventPanel;
    [SerializeField] private GameObject advertisementPanel;
    [SerializeField] private GameObject prophetPanel;
    [SerializeField] private GameObject shopUpgradePanel;
    [SerializeField] private GameObject machineShopPanel;
    [SerializeField] private GameObject newspaperPanel;

    /// <summary>
    /// 全パネルを非表示にする
    /// </summary>
    private void HideAll()
    {
        tomsShopPanel.SetActive(false);
        if (heroPanel != null) heroPanel.SetActive(false);
        blackSmithPanel.SetActive(false);
        if (toolShopPanel != null) toolShopPanel.SetActive(false);
        infoBrokerPanel.SetActive(false);
        mapPanel.SetActive(false);
        if (endPhasePanel != null) endPhasePanel.SetActive(false);
        if (settingPanel != null) settingPanel.SetActive(false);
        commonPanel.SetActive(false);
        turnEndSummaryPanel.SetActive(false);
        dungeonLevelUpPanel.SetActive(false);
        eventPanel.SetActive(false);
        if (advertisementPanel != null) advertisementPanel.SetActive(false);
        if (prophetPanel != null) prophetPanel.SetActive(false);
        if (shopUpgradePanel != null) shopUpgradePanel.SetActive(false);
        if (machineShopPanel != null) machineShopPanel.SetActive(false);
        if (newspaperPanel != null) newspaperPanel.SetActive(false);
    }

    // 全画面の切替は旧パネルを即座に消すので、フェードを 0 から始めると背景（カメラの背景色）が一瞬抜けて見える。
    // 少し見えた状態から始めてチラつきを抑える
    private const float SwitchFadeFrom = 0.35f;

    /// <summary>パネルを表示し、開き演出（フェードイン）を再生する。</summary>
    private static void ShowWithFx(GameObject panel)
    {
        if (panel == null) return;
        panel.SetActive(true);
        UIFx.PanelOpen(panel, 0.18f, SwitchFadeFrom);
        // パッド/キー操作時のフォーカスをこのパネルへ（フェード中でも入る）。マウス操作時は何もしない
        UIFocusManager.NotifyPanelShown(panel);
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
        _commonWasActive = commonPanel != null && commonPanel.activeSelf;
        HideAll();

        switch (subPhase)
        {
            case TomsShopGamePhase.Shop:
                ShowWithFx(tomsShopPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.Hero:
                ShowWithFx(heroPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.BlackSmith:
                // 鍛冶屋はCommonView(Turn/所持金/メニュー)と自前UIが重なるため出さない。
                // 所持金は鍛冶屋専用表示（BlackSmithView.playerMoneyText）が担う。
                ShowWithFx(blackSmithPanel);
                break;
            case TomsShopGamePhase.ToolShop:
                // 道具屋は作らない。万一ここへ来ても空の画面に閉じ込めないよう、店の画面を出す
                Debug.LogWarning("[GamePanelManager] 道具屋（ToolShop）は未使用です。店の画面を表示します。");
                ShowWithFx(tomsShopPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.Broker:
                // 情報屋も鍛冶屋と同レイアウトのためCommonViewを出さない（所持金は専用表示）
                ShowWithFx(infoBrokerPanel);
                break;
            case TomsShopGamePhase.Map:
                ShowWithFx(mapPanel);
                break;
            case TomsShopGamePhase.DungeonLevelUp:
                ShowWithFx(dungeonLevelUpPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.TurnEndSummary:
                ShowWithFx(turnEndSummaryPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.Advertisement:
                ShowWithFx(advertisementPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.Prophet:
                ShowWithFx(prophetPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.ShopUpgrade:
                ShowWithFx(shopUpgradePanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.MachineShop:
                ShowWithFx(machineShopPanel);
                ShowCommonPanel();
                break;
            case TomsShopGamePhase.Newspaper:
                // 朝刊は鍛冶屋・情報屋と同じ全画面レイアウト。
                // 所持金とターンは題字バーが持つので CommonView は出さない。
                ShowWithFx(newspaperPanel);
                break;
        }
    }

    private bool _commonWasActive;

    /// <summary>
    /// HUD（所持金・ターン・メニュー）を出す。切替の前から出ていたなら出しっぱなし（毎回フェードしてチラつかせない）、
    /// 新しく出すときだけ本体のパネルと同じフェードを掛ける（HUD だけ先にパッと出ないように）。
    /// </summary>
    private void ShowCommonPanel()
    {
        if (commonPanel == null) return;
        commonPanel.SetActive(true);
        if (!_commonWasActive) UIFx.PanelOpen(commonPanel, 0.18f, SwitchFadeFrom);
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
