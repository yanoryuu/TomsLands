using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemShopSlot : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Slider quantitySlider;      // ← InputFieldの代わり
    [SerializeField] private TextMeshProUGUI quantityText; // ← 現在値表示用（任意）
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    [Header("Data")]
    public string itemId { get; private set; }

    public Subject<int> OnDisplayQuantityChanged { get; } = new();
    public Subject<string> OnInfoRequested { get; } = new();
    public Subject<Unit> OnPurchaseClicked { get; } = new();

    private int displayQuantity;
    private int maxQuantity;
    private bool suppress;

    private void Awake()
    {
        // Slider変更イベント
        quantitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        infoButton.onClick.AddListener(() => OnInfoRequested.OnNext(itemId));
        purchaseButton.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
    }

    public void SetItem(string itemId, Sprite sprite, int price, int maxStock, int currentStock, bool isPopular)
    {
        this.itemId = itemId;
        if (icon) icon.sprite = sprite;
        if (nameText) nameText.text = itemId;
        SetPrice(price);
        stockText?.SetText($"所持: {currentStock}");
        SetMaxDisplayQuantity(Mathf.Max(0, maxStock));
        SetDisplayQuantity(0);
        UpdateButtonsInteractable();
    }

    public void SetPrice(int price)
    {
        priceText?.SetText($"{price}G");
    }

    // === 表示だけ更新（通知しない） ===
    public void SetDisplayQuantity(int q)
    {
        suppress = true;
        displayQuantity = Mathf.Clamp(q, 0, maxQuantity);
        if (quantitySlider)
        {
            quantitySlider.value = displayQuantity;
            quantityText?.SetText($"{displayQuantity}");
        }
        suppress = false;
        UpdateButtonsInteractable();
    }

    // === 最大値変更（通知しない） ===
    public void SetMaxDisplayQuantity(int max)
    {
        maxQuantity = Mathf.Max(0, max);
        if (quantitySlider)
        {
            quantitySlider.maxValue = maxQuantity;
        }
        if (displayQuantity > maxQuantity)
            SetDisplayQuantity(maxQuantity);
        UpdateButtonsInteractable();
    }

    // === ユーザー操作による変更 ===
    private void OnSliderValueChanged(float value)
    {
        int next = Mathf.RoundToInt(value);
        if (suppress) return;
        if (next == displayQuantity) return;

        displayQuantity = next;
        quantityText?.SetText($"{displayQuantity}");
        OnDisplayQuantityChanged.OnNext(displayQuantity);
        UpdateButtonsInteractable();
    }

    private void UpdateButtonsInteractable()
    {
        if (purchaseButton)
            purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;
    }

    private void OnDestroy()
    {
        quantitySlider?.onValueChanged.RemoveListener(OnSliderValueChanged);
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
    }
}