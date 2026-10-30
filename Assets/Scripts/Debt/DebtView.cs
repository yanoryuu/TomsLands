using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebtView : MonoBehaviour
{
    [Header("パネル本体")]
    [SerializeField] private GameObject debtPanel;

    [Header("返済UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI debtAmountText;
    [SerializeField] private TextMeshProUGUI currentMoneyText;
    [SerializeField] private TextMeshProUGUI insufficientText;
    [SerializeField] private Button payButton;
    [SerializeField] private Button closeButton;

    [Header("破産UI")]
    [SerializeField] private GameObject bankruptcyPanel;
    [SerializeField] private TextMeshProUGUI bankruptcyMessageText;
    [SerializeField] private Button goToResultButton;

    public Subject<Unit> OnPayClicked { get; } = new();
    public Subject<Unit> OnCloseClicked { get; } = new();
    public Subject<Unit> OnGoToResultClicked { get; } = new();

    /// <summary>返済パネル（破産を含む）を開いているか（朝の通知の待ち合わせ用。F-4）。</summary>
    public bool IsShowing => debtPanel != null && debtPanel.activeSelf && !IsClosing;

    /// <summary>閉じ演出の途中か（見た目はまだ出ているが、閉じたものとして扱う。支払いの二度押しを弾く）。</summary>
    public bool IsClosing => UIMotion.IsClosing(debtPanel);

    private void Awake()
    {
        // 閉じ演出（0.15 秒）の間に押されても二度払わない
        payButton.onClick.AddListener(() => { if (!IsClosing) OnPayClicked.OnNext(Unit.Default); });
        if (closeButton != null)
            closeButton.onClick.AddListener(() => { if (!IsClosing) OnCloseClicked.OnNext(Unit.Default); });
        if (goToResultButton != null)
            goToResultButton.onClick.AddListener(() => OnGoToResultClicked.OnNext(Unit.Default));

        // 起動時は演出なしで閉じる
        if (debtPanel != null) debtPanel.SetActive(false);
    }

    /// <summary>#1 閉じていればぽよんと開く（開いたまま中身だけ替えるときは開き直さない）。開いたら true。</summary>
    private bool OpenPanel()
    {
        bool opening = !debtPanel.activeSelf || IsClosing;
        if (opening) UIMotion.Open(debtPanel, MotionWindow.Of(debtPanel));
        return opening;
    }

    /// <summary>#2 開いた瞬間に納付額を 0 から数え上げる（開いたままの更新は差し替えるだけ）。</summary>
    private void ShowAmount(string label, int debtAmount, bool count)
    {
        if (debtAmountText == null) return;
        string Format(long v) => $"{label}：{v:#,0}G";
        if (count && debtAmount > 0) UIMotion.CountTo(debtAmountText, 0, debtAmount, Format, 0.5f);
        else UIMotion.CountTo(debtAmountText, debtAmount, debtAmount, Format);
    }

    /// <summary>
    /// 支払い可能な返済UIを表示する。forced=true のとき閉じるボタンを非表示にする。
    /// title / amountLabel は村の銀行 Lv5「勝負の融資」の返済で見出しと額の呼び名を変えるときに渡す（null なら納付）。
    /// </summary>
    public void ShowPayment(int debtAmount, int currentMoney, int cycle, bool forced, string title = null, string amountLabel = null)
    {
        bool opened = OpenPanel();
        if (bankruptcyPanel != null) bankruptcyPanel.SetActive(false);

        if (titleText != null) titleText.text = title ?? $"第{cycle}回 納付日！";
        ShowAmount(amountLabel ?? DefaultAmountLabel, debtAmount, opened);
        if (currentMoneyText != null) currentMoneyText.text = $"所持金：{currentMoney:#,0}G";

        bool canPay = currentMoney >= debtAmount;
        payButton.interactable = canPay;
        if (insufficientText != null)
        {
            // プレハブの文字は仮の「Message」のまま（どこも書き換えていなかった）なので、足りない額をここで入れる
            if (!canPay) insufficientText.text = $"所持金が {debtAmount - currentMoney:#,0}G 足りません";
            insufficientText.gameObject.SetActive(!canPay);
        }
        if (closeButton != null) closeButton.gameObject.SetActive(!forced);
    }

    /// <summary>
    /// 返済不能（ゲームオーバー）UIを表示する。強制モード専用。
    /// </summary>
    public void ShowBankruptcy(int debtAmount, int currentMoney, int cycle, string title = null, string amountLabel = null)
    {
        bool opened = OpenPanel();
        if (bankruptcyPanel != null)
        {
            bool wasShown = bankruptcyPanel.activeSelf;
            bankruptcyPanel.SetActive(true);
            // 破産の知らせは窓の上でもう一度弾ませる
            if (!wasShown && !opened) UIMotion.Bump(bankruptcyPanel.transform, 1.08f, 0.3f);
        }
        if (closeButton != null) closeButton.gameObject.SetActive(false);
        payButton.interactable = false;

        string label = amountLabel ?? DefaultAmountLabel;
        if (titleText != null) titleText.text = title ?? $"第{cycle}回 納付日！";
        ShowAmount(label, debtAmount, opened);
        if (currentMoneyText != null) currentMoneyText.text = $"所持金：{currentMoney:#,0}G";
        // 表示・非表示は従来どおり触らない（前の状態のまま）。出ているときに仮の「Message」が見えないよう文字だけ入れる
        if (insufficientText != null) insufficientText.text = $"所持金が {Mathf.Max(0, debtAmount - currentMoney):#,0}G 足りません";
        if (bankruptcyMessageText != null)
            bankruptcyMessageText.text = $"{label} {debtAmount:#,0}G に対して所持金が {currentMoney:#,0}G……！\nト、トコちゃん！　お店が差し押さえられました！！";
    }

    private const string DefaultAmountLabel = "納付金";

    /// <summary>#1 縮んで閉じる（省略モード・非表示中は即座に非表示）。</summary>
    public void Hide()
    {
        UIMotion.Close(debtPanel, null, MotionWindow.Of(debtPanel));
    }
}
