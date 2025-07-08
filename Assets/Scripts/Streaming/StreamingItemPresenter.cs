using System;
using R3;

public class StreamingItemPresenter : IDisposable
{
    StreamingItemModel   streamingItemModel;
    StreamingSettingModel streamingSettingModel;
    ItemModel            itemModel;
    StreamingView        streamingView;
    TomsShopModel        tomsShopModel;

    CompositeDisposable  disposables = new CompositeDisposable();

    public StreamingItemPresenter(
        StreamingItemModel    streamingItemModel,
        StreamingView         streamingView,
        ItemModel             itemModel,
        StreamingSettingModel settingModel,
        TomsShopModel         tomsShopModel)
    {
        this.streamingItemModel   = streamingItemModel;
        this.streamingView        = streamingView;
        this.itemModel            = itemModel;
        this.streamingSettingModel= settingModel;
        this.tomsShopModel        = tomsShopModel;
    }

    public void Initialize()
    {
        streamingItemModel.Initialize();
        streamingItemModel.LoadStreamingItems(
            streamingSettingModel.GetSelectedRuntimeItemData(itemModel));

        // // コスト表示
        // streamingItemModel.stealthMarketingModel.Cost
        //     .Subscribe(c => streamingView.SetStealthMarketingCost(c))
        //     .AddTo(disposables);
        //
        // // クールダウン表示
        // streamingItemModel.stealthMarketingModel.CooldownRemaining
        //     .Subscribe(cd => streamingView.SetStealthCooldown(cd))
        //     .AddTo(disposables);

        // 全体ステマ
        streamingView.OnBasicStealthRequested
            .Subscribe(_ => streamingItemModel.ApplyBasicStealth(tomsShopModel))
            .AddTo(disposables);

        // 集中ステマ（桜）
        streamingView.OnFocusedStealthRequested
            .Subscribe(id => streamingItemModel.ApplyFocusedStealth(id, tomsShopModel))
            .AddTo(disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
        streamingItemModel.stealthMarketingModel.Dispose();
    }
}