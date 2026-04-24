using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using VContainer;

/// <summary>
/// 配信リザルト画面。勝敗・売上サマリーを表示し、確認ボタンで次へ進む。
/// パネルの表示/非表示は BattlePanelManager が管理する。
/// </summary>
public class BattleResultView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resultTitleText;
    [SerializeField] private TextMeshProUGUI resultDetailText;
    [SerializeField] private Button confirmButton;

    [Header("売れたアイテム一覧")]
    [SerializeField] private BattleResultSoldItemSlot soldItemSlotPrefab;
    [SerializeField] private Transform soldItemContainer;

    private UniTaskCompletionSource _confirmTcs;
    private ItemModel _itemModel;

    [Inject]
    public void Construct(ItemModel itemModel)
    {
        _itemModel = itemModel;
    }

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        // パネルの表示/非表示は BattlePanelManager が管理するため、ここでは制御しない
    }

    /// <summary>
    /// リザルト画面のコンテンツを設定し、確認ボタンが押されるまで待機する。
    /// パネルの表示/非表示は BattlePanelManager が事前に行う。
    /// </summary>
    public async UniTask ShowResultAsync(BattleResult result, List<BattleOutputSoldItem> soldItems)
    {
        _confirmTcs = new UniTaskCompletionSource();

        // 勝敗タイトル
        if (resultTitleText != null)
        {
            resultTitleText.text = result == BattleResult.Victory ? "勝利！" : "敗北...";
        }

        // 売上サマリー
        if (resultDetailText != null)
        {
            int totalRevenue = 0;
            if (soldItems != null)
            {
                foreach (var item in soldItems)
                {
                    totalRevenue += item.SoldPrice * item.SoldQuantity;
                }
            }
            resultDetailText.text = $"売上: {totalRevenue} G";
        }

        // 売れたアイテムスロット生成
        PopulateSoldItemSlots(soldItems);

        // 確認ボタン待ち
        await _confirmTcs.Task;
    }

    private void PopulateSoldItemSlots(List<BattleOutputSoldItem> soldItems)
    {
        if (soldItemSlotPrefab == null || soldItemContainer == null) return;

        // 既存スロットを削除
        foreach (Transform child in soldItemContainer)
            Destroy(child.gameObject);

        if (soldItems == null) return;

        foreach (var soldItem in soldItems)
        {
            if (soldItem.SoldQuantity <= 0) continue;

            var runtimeItem = _itemModel?.GetRuntimeItem(soldItem.ItemId);
            if (runtimeItem == null) continue;

            var slot = Instantiate(soldItemSlotPrefab, soldItemContainer);
            slot.Setup(runtimeItem.ItemIcon, soldItem.SoldQuantity);
        }
    }

    private void OnConfirmClicked()
    {
        _confirmTcs?.TrySetResult();
    }
}

