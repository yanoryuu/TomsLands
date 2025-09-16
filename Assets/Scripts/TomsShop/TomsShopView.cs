using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TomsShopView : MonoBehaviour
{
    
    [SerializeField] private Button PurchaseButton;
    [SerializeField] private Button SetItemButton;
    [SerializeField] private GameObject TomsShopScreen;
    
    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnSetItemClicked { get; } = new();

    public void Awake()
    {
        PurchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        // SetItemButton.onClick.AddListener(() => OnSetItemClicked.OnNext(Unit.Default));
    }
    
    public void ShowTomsShopUI()
    {
        TomsShopScreen.SetActive(true);
    }

    public void HideTomsShopUI()
    {
        TomsShopScreen.SetActive(false);
    }
}
