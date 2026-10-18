using R3;
using System;

public class StreamingSalesPresenter : IDisposable
{
    private readonly StreamingSalesModel model;
    private readonly StreamingSalesView view;
    private readonly CompositeDisposable disposables = new();

    public StreamingSalesPresenter(StreamingSalesModel model, StreamingSalesView view)
    {
        this.model = model;
        this.view = view;
    }

    public void Bind()
    {
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlayBGM("配信フェーズ");

        model.TotalSales
            .Subscribe(amount => view.UpdateTotalSales(amount))
            .AddTo(disposables);

        view.DisplayItems(model.ItemsForSale);

        // 売れたスロットの通知を購読して、在庫表示更新と売れたアニメを実行する
        model.OnItemSold
            .Subscribe(slotIndex =>
            {
                if (model.ItemsForSale == null) return;
                if (slotIndex < 0 || slotIndex >= model.ItemsForSale.Count) return;

                var item = model.ItemsForSale[slotIndex];

                // 在庫表示の更新（View 側の在庫購読もあるが、即時反映させたいので明示更新）
                view.UpdateItemStock(slotIndex, item.Stock.Value);

                view.PlaySoldAnimation(slotIndex);
            })
            .AddTo(disposables);

        model.OnItemsReordered
            .Subscribe(_ => view.DisplayItems(model.ItemsForSale))
            .AddTo(disposables);

        view.DisplayItems(model.ItemsForSale);
    }

    public void Dispose() => disposables.Dispose();
}
