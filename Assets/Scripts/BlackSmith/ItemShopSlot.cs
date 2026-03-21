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
    [SerializeField] private Slider quantitySlider;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    [Header("Step Buttons")]
    [SerializeField] private Button plusButton;
    [SerializeField] private Button minusButton;

    [Header("Data")]
    public string itemId { get; private set; }

    // 通知系
    public Subject<int> OnDisplayQuantityChanged { get; } = new(); // Slider 由来の変更
    public Subject<int> OnStepClicked { get; } = new();            // +1 / -1 の要求
    public Subject<string> OnInfoRequested { get; } = new();
    public Subject<Unit> OnPurchaseClicked { get; } = new();

    // 内部状態（UI表示用）
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress; // UI更新時にイベントを抑止

    private void Awake()
    {
        if (quantitySlider)
        {
            quantitySlider.wholeNumbers = true; // 予約数は整数運用
            quantitySlider.onValueChanged.AddListener(OnSliderValueChanged);
        }

        infoButton?.onClick.AddListener(() => OnInfoRequested.OnNext(itemId));
        purchaseButton?.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));

        // ＋／－ボタン
        if (plusButton) plusButton.onClick.AddListener(() => OnStepClicked.OnNext(+1));
        if (minusButton) minusButton.onClick.AddListener(() => OnStepClicked.OnNext(-1));
    }

    public void SetItem(string itemId,string itemName, Sprite sprite, int price, int maxStock, int currentStock, bool isPopular)
    {
        this.itemId = itemId;
        if (icon) icon.sprite = sprite;
        if (nameText) nameText.text = itemName;
        SetPrice(price);
        stockText?.SetText($"{currentStock}");

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
        }
        quantityText?.SetText($"{displayQuantity}");

        suppress = false;
        UpdateButtonsInteractable();
    }

    public void SetCurrentStock(int currentStock)
    {
        stockText?.SetText($"{currentStock}");
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
        {
            SetDisplayQuantity(maxQuantity);
        }
        UpdateButtonsInteractable();
    }

    // === ユーザー操作によるスライダー変更 ===
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
        // 購入ボタン
        if (purchaseButton)
            purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;

        // ＋／－の活性状態（予約可能範囲に合わせて制御）
        if (plusButton)
            plusButton.interactable = displayQuantity < maxQuantity;
        if (minusButton)
            minusButton.interactable = displayQuantity > 0;
    }

    private void OnDestroy()
    {
        quantitySlider?.onValueChanged.RemoveListener(OnSliderValueChanged);
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
        plusButton?.onClick.RemoveAllListeners();
        minusButton?.onClick.RemoveAllListeners();
    }
}
