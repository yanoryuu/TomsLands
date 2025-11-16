using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;
using System;
using System.Collections.Generic;
using R3;

public class StreamingSalesController : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private float baseSalesInterval = 15f;
    [SerializeField] private float intervalRandomness = 3f;
    [SerializeField] private int maxItemsToSell = 5;

    [Header("参照")]
    [SerializeField] private StreamingSalesView view;

    [Header("Inventory (UI)")]
    [SerializeField] private List<ItemSlotView> inventorySlotViews; // 在庫側のスロット（シーン上の 10 インスタンス）
    private List<RuntimeItemData> inventorySlotItemRefs = new List<RuntimeItemData>(); // 在庫スロットが参照している RuntimeItemData

    private ItemModel _mainItemModel;
    private StreamingSalesPresenter _presenter;
    private StreamingSalesModel _model;

    // ## テスト用 ##
    private void Start()
    {
        Debug.LogWarning("--- テストモードで実行中 ---");

        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();

        var realItemModel = new ItemModel(masterItems);
        StartStreamingPhase(realItemModel);
    }

    public void StartStreamingPhase(ItemModel itemModel)
    {
        _mainItemModel = itemModel;
        var itemsInStock = _mainItemModel.PickItemRuntimeListForStock(_mainItemModel.RuntimeItems, 1);
        var itemsForSale = itemsInStock.Take(maxItemsToSell).ToList();

        _model = new StreamingSalesModel(baseSalesInterval, intervalRandomness);
        _model.SetItemsForSale(itemsForSale);

        _presenter = new StreamingSalesPresenter(_model, view);
        _presenter.Bind();

        ItemSlotView.OnItemDropped += HandleItemSwap;

        InitializeInventorySlots(itemModel, itemsForSale);

        _model.StartSalesLoopAsync(_mainItemModel, this.GetCancellationTokenOnDestroy()).Forget();
        Debug.Log($"営業開始！ {itemsForSale.Count}種類の商品を売りに出します。");
    }

    private void InitializeInventorySlots(ItemModel itemModel, List<RuntimeItemData> itemsForSale)
    {
        inventorySlotItemRefs = new List<RuntimeItemData>(new RuntimeItemData[inventorySlotViews.Count]);

        var candidates = itemModel.RuntimeItems.Where(r => !itemsForSale.Contains(r)).ToList();

        for (int i = 0; i < inventorySlotViews.Count; i++)
        {
            RuntimeItemData assign = i < candidates.Count ? candidates[i] : null;
            inventorySlotItemRefs[i] = assign;
            inventorySlotViews[i].SetItem(assign);
        }
    }

    private void HandleItemSwap(ItemSlotView fromSlot, ItemSlotView toSlot)
    {
        if (fromSlot == null || toSlot == null) return;

        int fromSellIndex = view.GetSlotIndex(fromSlot); // -1 if not sell slot
        int toSellIndex = view.GetSlotIndex(toSlot);

        int fromInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(fromSlot) : -1;
        int toInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(toSlot) : -1;

        // 1) 売り場内の入れ替え
        if (fromSellIndex != -1 && toSellIndex != -1)
        {
            Debug.Log($"売り場内スワップ: {fromSellIndex} <-> {toSellIndex}");
            _model.SwapItems(fromSellIndex, toSellIndex);
            return;
        }

        // 2) 在庫内の入れ替え
        if (fromInvIndex != -1 && toInvIndex != -1)
        {
            Debug.Log($"在庫内スワップ: {fromInvIndex} <-> {toInvIndex}");
            var tmp = inventorySlotItemRefs[toInvIndex];
            inventorySlotItemRefs[toInvIndex] = inventorySlotItemRefs[fromInvIndex];
            inventorySlotItemRefs[fromInvIndex] = tmp;

            inventorySlotViews[toInvIndex].SetItem(inventorySlotItemRefs[toInvIndex]);
            inventorySlotViews[fromInvIndex].SetItem(inventorySlotItemRefs[fromInvIndex]);
            return;
        }

        // 3) 売り場 <-> 在庫 の交換
        if ((fromSellIndex != -1 && toInvIndex != -1) || (toSellIndex != -1 && fromInvIndex != -1))
        {
            int sellIndex = fromSellIndex != -1 ? fromSellIndex : toSellIndex;
            int invIndex = fromInvIndex != -1 ? fromInvIndex : toInvIndex;

            Debug.Log($"売場 ⇄ 在庫 スワップ: sellIndex={sellIndex}, invIndex={invIndex}");

            var sellRef = _model.ItemsForSale.Count > sellIndex ? _model.ItemsForSale[sellIndex] : null;
            var invRef = inventorySlotItemRefs.Count > invIndex ? inventorySlotItemRefs[invIndex] : null;

            if (_model.ItemsForSale.Count > sellIndex)
                _model.ItemsForSale[sellIndex] = invRef;
            inventorySlotItemRefs[invIndex] = sellRef;

            view.DisplayItems(_model.ItemsForSale);

            for (int i = 0; i < inventorySlotViews.Count; i++)
            {
                inventorySlotViews[i].SetItem(inventorySlotItemRefs[i]);
            }

            _model.OnItemsReordered.OnNext(Unit.Default);

            return;
        }

        Debug.LogWarning("HandleItemSwap: どの領域でもないスワップが発生しました（無視）");
    }

    private void OnDestroy()
    {
        _presenter?.Dispose();
        ItemSlotView.OnItemDropped -= HandleItemSwap;
    }
}
