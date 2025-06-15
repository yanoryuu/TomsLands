using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using R3;

public class ItemShopView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;
    [SerializeField] private Transform itemListParent;
    [SerializeField] private GameObject itemShopSlotPrefab;
    // [SerializeField] private Button nextPhaseButton;

    public Subject<(string itemId, int quantity)> OnPurchaseRequested { get; } = new();
    public Subject<(string itemId, int quantity)> OnSellRequested { get; } = new();
    public Subject<Unit> OnNextPhaseRequested { get; } = new();

    private void Awake()
    {
        // nextPhaseButton.onClick.AddListener(() => OnNextPhaseRequested.OnNext(Unit.Default));
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
    public void PopulateItemList(List<RuntimeItemData> items)
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
                item.Stock.Value,
                item.IsPopular.Value
            );
            
            item.Stock.Subscribe(stock=>slot.UpdateStock(stock));
            
            item.CurrentPrice.Subscribe(price => slot.UpdatePrice(price));

            // 購入ボタンの処理（Presenter側で購読した方が良い場合はSubjectで通知）
            slot.OnPurchaseClicked
                .Subscribe(quantity => OnPurchaseRequested.OnNext((item.ItemId, quantity)))
                .AddTo(this);

            slot.OnSellClicked
                .Subscribe(quantity => OnSellRequested.OnNext((item.ItemId, quantity)))
                .AddTo(this);

            activeSlots.Add(slot);
        }
    }

    // 所持金更新表示
    public void UpdatePlayerMoney(int money)
    {
        playerMoneyText.text = $"{money} G";
    }
}