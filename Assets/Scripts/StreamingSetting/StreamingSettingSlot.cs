using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StreamingSettingSlot : MonoBehaviour
{
    [SerializeField] private Image itemIconImage;
    [SerializeField] private TextMeshProUGUI priceText;
    [SerializeField] private TextMeshProUGUI stockText;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private Slider amountSlider;
    public Subject<int> OnQuantityChanged { get; } = new();

    private string itemId;

    public void SetItem(string itemId, Sprite icon, string name, int price, int stock)
    {
        this.itemId = itemId;
        itemIconImage.sprite = icon;
        priceText.text = $"{price} G";
        stockText.text = $"在庫: {stock}";
        
        amountText.text = $"{amountSlider.value} amount";
        
        amountSlider.onValueChanged.AsObservable()
            .Subscribe(v=>amountText.text = $"{v} amount");
    }

    private void OnQuantityInputChanged(string input)
    {
        if (int.TryParse(input, out int quantity))
        {
            OnQuantityChanged.OnNext(quantity);
        }
    }
}
