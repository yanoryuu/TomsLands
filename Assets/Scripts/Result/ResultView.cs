using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// リザルト画面のView。
/// ゲーム全体の統計情報を表示し、タイトルへ戻るボタンを提供する。
/// endPhasePanel にアタッチして使用する。
/// </summary>
public class ResultView : MonoBehaviour
{
    [Header("=== ランク表示 ===")]
    [SerializeField] private TextMeshProUGUI rankText;

    [Header("=== 資産情報 ===")]
    [SerializeField] private TextMeshProUGUI finalMoneyText;
    [SerializeField] private TextMeshProUGUI moneyDifferenceText;
    [SerializeField] private TextMeshProUGUI stockValueText;
    [SerializeField] private TextMeshProUGUI netWorthText;

    [Header("=== ゲーム情報 ===")]
    [SerializeField] private TextMeshProUGUI totalTurnsText;
    [SerializeField] private TextMeshProUGUI blacksmithLevelText;
    [SerializeField] private TextMeshProUGUI infoBrokerLevelText;

    [Header("=== ボタン ===")]
    [SerializeField] private Button goToTitleButton;
    [SerializeField] private Button retryButton;


    /// <summary>タイトルへ戻るボタンが押された</summary>
    public Subject<Unit> OnGoToTitleClicked { get; } = new();

    /// <summary>もう一度遊ぶボタンが押された</summary>
    public Subject<Unit> OnRetryClicked { get; } = new();

    private void Awake()
    {
        if (goToTitleButton != null)
            goToTitleButton.onClick.AddListener(() => OnGoToTitleClicked.OnNext(Unit.Default));

        if (retryButton != null)
            retryButton.onClick.AddListener(() => OnRetryClicked.OnNext(Unit.Default));
    }

    /// <summary>
    /// リザルト画面の内容を更新する
    /// </summary>
    public void UpdateContent(ResultStatisticsData data)
    {
        if (data == null) return;


        // --- ランク表示 ---
        if (rankText != null)
            rankText.text = data.Rank;

        // --- 資産情報 ---
        if (finalMoneyText != null)
            finalMoneyText.text = $"最終金額{data.FinalMoney:#,0}G";

        if (moneyDifferenceText != null)
        {
            string sign = data.MoneyDifference >= 0 ? "+" : "";
            moneyDifferenceText.text = $"初期金額からの儲け{sign}{data.MoneyDifference:#,0}G";
            // 黒字は緑、赤字は赤
            moneyDifferenceText.color = data.MoneyDifference >= 0
                ? new Color(0.2f, 0.8f, 0.2f)
                : new Color(0.9f, 0.2f, 0.2f);
        }

        if (stockValueText != null)
            stockValueText.text = $"商品金額{data.TotalStockValue:#,0}G";

        if (netWorthText != null)
            netWorthText.text = $"合計{data.NetWorth:#,0}G";

        // --- ゲーム情報 ---
        if (totalTurnsText != null)
            totalTurnsText.text = $"合計{data.TotalTurns}ターン";

        if (blacksmithLevelText != null)
            blacksmithLevelText.text = $"鍛冶屋Lv.{data.BlacksmithLevel}";

        if (infoBrokerLevelText != null)
            infoBrokerLevelText.text = $"情報屋Lv.{data.InfoBrokerLevel}";

        // --- ランクに応じた色を設定 ---
        if (rankText != null)
            rankText.color = GetRankColor(data.Rank);
    }

    /// <summary>
    /// ランクに応じた色を返す
    /// </summary>
    private Color GetRankColor(string rank)
    {
        return rank switch
        {
            "S" => new Color(1f, 0.84f, 0f),       // ゴールド
            "A" => new Color(0.9f, 0.3f, 0.3f),     // 赤
            "B" => new Color(0.3f, 0.6f, 0.9f),     // 青
            "C" => new Color(0.3f, 0.8f, 0.3f),     // 緑
            "D" => new Color(0.5f, 0.5f, 0.5f),     // グレー
            _ => Color.white
        };
    }
}
