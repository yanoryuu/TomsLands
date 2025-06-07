using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button sellButton;

    public Subject<Unit> OnPurchaseClicked { get; } = new();
    public Subject<Unit> OnSellClicked { get; } = new();

    public void SetItem(string name, int price, int stock)
    {
        nameText.text = name;
        UpdatePrice(price);
        UpdateStock(stock);
    }

    public void UpdatePrice(int price)
    {
        priceText.text = $"{price}G";
    }

    public void UpdateStock(int stock)
    {
        stockText.text = $"在庫: {stock}";
    }

    private void Awake()
    {
        purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
        sellButton.onClick.AddListener(() => OnSellClicked.OnNext(Unit.Default));
    }
}