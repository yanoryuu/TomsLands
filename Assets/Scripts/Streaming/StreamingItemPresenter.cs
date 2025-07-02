using System.Collections.Generic;

public class StreamingItemPresenter
{
    public StreamingItemModel streamingItemModel { get; private set; }
    public StreamingView streamingView { get; private set; }
    
    public StreamingItemPresenter(StreamingItemModel streamingItemModel,StreamingView streamingView)
    {
        this.streamingItemModel = streamingItemModel;
        this.streamingView = streamingView;
    }

    public void Initialize(List<RuntimeItemData> itemDataList)
    {
        streamingItemModel.LoadData(itemDataList);
        
        
    }
}
