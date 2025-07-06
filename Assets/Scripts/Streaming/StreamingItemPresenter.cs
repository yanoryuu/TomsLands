using System.Collections.Generic;
using Unity.Collections;
using R3;

public class StreamingItemPresenter
{
    public StreamingItemModel streamingItemModel { get; private set; }
    
    public StreamingSettingModel streamingSettingModel { get; private set; }
    
    public ItemModel itemModel { get; private set; }
    public StreamingView streamingView { get; private set; }
    
    private CompositeDisposable disposable = new CompositeDisposable();
    
    public StreamingItemPresenter(StreamingItemModel streamingItemModel,StreamingView streamingView,ItemModel itemModel,StreamingSettingModel settingModel)
    {
        this.streamingItemModel = streamingItemModel;
        this.streamingView = streamingView;
        this.itemModel = itemModel;
        this.streamingSettingModel = settingModel;
    }

    public void Initialize()
    {
        streamingItemModel.LoadStreamingItems(streamingSettingModel.GetSelectedRuntimeItemData(itemModel));
        
        streamingItemModel.Initialize();
        
        streamingItemModel.stealthMarketingModel.Cost
            .Subscribe(cost => streamingView.SetStealthMarketingCost(cost))
            .AddTo(disposable);
    }
}
