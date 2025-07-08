using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using R3;
using UnityEngine.InputSystem.Composites;

public class ItemShopView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;
    public Transform BlackSmithWeaponParent;
    public Transform BlackSmithArmorParent;
    public Transform ToolParent;

    public GameObject BlackSmithWeaponPanel;
    public GameObject BlackSmithArmorPanel;
    public GameObject ToolPanel;
    
    [SerializeField] private GameObject itemShopSlotPrefab;
    [SerializeField] private Button closeButton;

    [SerializeField] private Button weaponButton;
    [SerializeField] private Button armorButton;
    [SerializeField] private Button toolButton;

    public Subject<(string itemId, int quantity)> OnPurchaseRequested { get; } = new();
    // public Subject<(string itemId, int quantity)> OnSellRequested { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnWeaponPanelRequested { get; } = new();
    public Subject<Unit> OnArmorPanelRequested { get; } = new();
    public Subject<Unit> OnToolPanelRequested { get; } = new();

    private void Awake()
    {
        closeButton.onClick.AddListener(() =>OnCloseRequested.OnNext(Unit.Default));
        weaponButton.onClick.AddListener(()=>OnWeaponPanelRequested.OnNext(Unit.Default));
        armorButton.onClick.AddListener(()=>OnArmorPanelRequested.OnNext(Unit.Default));
        // toolButton.onClick.AddListener(()=>OnToolPanelRequested.OnNext(Unit.Default));
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private readonly List<ItemShopSlot> activeSlots = new();
    
    // 店舗の商品リストをUIに並べる
    public void PopulateItemList(List<RuntimeItemData> items,Transform itemListParent)
    {
        Debug.Log("PopulateItemList");
        Debug.Log(items.Count);
        // 既存UIをクリア
        foreach (var slot in activeSlots)
        {
            Destroy(slot.gameObject);
        }
        activeSlots.Clear();

        // 新しいアイテムリストをUIに生成
        foreach (var item in items)
        {
            Debug.Log($"Adding item: {item}");
            GameObject obj = Instantiate(itemShopSlotPrefab, itemListParent);
            var slot = obj.GetComponent<ItemShopSlot>();

            slot.SetItem(
                item.ItemId,
                item.ItemIcon,
                item.CurrentPrice.Value,
                item.MaxStock.Value,
                item.Stock.Value,
                item.IsPopular.Value
            );
            
            item.Stock.Subscribe(stock=>slot.UpdateStock(stock));
            
            item.CurrentPrice.Subscribe(price => slot.UpdatePrice(price));

            // 購入ボタンの処理（Presenter側で購読した方が良い場合はSubjectで通知）
            slot.OnPurchaseClicked
                .Subscribe(quantity =>
                {
                    OnPurchaseRequested.OnNext((item.ItemId, quantity));
                    PopulateItemList(items, itemListParent);
                })
                .AddTo(this);

            // slot.OnSellClicked
            //     .Subscribe(quantity => OnSellRequested.OnNext((item.ItemId, quantity)))
            //     .AddTo(this);

            activeSlots.Add(slot);
        }
    }
}