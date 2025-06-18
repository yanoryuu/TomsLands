using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemShopSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private GameObject popularIcon;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button sellButton;
    [SerializeField] private TMP_InputField quantityInput;

    public Subject<int> OnPurchaseClicked { get; } = new();
    public Subject<int> OnSellClicked { get; } = new();

    private string itemId;

    public void SetItem(string itemId, Sprite icon, int price, int stock, bool isPopular)
    {
        this.itemId = itemId;
        iconImage.sprite = icon;
        Debug.Log(icon.name);
        priceText.text = $"{price} G";
        stockText.text = $"在庫: {stock}";
        popularIcon.SetActive(isPopular);

        purchaseButton.onClick.AddListener(() =>
        {
            int quantity = ParseQuantity();
            OnPurchaseClicked.OnNext(quantity);
        });

        sellButton.onClick.AddListener(() =>
        {
            int quantity = ParseQuantity();
            OnSellClicked.OnNext(quantity);
        });
    }
    
    public void UpdateStock(int stock)
    {
        stockText.text = $"在庫: {stock}";
    }

    public void UpdatePrice(int price)
    {
        priceText.text = $"{price} G";
    }


    private int ParseQuantity()
    {
        if (int.TryParse(quantityInput.text, out int quantity))
        {
            return Mathf.Max(1, quantity);
        }
        return 1;
    }
}