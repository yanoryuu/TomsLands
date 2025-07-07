using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public class StreamingItemModel
{
    public List<StreamingItemPlain> runtimeStreamingItems { get; private set; } = new();
    public ReactiveProperty<float>  trustPenalty           { get; private set; }
    public StealthMarketingModel    stealthMarketingModel { get; private set; }

    public void Initialize()
    {
        runtimeStreamingItems.Clear();
        trustPenalty = new ReactiveProperty<float>(0f);
        // initCost=200G, 全体20%, 集中50%, CD=30s
        stealthMarketingModel = new StealthMarketingModel(200, 20f, 50f, 30f);
    }

    public void LoadStreamingItems(List<StreamingItemPlain> itemDataList)
    {
        runtimeStreamingItems = itemDataList;
    }

    public void UpdateStreamingItems(string itemId, int newPrice, int newQuantity)
    {
        var itemData = runtimeStreamingItems.Find(item => item.itemId == itemId);
        if (itemData == null)
        {
            Debug.LogError($"Streaming item with ID {itemId} not found.");
            return;
        }
        itemData.price    = newPrice;
        itemData.quantity = newQuantity;
    }

    // ── 追加メソッド ──

    /// <summary>全体にステマをかける</summary>
    public void ApplyBasicStealth(TomsShopModel shop)
        => stealthMarketingModel.PerformBasic(runtimeStreamingItems, shop);

    /// <summary>指定アイテムにステマをかける</summary>
    public void ApplyFocusedStealth(string itemId, TomsShopModel shop)
    {
        var item = runtimeStreamingItems.Find(i => i.itemId == itemId);
        stealthMarketingModel.PerformFocused(item, shop);
    }
}


[Serializable]
public class StreamingItemPlain
{
    public StreamingItemPlain(string itemId, int price, Sprite icon, int quantity, float demand)
    {
        this.itemId = itemId;
        this.price = price;
        this.icon = icon;
        this.quantity = quantity;
        this.demand = demand;
    }
    public string itemId;
    public int price;
    public Sprite icon;
    public int quantity;
    public float demand;
}
