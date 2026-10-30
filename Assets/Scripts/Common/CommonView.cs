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

    [Header("次の配信までの表示（G-3・K-11） ※未配線でも動作する")]
    [SerializeField] private TextMeshProUGUI streamInfoText;

    [SerializeField] private Button menuButton;

    [Header("所持金カウントアップ演出")]
    [SerializeField] private float moneyCountDuration = 0.5f;
    [Tooltip("増減が所持金のこの割合を超えたら、カウントを長めにする")]
    [SerializeField] private float bigChangeRate = 0.3f;
    [SerializeField] private float bigChangeDuration = 0.9f;
    [SerializeField] private Color gainColor = new(0.91f, 0.64f, 0.24f, 1f);  // #E8A33D
    [SerializeField] private Color lossColor = new(0.66f, 0.2f, 0.17f, 1f);   // #A8332B
    [Tooltip("増減を浮かせて見せる時間（秒）")]
    [SerializeField] private float deltaFloatDuration = 0.8f;

    public Subject<Unit> OnMenuButtonClicked { get; } = new();

    // 現在表示中の金額。カウントアップアニメの起点に使う。
    private int displayedMoney;
    private int targetMoney;
    private bool moneyInitialized;
    private bool moneyHeld;
    private Tween moneyTween;
    private Tween colorTween;
    private Color moneyBaseColor = Color.white;

    private void Awake()
    {
        menuButton.onClick.AddListener(() => OnMenuButtonClicked.OnNext(Unit.Default));
        if (playerMoneyText != null) moneyBaseColor = playerMoneyText.color;
    }

    public void UpdatePlayerMoney(int money)
    {
        targetMoney = money;

        // 初回はアニメーションせず即時表示する
        if (!moneyInitialized)
        {
            moneyInitialized = true;
            displayedMoney = money;
            SetMoneyText(money);
            return;
        }

        // 朝レポートを読んでいる間は、閉じたときにまとめて数え上げる（F-4）
        if (moneyHeld) return;

        AnimateMoney(displayedMoney, money);
    }

    /// <summary>
    /// 所持金の表示を from に据え置く（朝レポートを開いている間など）。
    /// 実際の所持金は変えない。ReleaseMoneyDisplay で今の額まで数え上げる。
    /// </summary>
    public void HoldMoneyDisplay(int from)
    {
        moneyHeld = true;
        moneyTween?.Kill();
        displayedMoney = from;
        moneyInitialized = true;
        SetMoneyText(from);
    }

    /// <summary>据え置いた表示を今の所持金まで数え上げる。</summary>
    public void ReleaseMoneyDisplay()
    {
        if (!moneyHeld) return;
        moneyHeld = false;
        AnimateMoney(displayedMoney, targetMoney);
    }

    /// <summary>from → to へ数え上げ、増減を色と浮かぶ差分で見せる（F-5）。</summary>
    private void AnimateMoney(int from, int to)
    {
        // 直前のカウントアップが残っていれば停止して競合を防ぐ
        moneyTween?.Kill();
        int delta = to - from;
        if (delta == 0)
        {
            displayedMoney = to;
            SetMoneyText(to);
            return;
        }

        float duration = Mathf.Abs(delta) > Mathf.Max(1, Mathf.Abs(from)) * bigChangeRate ? bigChangeDuration : moneyCountDuration;
        // #2 所持金 HUD は従来のカウント・色・浮く差分のまま（MoneyFx に置き換えると二重に数えるため）。
        // 再生だけ UIMotion に通し、速い設定・省略モード（即最終の額）に従わせる
        moneyTween = UIMotion.Run(DOTween.To(() => from, value =>
            {
                displayedMoney = value;
                SetMoneyText(value);
            }, to, duration)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                displayedMoney = to;
                SetMoneyText(to);
            }), gameObject);

        FlashMoney(delta > 0 ? gainColor : lossColor, duration);
        if (delta > 0 && playerMoneyText != null) UIMotion.Bump(playerMoneyText.transform, 1.12f, 0.25f);
        FloatDelta(delta);
    }

    private void FlashMoney(Color color, float duration)
    {
        if (playerMoneyText == null) return;
        colorTween?.Kill();
        playerMoneyText.color = color;
        colorTween = UIMotion.Run(playerMoneyText.DOColor(moneyBaseColor, 0.35f).SetDelay(duration), playerMoneyText.gameObject);
    }

    /// <summary>所持金の下に「+1,200G」「-800G」を浮かせて消す。</summary>
    private void FloatDelta(int delta)
    {
        if (playerMoneyText == null || !playerMoneyText.gameObject.activeInHierarchy || UIMotion.IsSkip) return;

        var go = Instantiate(playerMoneyText.gameObject, playerMoneyText.transform.parent);
        go.name = "MoneyDelta";
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.fontSize = playerMoneyText.fontSize * 0.7f;
        tmp.color = delta > 0 ? gainColor : lossColor;
        tmp.text = delta > 0 ? $"+{delta:N0}G" : $"-{-delta:N0}G";
        var rt = (RectTransform)go.transform;
        var start = ((RectTransform)playerMoneyText.transform).anchoredPosition + new Vector2(0f, -78f);
        rt.anchoredPosition = start;
        UIMotion.Run(rt.DOAnchorPosY(start.y + 26f, deltaFloatDuration).SetEase(Ease.OutCubic), go);
        UIMotion.Run(tmp.DOFade(0f, deltaFloatDuration * 0.5f).SetDelay(deltaFloatDuration * 0.5f)
            .OnComplete(() => Destroy(go)), go);
    }

    // 例: 1,234,567G
    private void SetMoneyText(int money) => playerMoneyText.text = $"{money:N0}G";

    /// <summary>
    /// ターン表示を更新する。バズ中は残りターン数を併記する
    /// （バズオーバーレイ側の「残りNターン」が小さく気づかれにくいため、ここにも出す）。
    /// </summary>
    public void UpdateCurrentTurn(int turn, string buzzInfo = null)
    {
        string text = string.IsNullOrEmpty(buzzInfo)
            ? DayText.DayOf(turn)
            : $"{DayText.DayOf(turn)} <size=60%>{buzzInfo}</size>";
        // #16 日が進んだときだけ日めくり（最初の表示・バズの併記だけの変化は差し替えるだけ）
        if (shownTurn >= 0 && turn != shownTurn)
            FlipCounter.Flip(currentTurnText, text);
        else
            currentTurnText.text = text;
        shownTurn = turn;
    }

    private int shownTurn = -1;

    /// <summary>次の配信までの表示を更新する。空文字なら隠す。未配線なら何もしない。</summary>
    public void UpdateStreamInfo(string text)
    {
        if (streamInfoText == null) return;
        bool show = !string.IsNullOrEmpty(text);
        streamInfoText.gameObject.SetActive(show);
        if (!show) return;
        // #16 日が進んで残り日数が変わったら日めくり（最初の表示は差し替えるだけ）
        if (streamInfoShown) FlipCounter.Flip(streamInfoText, text);
        else streamInfoText.text = text;
        streamInfoShown = true;
    }

    private bool streamInfoShown;

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
        colorTween?.Kill();
    }
}
