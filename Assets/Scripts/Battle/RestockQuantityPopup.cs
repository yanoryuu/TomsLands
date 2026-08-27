using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトル中の在庫補充ポップアップ。
/// 鍛冶屋の購入UI（ItemDetailPanel）をそのまま流用し、数量スライダー・±ボタン・
/// 合計金額表示・購入ボタンで補充数を決める。
/// ShowAsync は選択された数量を返す（キャンセル時は 0）。
/// </summary>
public class RestockQuantityPopup : MonoBehaviour
{
    [Tooltip("ディム背景を含むポップアップ全体のルート")]
    [SerializeField] private GameObject panel;
    [Tooltip("鍛冶屋と同じ購入パネル（FightScene 内に複製したもの）")]
    [SerializeField] private ItemDetailPanel detailPanel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TextMeshProUGUI titleText;

    private int _quantity;
    private int _maxQuantity;
    private UniTaskCompletionSource<int> _tcs;
    private CompositeDisposable _showDisposables;

    private void Awake()
    {
        if (cancelButton) cancelButton.onClick.AddListener(() => Close(0));
        if (panel) panel.SetActive(false);
    }

    /// <summary>
    /// 補充ポップアップを表示する。
    /// </summary>
    /// <param name="runtime">補充するアイテム</param>
    /// <param name="unitCost">1個あたりの費用（基準価格）</param>
    /// <param name="maxQuantity">購入可能な最大数（0以下なら購入不可）</param>
    /// <param name="recommendScore">おすすめ度（鍛冶屋と同じ計算値）</param>
    /// <returns>選択された数量（キャンセル時は 0）</returns>
    public async UniTask<int> ShowAsync(RuntimeItemData runtime, int unitCost, int maxQuantity, float recommendScore, CancellationToken token)
    {
        if (detailPanel == null)
        {
            Debug.LogError("[RestockQuantityPopup] detailPanel が未配線です。");
            return 0;
        }

        _maxQuantity = Mathf.Max(0, maxQuantity);
        _quantity = Mathf.Clamp(10, _maxQuantity > 0 ? 1 : 0, _maxQuantity); // 既定10個（買える範囲に丸める）

        if (titleText) titleText.text = $"「{runtime.ItemName}」を補充";
        if (panel) panel.SetActive(true);

        // 鍛冶屋と同じ表示（アイコン・チャート・市場分析）。チャートは配信中の価格変動を表示
        detailPanel.ShowItem(runtime, unitCost, recommendScore, useBattleHistory: true);
        // 費用は現在価格ではなく補充単価（基準価格）で計算する
        detailPanel.SetPrice(unitCost);
        detailPanel.SetMaxQuantity(_maxQuantity);
        detailPanel.SetQuantity(_quantity);

        // パネル操作の購読（表示中のみ）
        _showDisposables = new CompositeDisposable();
        detailPanel.OnDisplayQuantityChanged
            .Subscribe(q => _quantity = Mathf.Clamp(q, 0, _maxQuantity))
            .AddTo(_showDisposables);
        detailPanel.OnStepClicked
            .Subscribe(step =>
            {
                _quantity = Mathf.Clamp(_quantity + step, 0, _maxQuantity);
                detailPanel.SetQuantity(_quantity);
            })
            .AddTo(_showDisposables);
        detailPanel.OnPurchaseClicked
            .Subscribe(_ => Close(_quantity))
            .AddTo(_showDisposables);

        _tcs = new UniTaskCompletionSource<int>();
        int result;
        try
        {
            // キャンセル（バトル終了等）時は 0 扱いで閉じる
            using (token.Register(() => _tcs.TrySetResult(0)))
            {
                result = await _tcs.Task;
            }
        }
        finally
        {
            _showDisposables?.Dispose();
            _showDisposables = null;
            detailPanel.Hide();
            if (panel) panel.SetActive(false);
            _tcs = null;
        }
        return result;
    }

    private void Close(int result)
    {
        _tcs?.TrySetResult(result);
    }

    private void OnDestroy()
    {
        _showDisposables?.Dispose();
    }
}
