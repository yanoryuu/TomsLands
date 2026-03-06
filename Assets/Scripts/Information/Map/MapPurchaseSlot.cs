using System;
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
    public Subject<DungeonName> OnPurchaseClicked { get; } = new();

    // 内部状態（UI表示用）
    private int displayQuantity;
    private int maxQuantity;
    private bool suppress; // UI更新時にイベントを抑止
    public DungeonName mapId{get; private set;}

    private void Awake()
    {
        purchaseButton?.onClick.AddListener(() => OnPurchaseClicked.OnNext(mapId));
    }

    public void SetMap(DungeonData dungeonData)
    {
        if (icon) icon.sprite = dungeonData.dungeonImage;
        if (nameText) nameText.text = dungeonData.dungeonName;
        mapId = dungeonData.key;
        
        //TODO: ターンの概念を追加したらここで設定
        SetPrice(dungeonData.GetPurchasePrice(1));
    }

    public void SetPrice(int price)
    {
        priceText?.SetText($"{price}G");
    }

    private void OnDestroy()
    {
        infoButton?.onClick.RemoveAllListeners();
        purchaseButton?.onClick.RemoveAllListeners();
    }
}