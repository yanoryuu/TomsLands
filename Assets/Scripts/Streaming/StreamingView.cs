using TMPro;
using UnityEngine;

public class StreamingView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI stealthMarketingCostText;
    
    public void SetStealthMarketingCost(int cost){
        stealthMarketingCostText.text = $"cost: {cost}G";
    }
    
    public void ShowStreamingUI()
    {
        
    }
}
