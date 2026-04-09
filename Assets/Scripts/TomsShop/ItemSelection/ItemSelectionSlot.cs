using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemSelectionSlot : MonoBehaviour
{
    [SerializeField] private Image itemIconImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private Toggle selectToggle;
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Button pulusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button infoButton;

    public Subject<bool> OnToggleChanged { get; } = new();
    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    
    public Subject<string> OnInfoRequested { get; } = new();

    public string itemId { get;private set; }
    
    private int maxQuantity;

    public void SetItem(string itemId, Sprite icon, Sprite background, string name, int price, int stock)
    {
        this.itemId = itemId;
        itemIconImage.sprite = icon;
        if (backgroundImage)
        {
            if (background != null)
            {
                backgroundImage.sprite = background;
                backgroundImage.enabled = true;
            }
            else
            {
                backgroundImage.enabled = false;
            }
        }
        itemNameText.text = name;
        priceText.text = $"{price} G";
        SetStock(stock);
        
        selectToggle.onValueChanged.AddListener(isOn => OnToggleChanged.OnNext(isOn));
        quantitySlider.onValueChanged.AddListener(v => OnDisplayQuantityChanged.OnNext((int)v));
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

    public void SetSelectToggle(bool isOn)
    {
        selectToggle.isOn = isOn;
    }

    public void SetStock(int stock)
    {
        if (stockText != null)
            stockText.text = $"{stock}";
    }
}