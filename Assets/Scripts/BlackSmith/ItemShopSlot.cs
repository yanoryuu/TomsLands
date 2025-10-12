using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemShopSlot : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button pulusButton;
    [SerializeField] private Button infoButton;
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    
    public string itemId { get; private set; }
    
    private int maxQuantity;

    public Subject<string> OnPurchaseClicked { get; } = new();
    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    public Subject<string> OnInfoRequested { get; } = new();
    


    public void SetItem(string itemId, Sprite icon, int price,int maxStock, int stock, bool isPopular)
    {
        this.itemId = itemId;
        iconImage.sprite = icon;
        Debug.Log(itemId);
        priceText.text = $"{price} G";
        
        quantitySlider.onValueChanged.AddListener(v => OnDisplayQuantityChanged.OnNext((int)v));
        
        purchaseButton.onClick.AddListener(() =>OnPurchaseClicked.OnNext(itemId));
        
        pulusButton.onClick.AddListener(() =>
        {
            OnDisplayQuantityChanged.OnNext(ParseQuantity(1));
        });
        
        minusButton.onClick.AddListener(() =>
        {
            OnDisplayQuantityChanged.OnNext(ParseQuantity(-1));
        });
        
        infoButton.onClick.AddListener(() =>
        {
            OnInfoRequested.OnNext(itemId);
        });
    }
    private int ParseQuantity(int changeValue)
    {
        if(quantitySlider.value + changeValue < 0|| quantitySlider.value + changeValue > maxQuantity) return (int)quantitySlider.value;
        
        return (int)(quantitySlider.value + changeValue);
    }

    
    public void SetDisplayQuantity(int quantity)
    {
        quantitySlider.value = Mathf.Clamp(quantity, 0, maxQuantity);
        quantityText.text = quantity.ToString();
    }

    public void SetMaxDisplayQuantity(int maxQuantity)
    {
        quantitySlider.maxValue = maxQuantity;
        this.maxQuantity = maxQuantity;
    }

    public void SetPrice(float price)
    {
        priceText.text = price.ToString();
    }

    private int ParseQuantity()
    {
        return (int)quantitySlider.value;
    }
    
}