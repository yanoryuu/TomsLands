using System.Collections.Generic;
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

    /// <summary>Show で開いて、まだ閉じていないか（シーン上で最初から表示になっているのと区別する）。</summary>
    public bool IsOpenedByShow { get; private set; }

    private void Awake()
    {
        fullAmountButton.onClick.AddListener(SetFullAmount);
        halfAmountButton.onClick.AddListener(SetHalfAmount);
        confirmButton.onClick.AddListener(HandleConfirm);
        cancelButton.onClick.AddListener(Hide);

        // パッド操作: モーダル。B=キャンセル、初期フォーカスは「全額」（購入確定は誤爆防止のため避ける）
        var scope = UIFocusScope.GetOrAdd(gameObject);
        if (scope != null)
        {
            scope.Modal = true;
            scope.Priority = Mathf.Max(scope.Priority, 10);
            scope.FirstSelected = fullAmountButton;
            scope.RememberLastSelected = false; // 開くたびに「全額」から
            if (!scope.HasCancel) scope.SetCancelButton(cancelButton);
        }
        SetupPadNavigation();

        // 選択肢はコードで確定させる（シーン側は空のTMP_Dropdownを置くだけでよい）
        if (strategyDropdown != null)
        {
            strategyDropdown.ClearOptions();
            strategyDropdown.AddOptions(new List<string>
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
    /// パッド操作のナビゲーションを明示する（自動だと「キャンセル」に到達できず、背後の画面へ抜けようとするため）。
    /// 入力欄 [全額]
    ///        [半額]
    ///        [方針]
    /// [キャンセル] [購入]
    /// </summary>
    private void SetupPadNavigation()
    {
        Selectable strategy = strategyDropdown != null && strategyDropdown.gameObject.activeSelf ? strategyDropdown : null;
        SetNav(budgetInputField, up: null, down: cancelButton, left: null, right: fullAmountButton);
        SetNav(fullAmountButton, up: null, down: halfAmountButton, left: budgetInputField, right: null);
        SetNav(halfAmountButton, up: fullAmountButton, down: strategy != null ? strategy : confirmButton, left: budgetInputField, right: null);
        if (strategy != null)
            SetNav(strategy, up: halfAmountButton, down: null, left: confirmButton, right: null);
        SetNav(confirmButton, up: budgetInputField, down: null, left: cancelButton, right: strategy);
        SetNav(cancelButton, up: budgetInputField, down: null, left: null, right: confirmButton);
    }

    private static void SetNav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        if (s == null) return;
        s.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = up,
            selectOnDown = down,
            selectOnLeft = left,
            selectOnRight = right,
        };
    }

    /// <summary>
    /// ポップアップを開く。playerMoney を渡すと初期値・上限に使われる。
    /// </summary>
    public void Show(int currentPlayerMoney) => Show(currentPlayerMoney, false);

    /// <summary>
    /// ポップアップを開く。locked=true のとき（初回チュートリアル）は予算=所持金全額で固定し、
    /// 予算入力・全額/半分・キャンセルを押せなくして、方針の選択と「購入」だけを押せる状態にする。
    /// </summary>
    public void Show(int currentPlayerMoney, bool locked)
    {
        playerMoney = currentPlayerMoney;
        budgetInputField.text = currentPlayerMoney.ToString();
        if (playerMoneyText != null)
            playerMoneyText.text = $"所持金: {currentPlayerMoney:N0}G";

        bool editable = !locked;
        budgetInputField.interactable = editable;
        if (fullAmountButton != null) fullAmountButton.interactable = editable;
        if (halfAmountButton != null) halfAmountButton.interactable = editable;
        if (cancelButton != null) cancelButton.interactable = editable;
        if (strategyDropdown != null)
        {
            // 初回チュートリアルでも方針だけは選べる（K-5。既定はおすすめ順）
            strategyDropdown.interactable = true;
            if (locked) strategyDropdown.SetValueWithoutNotify((int)AutoBuyStrategy.Recommend);
        }
        if (confirmButton != null) confirmButton.interactable = true;

        IsOpenedByShow = true;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        IsOpenedByShow = false;
        gameObject.SetActive(false);
    }

    private void SetFullAmount()
    {
        budgetInputField.text = playerMoney.ToString();
        PlaySE("営業/SE_数の増減");
    }

    private void SetHalfAmount()
    {
        budgetInputField.text = (playerMoney / 2).ToString();
        PlaySE("営業/SE_数の増減");
    }

    private void HandleConfirm()
    {
        if (!int.TryParse(budgetInputField.text, out int budget))
            budget = 0;
        budget = Mathf.Clamp(budget, 0, playerMoney);
        OnConfirmClicked.OnNext(budget);
    }

    private static void PlaySE(string key)
    {
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlaySE(key);
    }
}
