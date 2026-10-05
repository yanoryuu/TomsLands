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
    [Tooltip("方針プリセット（未配線ならおすすめ順で動作）")]
    [SerializeField] private TMP_Dropdown strategyDropdown;

    /// <summary>購入ボタンが押されたときに、入力された予算額を通知する。</summary>
    public Subject<int> OnConfirmClicked { get; } = new();

    /// <summary>選択中の方針プリセット（ドロップダウン未配線時はおすすめ順）。</summary>
    public AutoBuyStrategy SelectedStrategy =>
        strategyDropdown != null ? (AutoBuyStrategy)strategyDropdown.value : AutoBuyStrategy.Recommend;

    private int playerMoney;

    private void Awake()
    {
        fullAmountButton.onClick.AddListener(SetFullAmount);
        halfAmountButton.onClick.AddListener(SetHalfAmount);
        confirmButton.onClick.AddListener(HandleConfirm);
        cancelButton.onClick.AddListener(Hide);

        // 選択肢はコードで確定させる（シーン側は空のTMP_Dropdownを置くだけでよい）
        if (strategyDropdown != null)
        {
            strategyDropdown.ClearOptions();
            strategyDropdown.AddOptions(new System.Collections.Generic.List<string>
            {
                "おすすめ順",
                "次ダンジョン特化",
                "割安買い",
                "配当重視",
            });
        }

        // 注意: ここで SetActive(false) してはいけない。
        // シーン上で非アクティブ始まりのため Awake は初回 Show() 中に走り、
        // 最後に消すと「1回目のオート購入でポップアップが出ない」バグになる
        // （初期非表示はシーン側の初期状態で担保する）。
    }

    /// <summary>
    /// ポップアップを開く。playerMoney を渡すと初期値・上限に使われる。
    /// </summary>
    public void Show(int currentPlayerMoney)
    {
        playerMoney = currentPlayerMoney;
        budgetInputField.text = currentPlayerMoney.ToString();
        if (playerMoneyText != null)
            playerMoneyText.text = $"所持金: {currentPlayerMoney:N0}G";
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
