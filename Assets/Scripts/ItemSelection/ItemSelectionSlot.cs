using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemSelectionSlot : MonoBehaviour
{
    [SerializeField] private Image itemIconImage;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private Toggle selectToggle;
    [SerializeField] private TMP_InputField quantityInput;

    public Subject<bool> OnToggleChanged { get; } = new();
    public Subject<int> OnQuantityChanged { get; } = new();

    private string itemId;

    public void SetItem(string itemId, Sprite icon, string name, int price, int stock)
    {
        this.itemId = itemId;
        itemIconImage.sprite = icon;
        itemNameText.text = name;
        priceText.text = $"{price} G";
        stockText.text = $"在庫: {stock}";

        quantityInput.text = "1"; // デフォルト
        quantityInput.onValueChanged.AddListener(OnQuantityInputChanged);
        selectToggle.onValueChanged.AddListener(isOn => OnToggleChanged.OnNext(isOn));
    }

    private void OnQuantityInputChanged(string input)
    {
        if (int.TryParse(input, out int quantity))
        {
            OnQuantityChanged.OnNext(quantity);
        }
    }
}