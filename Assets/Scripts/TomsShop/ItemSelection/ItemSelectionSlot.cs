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
    [SerializeField] private Button selectButton;
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Button pulusButton;
    [SerializeField] private Button minusButton;
    [SerializeField] private Button infoButton;

    [Header("陳列中の視覚フィードバック")]
    [SerializeField] private Image slotBackground;
    [SerializeField] private Color displayingColor = new Color(0.6f, 1f, 0.6f, 1f);
    [SerializeField] private Color notDisplayingColor = new Color(0.8f, 0.8f, 0.8f, 1f);
    [SerializeField] private GameObject displayingIndicator;

    [Header("選択ボタンの視覚フィードバック")]
    [SerializeField] private Image selectButtonBackground;
    [SerializeField] private Color selectedButtonColor = new Color(0.4f, 0.8f, 0.4f, 1f);
    [SerializeField] private Color deselectedButtonColor = new Color(1f, 1f, 1f, 1f);

    public Subject<bool> OnToggleChanged { get; } = new();
    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    public Subject<string> OnInfoRequested { get; } = new();

    public string itemId { get; private set; }

    private int maxQuantity;
    private bool suppressEvents;
    private bool isSelected;

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

        selectButton.onClick.AddListener(() =>
        {
            if (suppressEvents) return;
            isSelected = !isSelected;
            OnToggleChanged.OnNext(isSelected);
            UpdateDisplayVisual(isSelected, (int)quantitySlider.value);
            UpdateSelectButtonVisual(isSelected);
        });
        quantitySlider.onValueChanged.AddListener(v =>
        {
            if (suppressEvents) return;
            OnDisplayQuantityChanged.OnNext((int)v);
            UpdateDisplayVisual(isSelected, (int)v);
        });
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
        suppressEvents = true;
        quantitySlider.value = Mathf.Clamp(quantity, 0, maxQuantity);
        quantityText.text = quantity.ToString();
        suppressEvents = false;
        UpdateDisplayVisual(isSelected, quantity);
    }

    public void SetMaxDisplayQuantity(int maxQuantity)
    {
        suppressEvents = true;
        quantitySlider.maxValue = maxQuantity;
        this.maxQuantity = maxQuantity;
        suppressEvents = false;
    }

    public void SetPrice(float price)
    {
        priceText.text = price.ToString();
    }

    public void SetSelectToggle(bool isOn)
    {
        suppressEvents = true;
        isSelected = isOn;
        suppressEvents = false;
        UpdateDisplayVisual(isOn, (int)quantitySlider.value);
        UpdateSelectButtonVisual(isOn);
    }

    private void UpdateDisplayVisual(bool isDisplaying, int quantity)
    {
        bool isActuallyDisplaying = isDisplaying && quantity > 0;
        if (slotBackground != null)
            slotBackground.color = isActuallyDisplaying ? displayingColor : notDisplayingColor;
        if (displayingIndicator != null)
            displayingIndicator.SetActive(isActuallyDisplaying);
    }

    private void UpdateSelectButtonVisual(bool isOn)
    {
        if (selectButtonBackground != null)
            selectButtonBackground.color = isOn ? selectedButtonColor : deselectedButtonColor;
    }

    public void SetStock(int stock)
    {
        if (stockText != null)
            stockText.text = $"{stock}";
    }
}