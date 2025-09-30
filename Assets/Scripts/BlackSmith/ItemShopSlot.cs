using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemShopSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Slider amountSlider;
    [SerializeField] private TextMeshProUGUI amountText;
    // [SerializeField] private GameObject popularIcon;
    // [SerializeField] private Button sellButton;
    // [SerializeField] private TMP_InputField quantityInput;

    public Subject<int> OnPurchaseClicked { get; } = new();
    public Subject<int> OnSellClicked { get; } = new();

    private string itemId;

    public void SetItem(string itemId, Sprite icon, int price,int maxStock, int stock, bool isPopular)
    {
        this.itemId = itemId;
        iconImage.sprite = icon;
        Debug.Log(itemId);
        priceText.text = $"{price} G";
        
        UpdateMaxStock(maxStock,stock);
        
        // stockText.text = $"在庫: {stock}";
        // popularIcon.SetActive(isPopular);

        purchaseButton.onClick.AddListener(() =>
        {
            int quantity = ParseQuantity();
            Debug.Log($"Item: {itemId}, Quantity: {quantity},Price: {price}");
            OnPurchaseClicked.OnNext(quantity);
        });
        
        amountText.text = $"{amountSlider.value} amount";
        
        amountSlider.onValueChanged.AsObservable()
            .Subscribe(v=>amountText.text = $"{v} amount"); 
        
        // sellButton.onClick.AddListener(() =>
        // {
        //     int quantity = ParseQuantity();
        //     OnSellClicked.OnNext(quantity);
        // });
    }
    
     public void UpdateStock(int stock)
     {
         stockText.text = $"Stock: {stock}";
     }

    public void UpdatePrice(int price)
    {
        priceText.text = $"{price} G";
    }

    public void UpdateMaxStock(int maxStock,int stock)
    {
        amountSlider.maxValue = maxStock - stock;
    }


    /*private int ParseQuantity()
    {
        if (int.TryParse(quantityInput.text, out int quantity))
        {
            return Mathf.Max(1, quantity);
        }
        return 1;
    }*/

    private int ParseQuantity()
    {
        return (int)amountSlider.value;
    }
    
}