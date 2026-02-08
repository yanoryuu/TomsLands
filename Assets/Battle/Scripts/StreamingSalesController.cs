using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;
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
    
        // 空リストで開始
        StartStreamingPhase(realItemModel, new List<RuntimeItemData>());
    }

    public void StartStreamingPhase(ItemModel itemModel, List<RuntimeItemData> itemsForSale = null)
    {
        _mainItemModel = itemModel;
    
        // itemsForSale が null なら空リストを使う
        if (itemsForSale == null)
        {
            itemsForSale = new List<RuntimeItemData>();
        }

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

    int fromSellIndex = view.GetSlotIndex(fromSlot);
    int toSellIndex = view.GetSlotIndex(toSlot);

    int fromInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(fromSlot) : -1;
    int toInvIndex = inventorySlotViews != null ? inventorySlotViews.IndexOf(toSlot) : -1;

    // 1) 売り場内の入れ替え
    if (fromSellIndex != -1 && toSellIndex != -1)
    {
        Debug.Log($"売り場内スワップ: {fromSellIndex} <-> {toSellIndex}");
        SwapSellSlots(fromSellIndex, toSellIndex);
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

    // 3) 在庫 → 売り場（toSlot が売り場）
    if (fromInvIndex != -1 && toSellIndex != -1)
    {
        Debug.Log($"在庫 → 売り場: invIndex={fromInvIndex}, sellIndex={toSellIndex}");
        MoveInventoryToSell(fromInvIndex, toSellIndex);
        return;
    }

    // 4) 売り場 → 在庫（fromSlot が売り場）
    if (fromSellIndex != -1 && toInvIndex != -1)
    {
        Debug.Log($"売り場 → 在庫: sellIndex={fromSellIndex}, invIndex={toInvIndex}");
        MoveSellToInventory(fromSellIndex, toInvIndex);
        return;
    }

    Debug.LogWarning("HandleItemSwap: どの領域でもないスワップが発生しました（無視）");
}

/// <summary>
/// 売り場内のスロット入れ替え（null も含む）
/// </summary>
private void SwapSellSlots(int indexA, int indexB)
{
    // リストを必要なサイズまで拡張
    EnsureSellListSize(Mathf.Max(indexA, indexB) + 1);

    var tmp = _model.ItemsForSale[indexB];
    _model.ItemsForSale[indexB] = _model.ItemsForSale[indexA];
    _model.ItemsForSale[indexA] = tmp;

    RefreshSellDisplay();
}

/// <summary>
/// 在庫から売り場へ移動（交換）
/// </summary>
private void MoveInventoryToSell(int invIndex, int sellIndex)
{
    var invItem = inventorySlotItemRefs[invIndex];
    if (invItem == null) return; // 空スロットからはドラッグできない

    // 売り場リストを必要なサイズまで拡張
    EnsureSellListSize(sellIndex + 1);

    // 売り場の現在のアイテム（null の可能性あり）
    var sellItem = _model.ItemsForSale[sellIndex];

    // 入れ替え
    _model.ItemsForSale[sellIndex] = invItem;
    inventorySlotItemRefs[invIndex] = sellItem;

    RefreshSellDisplay();
    inventorySlotViews[invIndex].SetItem(sellItem);
}

/// <summary>
/// 売り場から在庫へ移動（交換）
/// </summary>
private void MoveSellToInventory(int sellIndex, int invIndex)
{
    // 売り場のアイテムが存在するか確認
    if (sellIndex >= _model.ItemsForSale.Count) return;

    var sellItem = _model.ItemsForSale[sellIndex];
    if (sellItem == null) return; // 空スロットからはドラッグできない

    var invItem = inventorySlotItemRefs[invIndex];

    // 入れ替え
    _model.ItemsForSale[sellIndex] = invItem;
    inventorySlotItemRefs[invIndex] = sellItem;

    RefreshSellDisplay();
    inventorySlotViews[invIndex].SetItem(sellItem);
}

/// <summary>
/// 売り場リストを指定サイズまで null で拡張
/// </summary>
private void EnsureSellListSize(int requiredSize)
{
    while (_model.ItemsForSale.Count < requiredSize)
    {
        _model.ItemsForSale.Add(null);
    }
}

/// <summary>
/// 売り場の表示を更新
/// </summary>
private void RefreshSellDisplay()
{
    view.DisplayItems(_model.ItemsForSale);
    _model.OnItemsReordered.OnNext(Unit.Default);
}


    private void OnDestroy()
    {
        _presenter?.Dispose();
        ItemSlotView.OnItemDropped -= HandleItemSwap;
    }
}
