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
    [SerializeField] private Button infoButton;
    [SerializeField] private Button purchaseButton;

    // 通知系
    public Subject<DungeonName> OnPurchaseClicked { get; } = new();

    // 内部状態（UI表示用）
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress;
    private DungeonName dungeonKey;

    private void Awake()
    {
        purchaseButton?.onClick.AddListener(() => OnPurchaseClicked.OnNext(dungeonKey));
    }

    public void SetMapInfo(DungeonName dungeonName,string itemName, Sprite sprite, int price)
    {
        if (icon) icon.sprite = sprite;
        if (nameText) nameText.text = itemName;
        dungeonKey = dungeonName;
        SetPrice(price);
    }

    private void SetPrice(int price)
    {
        priceText?.SetText($"{price}G");
    }

    private void OnDestroy()
    {
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
    }
}