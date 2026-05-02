using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CommonView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;

    [SerializeField] private TextMeshProUGUI currentTurnText;

    [SerializeField] private Button menuButton;
    
    public Subject<Unit> OnMenuButtonClicked { get; } = new();
    
    
    private void Awake()    {
        menuButton.onClick.AddListener(() => OnMenuButtonClicked.OnNext(Unit.Default));
    }
    public void UpdatePlayerMoney(int money)
    {
        playerMoneyText.text = $"{money}G";
    }
    
    public void UpdateCurrentTurn(int turn)
    {
        currentTurnText.text = $"Turn: {turn}";
    }
}
