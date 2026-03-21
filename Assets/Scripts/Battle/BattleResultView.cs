using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// 配信リザルト画面。勝敗・売上サマリーを表示し、確認ボタンで次へ進む。
/// </summary>
public class BattleResultView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI resultTitleText;
    [SerializeField] private TextMeshProUGUI resultDetailText;
    [SerializeField] private Button confirmButton;

    private UniTaskCompletionSource _confirmTcs;

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }
        gameObject.SetActive(false);
    }

    /// <summary>
    /// リザルト画面を表示し、確認ボタンが押されるまで待機する。
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
            int totalSold = 0;
            if (soldItems != null)
            {
                foreach (var item in soldItems)
                {
                    totalRevenue += item.SoldPrice * item.SoldQuantity;
                    totalSold += item.SoldQuantity;
                }
            }
            resultDetailText.text = $"販売数: {totalSold} 個\n売上: {totalRevenue} G";
        }

        gameObject.SetActive(true);

        // 確認ボタン待ち
        await _confirmTcs.Task;

        gameObject.SetActive(false);
    }

    private void OnConfirmClicked()
    {
        _confirmTcs?.TrySetResult();
    }
}

