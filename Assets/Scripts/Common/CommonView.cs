using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CommonView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerMoneyText;

    [SerializeField] private TextMeshProUGUI currentTurnText;

    [Header("売り注文の入金予定バッジ ※未配線でも動作する")]
    [SerializeField] private TextMeshProUGUI pendingIncomeText;

    [SerializeField] private Button menuButton;

    [Header("所持金カウントアップ演出")]
    [SerializeField] private float moneyCountDuration = 0.5f;

    public Subject<Unit> OnMenuButtonClicked { get; } = new();

    // 現在表示中の金額。カウントアップアニメの起点に使う。
    private int displayedMoney;
    private bool moneyInitialized;
    private Tween moneyTween;


    private void Awake()    {
        menuButton.onClick.AddListener(() => OnMenuButtonClicked.OnNext(Unit.Default));
    }

    public void UpdatePlayerMoney(int money)
    {
        // 初回はアニメーションせず即時表示する
        if (!moneyInitialized)
        {
            moneyInitialized = true;
            displayedMoney = money;
            SetMoneyText(money);
            return;
        }

        // 直前のカウントアップが残っていれば停止して競合を防ぐ
        moneyTween?.Kill();

        int from = displayedMoney;
        moneyTween = DOTween.To(() => from, value =>
            {
                displayedMoney = value;
                SetMoneyText(value);
            }, money, moneyCountDuration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                displayedMoney = money;
                SetMoneyText(money);
            });
    }

    // 例: 1,234,567G
    private void SetMoneyText(int money)
    {
        playerMoneyText.text = $"{money:N0}G";
    }

    public void UpdateCurrentTurn(int turn)
    {
        currentTurnText.text = $"Turn: {turn}";
    }

    /// <summary>
    /// 未約定の売り注文の見込み入金額バッジを更新する。0 なら非表示。
    /// テキストが未配線（null）の場合は何もしない。
    /// </summary>
    public void UpdatePendingIncome(int amount)
    {
        if (pendingIncomeText == null) return;

        pendingIncomeText.gameObject.SetActive(amount > 0);
        if (amount > 0)
            pendingIncomeText.text = $"入金予定 +{amount:N0}G";
    }

    private void OnDestroy()
    {
        moneyTween?.Kill();
    }
}
