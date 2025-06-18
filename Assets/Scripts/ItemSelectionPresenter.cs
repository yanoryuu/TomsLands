using System;
using R3;

public class ItemSelectionPresenter : IDisposable
{
    private readonly ItemSelectionModel selectionModel;
    private readonly ItemSelectionView selectionView;
    private readonly ItemModel itemModel;
    private readonly CompositeDisposable disposables = new();

    public ItemSelectionPresenter(ItemSelectionModel selectionModel, ItemSelectionView selectionView, ItemModel itemModel)
    {
        this.selectionModel = selectionModel;
        this.selectionView = selectionView;
        this.itemModel = itemModel;

        selectionView.OnConfirmSelection
            .Subscribe(selectedItems =>
            {
                var displayList = itemModel.CreateItemListForDisplay(selectedItems);
                selectionModel.SetSelection(displayList);
                selectionModel.SaveSelection();

                itemModel.SetDisplayItemList(displayList);
                selectionView.Hide();
            })
            .AddTo(disposables);

        selectionView.OnCloseRequested
            .Subscribe(_ => selectionView.Hide())
            .AddTo(disposables);
    }

    public void LoadSelection()
    {
        var loaded = selectionModel.LoadSelection(itemModel.masterItems);
        itemModel.SetDisplayItemList(loaded);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}