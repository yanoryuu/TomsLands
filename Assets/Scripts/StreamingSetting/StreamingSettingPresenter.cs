using R3;
using System;
using System.Linq;

/// <summary>
/// StreamingSettingView と StreamingSettingModel, ItemModel をつなぐ Presenter
/// ドラッグ＆ドロップ／数量変更／削除を取りまとめる
/// </summary>
public class StreamingSettingPresenter : IDisposable
{
    private readonly StreamingSettingModel model;
    private readonly StreamingSettingView  view;
    private readonly ItemModel             itemModel;
    private readonly GamePhasePresenter    gamePhasePresenter;
    private readonly CompositeDisposable   d = new CompositeDisposable();

    public StreamingSettingPresenter(
        StreamingSettingModel model,
        StreamingSettingView  view,
        ItemModel             itemModel,
        GamePhasePresenter    gamePhasePresenter
        )
    {
        this.model     = model;
        this.view      = view;
        this.itemModel = itemModel;
        this.gamePhasePresenter = gamePhasePresenter;

        // 右パネル生成
        view.PopulateAvailableItems(itemModel.RuntimeItems);

        // ドロップ処理
        view.OnItemDropped
            .Subscribe(id => HandleDropped(id))
            .AddTo(d);

        // 数量変更
        view.OnQuantityChanged
            .Subscribe(tuple => model.SetQuantity(tuple.id, tuple.qty))
            .AddTo(d);

        // 削除
        view.OnItemRemoved
            .Subscribe(id => model.Remove(id))
            .AddTo(d);
        
        //　配信画面行き
        view.OnConfirmClicked.Subscribe(_=>gamePhasePresenter.ChangePhase(GamePhase.Streaming))
            .AddTo(d);
    }

    private void HandleDropped(string id)
    {
        if (!model.TryAdd(id))
            return;

        var master = itemModel.GetMasterItem(id);
        view.AddSelectedItem(id, master.itemIcon, master.itemName);
    }

    public void Show() => view.gameObject.SetActive(true);
    public void Hide() => view.gameObject.SetActive(false);

    public void Dispose() => d.Dispose();
}