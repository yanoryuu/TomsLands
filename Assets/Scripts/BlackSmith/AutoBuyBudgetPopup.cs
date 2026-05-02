using TMPro;
using R3;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// まとめて購入の予算設定ポップアップ。
/// BlackSmithView に SerializeField として設定し、BlackSmithPresenter から制御する。
/// </summary>
public class AutoBuyBudgetPopup : MonoBehaviour
{
    [SerializeField] private TMP_InputField budgetInputField;
    [SerializeField] private Button fullAmountButton;   // 全額ボタン
    [SerializeField] private Button halfAmountButton;   // 半分ボタン
    [SerializeField] private Button confirmButton;      // 購入ボタン
    [SerializeField] private Button cancelButton;       // キャンセルボタン
    [SerializeField] private TextMeshProUGUI playerMoneyText;  // 所持金表示テキスト

    /// <summary>購入ボタンが押されたときに、入力された予算額を通知する。</summary>
    public Subject<int> OnConfirmClicked { get; } = new();

    private int playerMoney;

    private void Awake()
    {
        fullAmountButton.onClick.AddListener(SetFullAmount);
        halfAmountButton.onClick.AddListener(SetHalfAmount);
        confirmButton.onClick.AddListener(HandleConfirm);
        cancelButton.onClick.AddListener(Hide);

        gameObject.SetActive(false);
    }

    /// <summary>
    /// ポップアップを開く。playerMoney を渡すと初期値・上限に使われる。
    /// </summary>
    public void Show(int currentPlayerMoney)
    {
        playerMoney = currentPlayerMoney;
        budgetInputField.text = currentPlayerMoney.ToString();
        if (playerMoneyText != null)
            playerMoneyText.text = $"所持金: {currentPlayerMoney}G";
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void SetFullAmount()
    {
        budgetInputField.text = playerMoney.ToString();
        SoundManager.Instance?.PlaySE("営業/SE_数の増減");
    }

    private void SetHalfAmount()
    {
        budgetInputField.text = (playerMoney / 2).ToString();
        SoundManager.Instance?.PlaySE("営業/SE_数の増減");
    }

    private void HandleConfirm()
    {
        if (!int.TryParse(budgetInputField.text, out int budget))
            budget = 0;
        budget = Mathf.Clamp(budget, 0, playerMoney);
        OnConfirmClicked.OnNext(budget);
    }
}
