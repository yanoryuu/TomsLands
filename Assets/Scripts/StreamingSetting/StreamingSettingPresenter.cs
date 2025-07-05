using R3;
using System;
using System.Linq;
public class StreamingSettingPresenter : IDisposable
{
    private readonly StreamingSettingModel _model;
    private readonly StreamingSettingView  _view;
    private readonly ItemModel             _itemModel;
    private readonly CompositeDisposable   _d = new CompositeDisposable();

    public StreamingSettingPresenter(
        StreamingSettingModel model,
        StreamingSettingView  view,
        ItemModel             itemModel)
    {
        _model     = model;
        _view      = view;
        _itemModel = itemModel;

        // 1) ロードして左パネルに再表示
        _model.LoadData();
        _view.PopulateSelected(_model.Selected, _itemModel);

        // 2) 利用可能アイテム一覧
        _view.PopulateAvailable(_itemModel.RuntimeItems);

        // 3) 選択
        _view.OnItemSelected
            .Subscribe(id => HandleSelected(id))
            .AddTo(_d);

        // 4) 解除
        _view.OnItemDeselected
            .Subscribe(id => HandleDeselected(id))
            .AddTo(_d);

        // 5) 数量変更
        _view.OnQuantityChanged
            .Subscribe(tuple => { _model.SetQuantity(tuple.id, tuple.qty); _model.SaveData(); })
            .AddTo(_d);
    }

    // StreamingSettingPresenter.cs

    private void HandleSelected(string id)
    {
        if (!_model.TryAdd(id)) return;

        // モデルから在庫数を取得
        int maxQty = _itemModel.GetRuntimeItem(id).Stock.Value;
        int current = _model.Selected[id];

        // 左パネルを再描画
        _view.PopulateSelected(_model.Selected, _itemModel); // 既存の全件再描画
        // もしくは個別で追加:
        _view.AddSelectedItem(id,
            _itemModel.GetMasterItem(id).itemIcon,
            _itemModel.GetMasterItem(id).itemName,
            current,
            maxQty);

        _model.SaveData();
    }


    private void HandleDeselected(string id)
    {
        _model.Remove(id);
        _view.PopulateSelected(_model.Selected, _itemModel);
        _model.SaveData();
    }

    public void Show() => _view.Show();
    public void Hide() { _model.SaveData(); _view.Hide(); }
    public void Dispose() => _d.Dispose();
}