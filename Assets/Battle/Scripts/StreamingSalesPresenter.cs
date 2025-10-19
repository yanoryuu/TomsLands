using R3;
using System;

public class StreamingSalesPresenter : IDisposable
{
    private readonly StreamingSalesModel model;
    private readonly StreamingSalesView view;
    private readonly CompositeDisposable disposables = new CompositeDisposable();

    public StreamingSalesPresenter(StreamingSalesModel model, StreamingSalesView view)
    {
        this.model = model;
        this.view = view;
    }

    public void Bind()
    {
        // Modelの総売上が変更されたら、自動的にViewの表示を更新するように購読
        model.TotalSales
            .Subscribe(amount => view.UpdateTotalSales(amount))
            .AddTo(disposables);

        // 最初に売り場の商品を表示します
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

                // 売れたエフェクト再生
                view.PlaySoldAnimation(slotIndex);
            })
            .AddTo(disposables);

        model.OnItemsReordered
            .Subscribe(_ => {
                view.DisplayItems(model.ItemsForSale);
            })
            .AddTo(disposables);

        // 商品リストが更新されたら、Viewの表示を更新
        view.DisplayItems(model.ItemsForSale);
    }



    public void Dispose()
    {
        disposables.Dispose();
    }
}
