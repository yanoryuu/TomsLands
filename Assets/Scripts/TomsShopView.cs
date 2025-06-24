using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TomsShopView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;
    [SerializeField] private Button PurchaseButton;
    [SerializeField] private Button SetItemButton;
    
    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnSetItemClicked { get; } = new();

    public void Awake()
    {
        PurchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        // SetItemButton.onClick.AddListener(() => OnSetItemClicked.OnNext(Unit.Default));
    }
    
    public void UpdatePlayerMoney(int money)
    {
        playerMoneyText.text = $"Money: {money}G";
    }
    
    public void ShowTomsShopUI()
    {
        gameObject.SetActive(true);
    }

    public void HideTomsShopUI()
    {
        gameObject.SetActive(false);
    }
}
