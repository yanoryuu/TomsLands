using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using R3;

public class ItemShopView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;
    [SerializeField] private Transform itemListContent;
    [SerializeField] private GameObject itemPrefab;
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

    public void UpdatePlayerMoney(int money)
    {
        playerMoneyText.text = $"所持金: {money}G";
    }

    public void PopulateItemList(List<RuntimeItemData> items, ItemModel model)
    {
        foreach (Transform child in itemListContent)
        {
            Destroy(child.gameObject);
        }

        foreach (var item in items)
        {
            var master = model.GetMasterItem(item.ItemId);
            if (master == null) continue;

            var go = Instantiate(itemPrefab, itemListContent);
            var itemUI = go.GetComponent<ItemUI>();


            item.CurrentPrice
                .Subscribe(price => itemUI.UpdatePrice(price))
                .AddTo(itemUI);

            item.Stock
                .Subscribe(stock => itemUI.UpdateStock(stock))
                .AddTo(itemUI);

            itemUI.OnPurchaseClicked
                .Subscribe(_ => OnPurchaseRequested.OnNext((item.ItemId, 1)))
                .AddTo(itemUI);

            itemUI.OnSellClicked
                .Subscribe(_ => OnSellRequested.OnNext((item.ItemId, 1)))
                .AddTo(itemUI);
        }
    }
}