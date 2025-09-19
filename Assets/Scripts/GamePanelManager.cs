using UnityEngine;

public class GamePanelManager : MonoBehaviour
{
    [SerializeField] private GameObject titlePanel;
    // [SerializeField] private GameObject preparationPanel;
    [SerializeField] private GameObject streamingPanel;
    [SerializeField] private GameObject tomsShopPanel;
    [SerializeField] private GameObject streamingSettingPanel;
    // [SerializeField] private GameObject endPhasePanel;
    
    
    public void ShowPanel(GamePhase gamePhase)
    {
        titlePanel.SetActive(false);
        streamingPanel.SetActive(false);
        tomsShopPanel.SetActive(false);
        streamingSettingPanel.SetActive(false);
        // endPhasePanel.SetActive(false);
        // preparationPanel.SetActive(false);
        
        switch (gamePhase)
        {
            case GamePhase.Title:
                titlePanel.SetActive(true);
                break;
            case GamePhase.Preparation:
                // preparationPanel.SetActive(true);
                break;
            case GamePhase.TomsShop:
                tomsShopPanel.SetActive(true);
                break;
            case GamePhase.Streaming:
                streamingPanel.SetActive(true);
                break;
            case GamePhase.StreamingSetting:
                streamingSettingPanel.SetActive(true);
                break;
            case GamePhase.End:
                // endPhasePanel.SetActive(true);
                break;
        }
    }
}
