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

    [Header("陳列中の視覚フィードバック")]
    [SerializeField] private Image slotBackground;
    [SerializeField] private Color displayingColor = new Color(0.6f, 1f, 0.6f, 1f);    // 陳列中: 緑っぽい色
    [SerializeField] private Color notDisplayingColor = new Color(0.8f, 0.8f, 0.8f, 1f); // 非陳列: グレー
    [SerializeField] private GameObject displayingIndicator; // 「陳列中」表示用オブジェクト（任意）

    public Subject<bool> OnToggleChanged { get; } = new();
    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    
    public Subject<string> OnInfoRequested { get; } = new();

    public string itemId { get;private set; }
    
    private int maxQuantity;
    private bool suppressEvents;

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

        // ToggleGroupから外してほかの場所クリックでリセットされるのを防止
        selectToggle.group = null;
        // Navigationを無効化してフォーカス移動によるリセットを防止
        selectToggle.navigation = new Navigation { mode = Navigation.Mode.None };
        
        selectToggle.onValueChanged.AddListener(isOn =>
        {
            if (suppressEvents) return;
            OnToggleChanged.OnNext(isOn);
            UpdateDisplayVisual(isOn, (int)quantitySlider.value);
        });
        quantitySlider.onValueChanged.AddListener(v =>
        {
            if (suppressEvents) return;
            OnDisplayQuantityChanged.OnNext((int)v);
            UpdateDisplayVisual(selectToggle.isOn, (int)v);
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
        UpdateDisplayVisual(selectToggle.isOn, quantity);
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
        selectToggle.isOn = isOn;
        suppressEvents = false;
        UpdateDisplayVisual(isOn, (int)quantitySlider.value);
    }

    /// <summary>
    /// 陳列中かどうかの視覚表示を更新（トグルON かつ 数量>0 で陳列中とみなす）
    /// </summary>
    private void UpdateDisplayVisual(bool isDisplaying, int quantity)
    {
        bool isActuallyDisplaying = isDisplaying && quantity > 0;
        if (slotBackground != null)
        {
            slotBackground.color = isActuallyDisplaying ? displayingColor : notDisplayingColor;
        }
        if (displayingIndicator != null)
        {
            displayingIndicator.SetActive(isActuallyDisplaying);
        }
    }

    public void SetStock(int stock)
    {
        if (stockText != null)
            stockText.text = $"{stock}";
    }
}