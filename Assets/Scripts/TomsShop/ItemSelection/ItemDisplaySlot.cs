using UnityEngine;
using UnityEngine.UI;
using TMPro;
using R3;

public class ItemDisplaySlot : MonoBehaviour
{
    [SerializeField] private Image itemIconImage;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button removeButton;

    public Subject<string> OnRemoveRequested { get; } = new();
    public string ItemId { get; private set; }

    public void SetItem(string itemId, Sprite icon, int quantity)
    {
        ItemId = itemId;
        itemIconImage.sprite = icon;
        SetQuantity(quantity);

        if (removeButton != null)
            removeButton.onClick.AddListener(() => OnRemoveRequested.OnNext(ItemId));
    }

    public void SetQuantity(int quantity)
    {
        quantityText.text = quantity.ToString();
    }
}
