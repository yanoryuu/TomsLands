using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

[Serializable]
public class StealthMarketingModel : IDisposable
{
    // 初期コスト
    public ReactiveProperty<int> Cost               { get; private set; }
    // 全体ステマ効果（例：20% → 0.2f）
    public ReactiveProperty<float> PriceImpactAll   { get; private set; }
    // 集中ステマ効果（例：50% → 0.5f）
    public ReactiveProperty<float> PriceImpactFocused { get; private set; }
    // クールダウン設定（秒）
    public float CooldownDuration                   { get; private set; }
    public ReactiveProperty<float> CooldownRemaining{ get; private set; }

    readonly CompositeDisposable disposables = new();

    public StealthMarketingModel(int initCost, float impactAllPercent, float impactFocusedPercent, float cooldownSec)
    {
        Cost               = new ReactiveProperty<int>(initCost);
        PriceImpactAll     = new ReactiveProperty<float>(impactAllPercent   / 100f);
        PriceImpactFocused = new ReactiveProperty<float>(impactFocusedPercent/ 100f);
        CooldownDuration   = cooldownSec;
        CooldownRemaining  = new ReactiveProperty<float>(0f);
    }

    public bool CanPerform => CooldownRemaining.Value <= 0f;

    public bool PerformBasic(IEnumerable<StreamingItemPlain> items, TomsShopModel shop)
    {
        if (!CanPerform || shop.PlayerMoney.Value < Cost.Value) return false;

        shop.PlayerMoney.Value -= Cost.Value;
        foreach (var it in items)
            it.price.Value = Mathf.RoundToInt(it.price.Value * (1 + PriceImpactAll.Value));

        shop.Trust.Value = Mathf.Max(0f, shop.Trust.Value - 0.1f);
        Cost.Value       = Mathf.RoundToInt(Cost.Value * 1.2f);
        
        Debug.Log("Basic stealth applied to all items:");
        StartCooldown();
        return true;
    }

    public bool PerformFocused(StreamingItemPlain item, TomsShopModel shop)
    {
        if (!CanPerform || shop.PlayerMoney.Value < Cost.Value || item == null) return false;

        shop.PlayerMoney.Value -= Cost.Value;
        item.price.Value = Mathf.RoundToInt(item.price.Value * (1 + PriceImpactFocused.Value));

        shop.Trust.Value = Mathf.Max(0f, shop.Trust.Value - 0.1f);
        Cost.Value       = Mathf.RoundToInt(Cost.Value * 1.2f);
        
        Debug.Log("Focused stealth applied to item: " + item.itemId);
        StartCooldown();
        return true;
    }

    void StartCooldown()
    {
        CooldownRemaining.Value = CooldownDuration;
        Observable
          .Interval(TimeSpan.FromSeconds(1))
          .TakeWhile(_ => CooldownRemaining.Value > 0f)
          .Subscribe(_ => CooldownRemaining.Value = Mathf.Max(0f, CooldownRemaining.Value - 1f))
          .AddTo(disposables);
    }

    public void Dispose() => disposables.Dispose();
}
