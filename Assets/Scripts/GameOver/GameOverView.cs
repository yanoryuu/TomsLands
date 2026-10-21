using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameOverView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button goToTitleButton;

    [Header("振り返り（任意。未配線なら出さない）")]
    [Tooltip("配信の成績・持ち込みの行方・村へ入る額")]
    [SerializeField] private TextMeshProUGUI reviewText;
    [Tooltip("所持金の推移")]
    [SerializeField] private PriceChartView moneyChart;

    public Subject<Unit> OnRetryClicked { get; } = new();
    public Subject<Unit> OnGoToTitleClicked { get; } = new();

    private void Awake()
    {
        retryButton.onClick.AddListener(() => OnRetryClicked.OnNext(Unit.Default));
        goToTitleButton.onClick.AddListener(() => OnGoToTitleClicked.OnNext(Unit.Default));

        // ボタンは「村へ戻る」1つにまとめる（どちらも村へ戻っていたため。タイトルへは村のメニューから行ける）
        SetButtonLabel(retryButton, "村へ戻る");
        if (goToTitleButton != null) goToTitleButton.gameObject.SetActive(false);

        // パッド/キー: 最初は「村へ戻る」。戻る操作は無い（Cancel なし）
        var scope = UIFocusScope.GetOrAdd(gameObject);
        scope.FirstSelected = retryButton;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;
        var tmp = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null) tmp.text = label;
    }

    public void Setup(int turn)
    {
        if (titleText != null)
            titleText.text = "税金納付不能！！";

        if (messageText != null)
            messageText.text = "ト、トコちゃん！！\nお店が差し押さえられてしまいました！";

        if (turnText != null)
            turnText.text = $"ターン {turn} で力尽きました...";
    }

    /// <summary>
    /// このランの振り返りを出す（所持金の推移・配信の成績・持ち込みの行方・村へ入る額）。未配線の部品は飛ばす。
    /// </summary>
    public void ShowReview(IReadOnlyList<int> moneyHistory, int heroWins, int heroLosses,
        int streamEarnings, int carried, int villageFunds)
    {
        if (moneyChart != null && moneyHistory != null && moneyHistory.Count > 0)
        {
            var points = new List<int>(moneyHistory);
            if (points.Count == 1) points.Insert(0, points[0]);
            moneyChart.SetData(points);
        }

        if (reviewText == null) return;
        string sign = streamEarnings >= 0 ? "+" : "−";
        var lines = new List<string>
        {
            $"配信　討伐 {heroWins}・防衛 {heroLosses}　収支 {sign}{Mathf.Abs(streamEarnings):N0}G",
        };
        if (carried > 0) lines.Add($"持ち込んだ {carried:N0}G は戻らない");
        lines.Add($"村へ +{villageFunds:N0}G");
        reviewText.text = string.Join("\n", lines);
    }
}
