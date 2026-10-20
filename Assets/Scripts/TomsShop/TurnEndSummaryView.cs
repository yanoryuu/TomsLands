using System.Collections.Generic;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ターン終了時のサマリーパネルView。
/// 売れた数・売上・アイテムトレンド・次の配信までの日数を表示する。
/// パネルの表示/非表示は GamePanelManager が管理する。
/// </summary>
public class TurnEndSummaryView : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject rowPrefab;

    [Header("Summary Texts")]
    [SerializeField] private TextMeshProUGUI totalRevenueText;
    [SerializeField] private TextMeshProUGUI totalSoldCountText;
    [SerializeField] private TextMeshProUGUI daysUntilBattleText;

    [Header("売り注文（遅延約定）表示 ※未配線でも動作する")]
    [Tooltip("本日約定した売り注文の入金額（サマリーの主役。カウントアップする）")]
    [SerializeField] private TextMeshProUGUI settledIncomeText;
    [Tooltip("本日出した売り注文の見込み額（明日の営業後に入金）")]
    [SerializeField] private TextMeshProUGUI pendingIncomeText;
    [Tooltip("本日の入金の値（サマリーの主役。左列1行目）。配線されていれば settledIncomeText（旧・シーン側の1行表示）は隠す")]
    [SerializeField] private TextMeshProUGUI incomeValueText;
    [Tooltip("明日の夜に入金の値（左列2行目）。配線されていれば pendingIncomeText（旧・シーン側の1行表示）は隠す")]
    [SerializeField] private TextMeshProUGUI pendingValueText;
    [Tooltip("本日の入金の見出し（入金が無く注文だけの日は「本日の注文」に変える）")]
    [SerializeField] private TextMeshProUGUI settledIncomeLabel;
    [Tooltip("明日入金の見出し")]
    [SerializeField] private TextMeshProUGUI pendingIncomeLabel;
    [Tooltip("本日の入金の明細（銘柄ごとの約定・倍率の内訳・実現損益）")]
    [SerializeField] private TextMeshProUGUI settlementDetailText;
    [Tooltip("「次の配信まで」の行（見出し・値・アイコンの親でなくてよい。隠すときに使う）")]
    [SerializeField] private GameObject[] daysUntilBattleRow;
    [Tooltip("「次の配信まで」の見出し（最後の配信なら「最後の配信まで」）")]
    [SerializeField] private TextMeshProUGUI daysUntilBattleLabel;

    [Header("本日の入金のカウントアップ")]
    [SerializeField] private float incomeCountDuration = 0.6f;
    [SerializeField] private Color incomeColor = new(0.91f, 0.64f, 0.24f, 1f); // #E8A33D

    [Header("ターン評価")]
    [SerializeField] private Image gradeImage;
    [SerializeField] private TextMeshProUGUI gradeCommentText;
    [SerializeField] private Sprite gradeS;
    [SerializeField] private Sprite gradeA;
    [SerializeField] private Sprite gradeB;
    [SerializeField] private Sprite gradeC;
    [SerializeField] private Sprite gradeD;
    [SerializeField] private Sprite gradeE;

    [Header("Confirm Button")]
    [SerializeField] private Button confirmButton;

    /// <summary>
    /// 確認ボタンが押された
    /// </summary>
    public Subject<Unit> OnConfirmClicked { get; } = new();

    private Tween incomeTween;
    private Color settledBaseColor = Color.white;
    private bool settledColorCaptured;

    private void OnDestroy() => incomeTween?.Kill();

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
        }
    }

    /// <summary>
    /// ターン評価（ランク画像とコメント）を表示する
    /// </summary>
    public void ShowGrade(TurnGrade grade, string comment)
    {
        if (gradeImage != null)
        {
            gradeImage.sprite = grade switch
            {
                TurnGrade.S => gradeS,
                TurnGrade.A => gradeA,
                TurnGrade.B => gradeB,
                TurnGrade.C => gradeC,
                TurnGrade.D => gradeD,
                _           => gradeE,
            };
        }

        if (gradeCommentText != null)
            gradeCommentText.text = comment;

        // ランク画像を弾ませる（瞬間表示にしない）
        if (gradeImage != null && gradeImage.gameObject.activeInHierarchy)
            UIFx.Pop(gradeImage.transform, 0.6f, 0.3f);
    }

    /// <summary>
    /// サマリーの内容を更新する
    /// </summary>
    public void UpdateContent(List<TurnEndSummaryItemData> items, int totalRevenue, int totalSoldCount, int daysUntilBattle)
    {
        ClearRows();

        foreach (var item in items)
        {
            var row = Instantiate(rowPrefab, contentParent);
            row.SetActive(true);

            var rowUI = row.GetComponent<TurnEndSummaryRowUI>();
            if (rowUI != null)
            {
                rowUI.Setup(item);
            }
        }

        // サマリーテキスト更新（主役は本日の入金。totalRevenueText は旧配線の互換用）
        if (totalRevenueText != null && totalRevenueText != settledIncomeText && totalRevenueText != pendingIncomeText)
            totalRevenueText.text = $"{totalRevenue:N0}G";

        if (totalSoldCountText != null)
            totalSoldCountText.text = $"{totalSoldCount}個";

        if (daysUntilBattleText != null)
            daysUntilBattleText.text = FormatDays(daysUntilBattle);
    }

    /// <summary>次の配信までの日数の表示（明日=1）。</summary>
    public static string FormatDays(int days) => days switch
    {
        < 0 => "予定なし",
        0 => "今日",
        1 => "明日",
        _ => $"あと{days}日",
    };

    /// <summary>「次の配信まで」の行の表示可否と、最後の配信かどうか（K-6・K-11）。</summary>
    public void SetDaysUntilStreamVisible(bool visible, bool finalStream)
    {
        if (daysUntilBattleRow != null)
            foreach (var go in daysUntilBattleRow) if (go != null) go.SetActive(visible);
        if (daysUntilBattleLabel != null)
            daysUntilBattleLabel.text = finalStream ? "最後の配信まで" : "次の配信まで";
    }

    /// <summary>本日の入金の明細を出す。空なら欄を隠す。</summary>
    public void UpdateSettlementDetail(string detail)
    {
        if (settlementDetailText == null) return;
        bool has = !string.IsNullOrEmpty(detail);
        settlementDetailText.gameObject.SetActive(has);
        if (has) settlementDetailText.text = detail;
    }

    /// <summary>
    /// 売り注文まわりの表示を更新する。
    /// テキストが未配線（null）の場合は何もしない。
    /// </summary>
    public void UpdateSettlementInfo(int settledIncome, int pendingEstimate)
    {
        // 入金が無く、注文だけ入った日（1日目など）は「注文が入った」を主役にする（K-3）
        bool orderOnly = settledIncome <= 0 && pendingEstimate > 0;
        bool labeled = settledIncomeLabel != null;

        if (settledIncomeLabel != null)
            settledIncomeLabel.text = orderOnly ? "本日の注文" : "本日の入金";
        if (pendingIncomeLabel != null)
            pendingIncomeLabel.text = orderOnly ? "入金" : "明日の夜に入金";

        // 左列の行に配線されていれば、旧・1行表示（シーン側で足したテキスト）は隠して行のほうを使う
        bool rows = incomeValueText != null;
        if (rows)
        {
            if (settledIncomeText != null && settledIncomeText != incomeValueText) settledIncomeText.gameObject.SetActive(false);
            if (pendingIncomeText != null && pendingIncomeText != pendingValueText) pendingIncomeText.gameObject.SetActive(false);
        }
        var income = rows ? incomeValueText : settledIncomeText;
        var pending = rows ? pendingValueText : pendingIncomeText;
        labeled &= rows;

        if (income != null)
        {
            int target = orderOnly ? pendingEstimate : settledIncome;
            string prefix = labeled ? "+" : (orderOnly ? "本日の注文 +" : "本日の入金 +");
            PlayIncomeCount(income, target, prefix);
        }

        if (pending != null)
        {
            if (orderOnly) pending.text = "明日の夜";
            else pending.text = labeled ? $"+{pendingEstimate:N0}G" : $"明日の夜に入金 +{pendingEstimate:N0}G";
        }
    }

    /// <summary>本日の入金を 0 から数え上げる（金色。0.6秒 OutCubic）。演出を速くする設定に従う。</summary>
    private void PlayIncomeCount(TextMeshProUGUI settledIncomeText, int target, string prefix)
    {
        if (!settledColorCaptured)
        {
            settledBaseColor = settledIncomeText.color;
            settledColorCaptured = true;
        }
        incomeTween?.Kill();
        settledIncomeText.color = target > 0 ? incomeColor : settledBaseColor;
        if (target <= 0 || !settledIncomeText.gameObject.activeInHierarchy)
        {
            settledIncomeText.text = $"{prefix}{target:N0}G";
            return;
        }
        int shown = 0;
        settledIncomeText.text = $"{prefix}0G";
        float duration = incomeCountDuration * GameSettings.EffectDurationScale;
        incomeTween = DOTween.To(() => shown, v => { shown = v; settledIncomeText.text = $"{prefix}{v:N0}G"; }, target, duration)
            .SetEase(Ease.OutCubic)
            .SetLink(settledIncomeText.gameObject)
            .OnComplete(() =>
            {
                settledIncomeText.text = $"{prefix}{target:N0}G";
                UIFx.Pop(settledIncomeText.transform, 1.15f, 0.25f);
            });
    }

    private void ClearRows()
    {
        if (contentParent == null) return;

        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            Destroy(contentParent.GetChild(i).gameObject);
        }
    }
}

