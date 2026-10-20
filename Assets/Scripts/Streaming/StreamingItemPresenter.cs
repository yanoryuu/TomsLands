using System;
using R3;

// 未使用（2026-10-20 確認）: 旧ステマ機能の名残。どのシーン・コードからも使われていない（StreamingView はビルド外の Information.unity にだけある）。
// 廃止済みの TomsModel.Trust を前提にしているので、使うなら作り直すこと。
public class StreamingItemPresenter : IDisposable , IPresenter
{ 
    private readonly StreamingItemModel    streamingItemModel;
    private readonly StreamingSettingModel streamingSettingModel;
    private readonly ItemModel             itemModel;
    private readonly StreamingView         streamingView;
    private readonly TomsModel             tomsModel;
    private CompositeDisposable disposables = new();

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
            // for のループ変数はラムダ間で共有されるため、スロットごとにコピーして捕まえる
            int index = i;
            var item = streamingItemModel.runtimeStreamingItems[index];
            item.price.Subscribe(price => streamingView.SetStreamingItemsPriceText(index, price))
                .AddTo(disposables);
        }

        for (int i = 0; i < streamingView.OnStreamingItemToggled.Count; i++)
        {
            streamingView.OnStreamingItemToggled[i].Subscribe(x => streamingItemModel.SetIsSell(x.itemId, x.isSell))
                .AddTo(disposables);
        }

        // ※ 戦闘結果の処理（勝敗ボーナス/ペナルティ・売上精算）は
        //    BattleResultHandler に移行済み。戦闘は FightScene で実行される。
    }

    public void Dispose()
    {
        disposables.Dispose();
        streamingItemModel.stealthMarketingModel.Dispose();
    }
}