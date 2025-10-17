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
    
    
    public void ShowPanel(GamePhase gamePhase)
    {
        titlePanel.SetActive(false);
        streamingPanel.SetActive(false);
        tomsShopPanel.SetActive(false);
        streamingSettingPanel.SetActive(false);
        mapPanel.SetActive(false);
        settingPanel.SetActive(false);
        blackSmithPanel.SetActive(false);
        toolShopPanel.SetActive(false);
        infoBrokerPanel.SetActive(false);
        endPhasePanel.SetActive(false);
        preparationPanel.SetActive(false);
        streamingResultPanel.SetActive(false);
        commonPanel.SetActive(false);
        
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
            //トムの店ここから色々な画面へ遷移
            case GamePhase.TomsShop:
                tomsShopPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            //鍛冶屋武器を仕入れる
            case GamePhase.BlackSmith:
                blackSmithPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            //道具屋で道具を仕入れる
            case GamePhase.ToolShop:
                toolShopPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            //情報屋
            case GamePhase.InfoBroker:
                infoBrokerPanel.SetActive(true);
                commonPanel.SetActive(true);
                break;
            case GamePhase.Map:
                mapPanel.SetActive(true);
                break;
            //配信(勇者がダンジョンの潜りながらリアルタイムで売買
            case GamePhase.Streaming:
                streamingPanel.SetActive(true);
                break;
            //配信の準備(品出しする武器を３つ選択）
            case GamePhase.StreamingSetting:
                streamingSettingPanel.SetActive(true);
                break;
            //配信画面のリザルト
            case GamePhase.StreamingResult:
                streamingResultPanel.SetActive(true);
                break;
            //音量などのシステム的な各所設定
            case GamePhase.Setting:
                settingPanel.SetActive(true);
                break;
            //リザルト画面
            case GamePhase.End:
                endPhasePanel.SetActive(true);
                break;
        }
    }
}
