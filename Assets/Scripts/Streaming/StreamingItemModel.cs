using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

public class StreamingItemModel
{
    public List<StreamingItemPlain> runtimeStreamingItems { get; private set; }　= new();
    
    public ReactiveProperty<float> trustPenalty  { get;private set; }
    
    public StealthMarketingModel stealthMarketingModel { get; private set; }
    
    public void Initialize()
    {
        // 初期化処理が必要な場合はここに記述
        runtimeStreamingItems.Clear();

        trustPenalty = new ReactiveProperty<float>();
        
        stealthMarketingModel = new StealthMarketingModel(200, 20, 3);
    }
    
    public void LoadStreamingItems(List<StreamingItemPlain> itemDataList)
    {
        runtimeStreamingItems = itemDataList;
    }

    public void UpdateStreamingItems(string itemId,int newPrice,int newQuantity)
    {
        var itemData = runtimeStreamingItems.Find(item => item.itemId == itemId);
        if (itemData == null)
        {
            Debug.LogError($"Streaming item with ID {itemId} not found.");
            return;
        }
        
        itemData.price = newPrice;
        itemData.quantity = newQuantity;
    }
    
    
    public void StealthMarketing(StreamingItemPlain item, StealthMarketingModel model, TomsShopModel tomsShopModel)
    {
        if (item == null || model == null) return;

        // 価格にインパクトを与える
        item.price = Mathf.RoundToInt(item.price * (1 + model.PriceImpact.Value));
        
        // 信頼度を減少させる
        trustPenalty.Value = Mathf.Max(0, trustPenalty.Value - 0.1f);

        // コストを支払う
        tomsShopModel.PlayerMoney.Value -= model.Cost.Value;

        // 使用回数を増やす
        model.UsesRemaining.Value++;
        
        //次に使用する値段を上げる
        model.Cost.Value = Mathf.RoundToInt(model.Cost.Value * 1.2f);
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

[Serializable]
public class StealthMarketingModel
{
    public StealthMarketingModel(int initCost, int priceImpact, int usesRemaining)
    {
        Cost = new ReactiveProperty<int>(initCost);
        PriceImpact = new ReactiveProperty<int>(priceImpact/100);
        UsesRemaining = new ReactiveProperty<int>(usesRemaining);
    }
    // ステマコスト
    public ReactiveProperty<int> Cost{ get; }
    
    public ReactiveProperty<int> PriceImpact   { get; }
    // 使用回数
    public ReactiveProperty<int>   UsesRemaining { get; }
}
