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
        TomsModel         tomsShopModel
        )
    {
        this.streamingItemModel   = streamingItemModel;
        this.streamingView        = streamingView;
        this.itemModel            = itemModel;
        this.streamingSettingModel= settingModel;
        this.tomsModel        = tomsShopModel;
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

        // ※ 戦闘結果の処理（勝敗ボーナス/ペナルティ・売上精算）は
        //    BattleResultHandler に移行済み。戦闘は FightScene で実行される。
    }
    

    public void Dispose()
    {
        disposables.Dispose();
        streamingItemModel.stealthMarketingModel.Dispose();
    }
}