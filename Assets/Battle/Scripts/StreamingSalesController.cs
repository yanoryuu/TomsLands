using Cysharp.Threading.Tasks;
using System.Linq;
using UnityEngine;

public class StreamingSalesController : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private float baseSalesInterval = 15f;
    [SerializeField] private float intervalRandomness = 3f;
    [SerializeField] private int maxItemsToSell = 5;

    [Header("参照")]
    [SerializeField] private StreamingSalesView view;

    private ItemModel _mainItemModel;
    private StreamingSalesPresenter _presenter;
    private StreamingSalesModel _model;

    // ## テスト用 ##
    private void Start()
    {
        Debug.LogWarning("--- テストモードで実行中 ---");

        // Project内のアセットを全て読み込み
        var masterItems = Resources.LoadAll<ItemData>("ItemData").ToList();

        var realItemModel = new ItemModel(masterItems);
        StartStreamingPhase(realItemModel);
    }

    // ## テスト用メソッドの終わり ##

    public void StartStreamingPhase(ItemModel itemModel)
    {
        _mainItemModel = itemModel;
        var itemsInStock = _mainItemModel.PickItemRuntimeListForStock(_mainItemModel.RuntimeItems, 1);
        var itemsForSale = itemsInStock.Take(maxItemsToSell).ToList();

        _model = new StreamingSalesModel(baseSalesInterval, intervalRandomness);
        _model.SetItemsForSale(itemsForSale);

        _presenter = new StreamingSalesPresenter(_model, view);
        _presenter.Bind();

        // OnItemDroppedを、このControllerが購読
        ItemSlotView.OnItemDropped += HandleItemSwap;

        _model.StartSalesLoopAsync(_mainItemModel, this.GetCancellationTokenOnDestroy()).Forget();
        Debug.Log($"営業開始！ {itemsForSale.Count}種類の商品を売りに出します。");
    }

    private void HandleItemSwap(ItemSlotView fromSlot, ItemSlotView toSlot)
    {
        // どのスロットとどのスロットが入れ替わったか、インデックスを取得
        int fromIndex = view.GetSlotIndex(fromSlot);
        int toIndex = view.GetSlotIndex(toSlot);

        if (fromIndex == -1 || toIndex == -1) return;

        Debug.Log($"アイテムを入れ替え: {fromIndex}番目 と {toIndex}番目");

        // Modelにリストの順番を入れ替え命令
        _model.SwapItems(fromIndex, toIndex);
    }
    private void OnDestroy()
    {
        _presenter?.Dispose();
        ItemSlotView.OnItemDropped -= HandleItemSwap;
    }
}
