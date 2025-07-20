using System;
using R3;
using UnityEngine;

public class StreamingItemPresenter : IDisposable
{ 
    private StreamingItemModel   streamingItemModel;
    private StreamingSettingModel streamingSettingModel;
    private ItemModel            itemModel;
    private StreamingView        streamingView;
    private TomsShopModel        tomsShopModel;
    private BattleManager       battleManager;
    private CompositeDisposable  disposables = new CompositeDisposable();

    public StreamingItemPresenter(
        StreamingItemModel    streamingItemModel,
        StreamingView         streamingView,
        ItemModel             itemModel,
        StreamingSettingModel settingModel,
        TomsShopModel         tomsShopModel,
        BattleManager battleManager
        )
    {
        this.streamingItemModel   = streamingItemModel;
        this.streamingView        = streamingView;
        this.itemModel            = itemModel;
        this.streamingSettingModel= settingModel;
        this.tomsShopModel        = tomsShopModel;
        this.battleManager        = battleManager;
        disposables = new CompositeDisposable();
    }

    public void Initialize()
    {
        disposables.Dispose();
        disposables = new CompositeDisposable();
        
        streamingItemModel.Initialize();
        streamingItemModel.LoadStreamingItems(
            streamingSettingModel.GetSelectedRuntimeItemData(itemModel));

        // コスト表示
        streamingItemModel.stealthMarketingModel.Cost
            .Subscribe(c => streamingView.SetStealthMarketingCost(c))
            .AddTo(disposables);
        
        // クールダウン表示
        streamingItemModel.stealthMarketingModel.CooldownRemaining
            .Subscribe(cd => streamingView.SetStealthCooldown(cd))
            .AddTo(disposables);

        // 全体ステマ
        streamingView.OnBasicStealthRequested
            .Subscribe(_ => streamingItemModel.ApplyBasicStealth(tomsShopModel))
            .AddTo(disposables);
        
        for(int i = 0; i < streamingItemModel.runtimeStreamingItems.Count; i++)
        {
            var item = streamingItemModel.runtimeStreamingItems[i];
            // アイテムの価格を更新
            item.price.Subscribe(price =>
                {
                    streamingView.SetStreamingItemsPriceText(i, price);
                })
                .AddTo(disposables);
        }
        
        

        // // 集中ステマ（桜）
         // streamingView.OnFocusedStealthRequested
        //     .Subscribe(id => streamingItemModel.ApplyFocusedStealth(id, tomsShopModel))
        //     .AddTo(disposables);
        
        
        battleManager.OnWin
            .Subscribe(win =>
            {
                foreach (var item in streamingItemModel.runtimeStreamingItems)
                {
                    int soldQuantity = Mathf.RoundToInt(item.quantity * item.demand);
                    if (soldQuantity > 0)
                    {
                        // アイテムモデルに販売数量を反映
                        itemModel.Settlement(item.itemId, soldQuantity);
                
                        // TomsShopModelに販売処理を反映
                        tomsShopModel.Settlement(item.price.Value, soldQuantity);
                    }
                }
                //勝利時に装備していた装備の値段をあげる
                itemModel.BattleWinBonus(win.armor,5);
                itemModel.BattleWinBonus(win.weapon,5);
            })
            .AddTo(disposables);
        
        battleManager.OnDefeat
            .Subscribe(defeat =>
            {
                foreach (var item in streamingItemModel.runtimeStreamingItems)
                {
                    int soldQuantity = Mathf.RoundToInt(item.quantity * item.demand);
                    if (soldQuantity > 0)
                    {
                        // アイテムモデルに販売数量を反映
                        itemModel.Settlement(item.itemId, soldQuantity);
                
                        // TomsShopModelに販売処理を反映
                        tomsShopModel.Settlement(item.price.Value, soldQuantity);
                    }
                }
                //敗北時に装備していた装備の値段を下げる
                itemModel.BattleDefeatPenalty(defeat.armor,2);
                itemModel.BattleDefeatPenalty(defeat.weapon,2);
            })
            .AddTo(disposables);

        foreach (var enemy in battleManager.enemies)
        {
            enemy.OnTakeDamage.Subscribe(isHero =>
            {
                if (isHero)
                {
                    var item = streamingItemModel.runtimeStreamingItems.Find(item =>
                        item.itemId == enemy.HeroData.armorId.Value);
                    streamingItemModel.UpdateStreamingItems(enemy.HeroData.armorId.Value,Mathf.RoundToInt(item.price.Value*1.1f));
                }
                else
                {
                    var item = streamingItemModel.runtimeStreamingItems.Find(item =>
                        item.itemId == enemy.HeroData.armorId.Value);
                    streamingItemModel.UpdateStreamingItems(enemy.HeroData.weaponId.Value,Mathf.RoundToInt(item.price.Value*1.1f));
                }
            })
            .AddTo(disposables);
        }
    }

    public void Dispose()
    {
        disposables.Dispose();
        streamingItemModel.stealthMarketingModel.Dispose();
    }
}