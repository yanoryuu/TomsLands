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
            titleText.text = "納付金が払えない！！";

        if (messageText != null)
            messageText.text = "ト、トコちゃん！！\nお店が差し押さえられてしまいました！";

        if (turnText != null)
            turnText.text = $"{DayText.DayOf(turn)}で力尽きました……";
    }

    /// <summary>
    /// このランの振り返りを出す（所持金の推移・配信の成績・持ち込みの行方・村へ入る額）。未配線の部品は飛ばす。
    /// </summary>
    public void ShowReview(IReadOnlyList<int> moneyHistory, int heroWins, int heroLosses,
        int streamEarnings, int carried, int villageFunds, VillageFundsBreakdown breakdown = null)
    {
        if (moneyChart != null && moneyHistory != null && moneyHistory.Count > 0)
        {
            var points = new List<int>(moneyHistory);
            if (points.Count == 1) points.Insert(0, points[0]);
            moneyChart.SetData(points);
        }

        if (reviewText == null) return;
        string sign = streamEarnings >= 0 ? "+" : "-"; // U+2212 は破産画面のフォントに無い（豆腐になる）
        var lines = new List<string>
        {
            $"配信　討伐 {heroWins}・防衛 {heroLosses}　収支 {sign}{Mathf.Abs(streamEarnings):N0}G",
        };
        if (carried > 0) lines.Add($"持ち込んだ {carried:N0}G は戻らない");
        if (villageFunds >= 0) // 体験版は -1（村が無い）
        {
            // 内訳は同じ行に小さく添える（欄は 1000x190 で行を増やすと文字が縮むため）
            string detail = breakdown != null ? breakdown.BuildInlineText() : "";
            lines.Add(string.IsNullOrEmpty(detail)
                ? $"村へ +{villageFunds:N0}G"
                : $"村へ +{villageFunds:N0}G<size=70%>（{detail}）</size>");
        }
        reviewText.text = string.Join("\n", lines);
    }

    /// <summary>体験版: ボタンを「次へ」にする（押すと「製品版へ」を出す。村へは戻らない）。</summary>
    public void ApplyDemoLabels()
    {
        SetButtonLabel(retryButton, "次へ");
    }
}
