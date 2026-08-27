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

    private void Awake()
    {
        payButton.onClick.AddListener(() => OnPayClicked.OnNext(Unit.Default));
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseClicked.OnNext(Unit.Default));
        if (goToResultButton != null)
            goToResultButton.onClick.AddListener(() => OnGoToResultClicked.OnNext(Unit.Default));

        Hide();
    }

    /// <summary>
    /// 支払い可能な返済UIを表示する。forced=true のとき閉じるボタンを非表示にする。
    /// </summary>
    public void ShowPayment(int debtAmount, int currentMoney, int cycle, bool forced)
    {
        debtPanel.SetActive(true);
        if (bankruptcyPanel != null) bankruptcyPanel.SetActive(false);

        if (titleText != null) titleText.text = $"第{cycle}回 税金納付日！";
        if (debtAmountText != null) debtAmountText.text = $"納税額：{debtAmount:#,0}G";
        if (currentMoneyText != null) currentMoneyText.text = $"所持金：{currentMoney:#,0}G";

        bool canPay = currentMoney >= debtAmount;
        payButton.interactable = canPay;
        if (insufficientText != null) insufficientText.gameObject.SetActive(!canPay);
        if (closeButton != null) closeButton.gameObject.SetActive(!forced);
    }

    /// <summary>
    /// 返済不能（ゲームオーバー）UIを表示する。強制モード専用。
    /// </summary>
    public void ShowBankruptcy(int debtAmount, int currentMoney, int cycle)
    {
        debtPanel.SetActive(true);
        if (bankruptcyPanel != null) bankruptcyPanel.SetActive(true);
        if (closeButton != null) closeButton.gameObject.SetActive(false);
        payButton.interactable = false;

        if (titleText != null) titleText.text = $"第{cycle}回 税金納付日！";
        if (debtAmountText != null) debtAmountText.text = $"納税額：{debtAmount:#,0}G";
        if (currentMoneyText != null) currentMoneyText.text = $"所持金：{currentMoney:#,0}G";
        if (bankruptcyMessageText != null)
            bankruptcyMessageText.text = $"納税額 {debtAmount:#,0}G に対して所持金が {currentMoney:#,0}G...!\nト、トムさん！お店が差し押さえられました！！";
    }

    public void Hide()
    {
        debtPanel.SetActive(false);
    }
}
