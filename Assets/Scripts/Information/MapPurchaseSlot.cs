using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MapPurchaseSlot : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    // 通知系
    public Subject<Unit> OnPurchaseClicked { get; } = new();

    // 内部状態（UI表示用）
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress; // UI更新時にイベントを抑止

    private void Awake()
    {
        purchaseButton?.onClick.AddListener(() => OnPurchaseClicked.OnNext(Unit.Default));
    }

    public void SetItem(string itemId,string itemName, Sprite sprite, int price, int currentStock)
    {
        if (icon) icon.sprite = sprite;
        if (nameText) nameText.text = itemName;
        SetPrice(price);
        stockText?.SetText($"所持: {currentStock}");
        UpdateButtonsInteractable();
    }

    public void SetPrice(int price)
    {
        priceText?.SetText($"{price}G");
    }
    
    // === 最大値変更（通知しない） ===
   
    // === ユーザー操作によるスライダー変更 ===
    private void OnSliderValueChanged(float value)
    {
        int next = Mathf.RoundToInt(value);
        if (suppress) return;
        if (next == displayQuantity) return;

        displayQuantity = next;
        quantityText?.SetText($"{displayQuantity}");
        UpdateButtonsInteractable();
    }

    private void UpdateButtonsInteractable()
    {
        // 購入ボタン
        if (purchaseButton)
            purchaseButton.interactable = displayQuantity > 0 && maxQuantity > 0;
    }

    private void OnDestroy()
    {
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
    }
}