using System;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

public class StreamingItemPresenter : IDisposable , IPresenter
{ 
    private StreamingItemModel   streamingItemModel;
    private StreamingSettingModel streamingSettingModel;
    private ItemModel            itemModel;
    private StreamingView        streamingView;
    private TomsModel        tomsModel;
    private BattleSequencer     battleSequencer;
    private CompositeDisposable  disposables = new CompositeDisposable();

    public void Entry()
    {
        //ここにこの画面に移動した時にここを呼び出す。
    }
    
    public StreamingItemPresenter(
        StreamingItemModel    streamingItemModel,
        StreamingView         streamingView,
        ItemModel             itemModel,
        StreamingSettingModel settingModel,
        TomsModel         tomsShopModel,
        BattleSequencer battleSequencer
        )
    {
        this.streamingItemModel   = streamingItemModel;
        this.streamingView        = streamingView;
        this.itemModel            = itemModel;
        this.streamingSettingModel= settingModel;
        this.tomsModel        = tomsShopModel;
        this.battleSequencer      = battleSequencer;
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
            .Subscribe(_ => streamingItemModel.ApplyBasicStealth(tomsModel))
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

        for (int i = 0; i < streamingView.OnStreamingItemToggled.Count; i++)
        {
            streamingView.OnStreamingItemToggled[i].Subscribe(x =>
                {
                    streamingItemModel.SetIsSell(x.itemId,x.isSell);
                })
                .AddTo(disposables);
        }        

        // // 集中ステマ（桜）
         // streamingView.OnFocusedStealthRequested
        //     .Subscribe(id => streamingItemModel.ApplyFocusedStealth(id, tomsShopModel))
        //     .AddTo(disposables);
        
        
        battleSequencer.OnBattleWin
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
                        // tomsShopModel.Settlement(item.price.Value, soldQuantity);
                    }
                }
                //勝利時に装備していた装備の値段をあげる
                itemModel.BattleWinBonus(win.armorId,5);
                itemModel.BattleWinBonus(win.weaponId,5);
            })
            .AddTo(disposables);
        
        battleSequencer.OnBattleDefeat
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
                        // tomsShopModel.Settlement(item.price.Value, soldQuantity);
                    }
                }
                //敗北時に装備していた装備の値段を下げる
                itemModel.BattleDefeatPenalty(defeat.armorId,2);
                itemModel.BattleDefeatPenalty(defeat.weaponId,2);
            })
            .AddTo(disposables);

        foreach (var characterPresenter in battleSequencer.CharacterPresenters)
        {
            characterPresenter.OnTakeDamage.Subscribe(attackerModel =>
            {
                var targetModel = characterPresenter.GetModel();

                // --- 勇者がダメージを受けた時の処理 ---
                if (targetModel.Type == CharacterType.Hero)
                {
                    // 勇者が装備している防具のIDを取得
                    string armorId = targetModel.EquippedArmor?.itemId;
                    if (!string.IsNullOrEmpty(armorId))
                    {
                        var item = streamingItemModel.runtimeStreamingItems.Find(i => i.itemId == armorId);
                        if (item != null)
                        {
                            streamingItemModel.UpdateStreamingItems(armorId, Mathf.RoundToInt(item.price.Value * 1.1f));
                        }
                    }
                }
                // --- 敵がダメージを受けた時の処理 ---
                else if (targetModel.Type == CharacterType.Enemy)
                {
                    // 勇者(攻撃者)が装備している武器のIDを取得
                    string weaponId = attackerModel.EquippedWeapon?.itemId;
                    if (!string.IsNullOrEmpty(weaponId))
                    {
                        var item = streamingItemModel.runtimeStreamingItems.Find(i => i.itemId == weaponId);
                        if (item != null)
                        {
                            streamingItemModel.UpdateStreamingItems(weaponId, Mathf.RoundToInt(item.price.Value * 1.1f));
                        }
                    }
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