using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// リザルト画面のView。
/// 演出の流れ: 「営業終了」カットイン → （トコの会話中は暗幕）→ 統計を1項目ずつ表示（数字はカウントアップ）
/// → 所持金グラフ・ハイライト → ランクのハンコ → （ランクSなら紙吹雪とトコ）→ ボタン有効化。
/// 演出中は画面クリックでその段階を最後まで飛ばせる。全参照は未配線でも動く（null安全）。
/// </summary>
public class ResultView : MonoBehaviour
{
    [Header("=== ランク表示 ===")]
    [Tooltip("ランク文字のフォールバック（rankLetterImage にスプライトが無いとき使う）")]
    [SerializeField] private TextMeshProUGUI rankText;

    [Header("=== 資産情報 ===")]
    [SerializeField] private TextMeshProUGUI finalMoneyText;
    [SerializeField] private TextMeshProUGUI moneyDifferenceText;
    [SerializeField] private TextMeshProUGUI stockValueText;
    [SerializeField] private TextMeshProUGUI netWorthText;

    [Header("=== ゲーム情報 ===")]
    [SerializeField] private TextMeshProUGUI totalTurnsText;
    [SerializeField] private TextMeshProUGUI blacksmithLevelText;
    [SerializeField] private TextMeshProUGUI infoBrokerLevelText;

    [Header("=== ボタン ===")]
    [SerializeField] private Button goToTitleButton;
    [SerializeField] private Button retryButton;

    [Header("=== 画面全体 ===")]
    [Tooltip("開始時の黒フェード（alpha 1 → 0）")]
    [SerializeField] private CanvasGroup screenFader;
    [Tooltip("トコの会話中に背景を暗くする暗幕")]
    [SerializeField] private CanvasGroup talkDimmer;
    [SerializeField] private ResultSkipCatcher skipCatcher;

    [Header("=== 営業終了カットイン ===")]
    [SerializeField] private CanvasGroup cutInRoot;
    [Tooltip("画面を横切る帯（縦にひらく）")]
    [SerializeField] private RectTransform cutInBand;
    [Tooltip("看板（左から滑り込む）")]
    [SerializeField] private RectTransform cutInSign;

    [Header("=== パネル ===")]
    [SerializeField] private RectTransform statsPanel;
    [SerializeField] private RectTransform reviewPanel;
    [SerializeField] private RectTransform buttonsRoot;

    [Header("=== ランクのハンコ ===")]
    [SerializeField] private RectTransform rankStamp;
    [SerializeField] private Image rankLetterImage;
    [Tooltip("S, A, B, C, D の順")]
    [SerializeField] private Sprite[] rankSprites;
    [Tooltip("押した瞬間に広がって消えるインクの輪")]
    [SerializeField] private Image stampFlash;
    [SerializeField] private RectTransform rankLaurel;

    [Header("=== 所持金グラフ ===")]
    [SerializeField] private PriceChartView moneyChart;
    [SerializeField] private ResultChartFill moneyChartFill;
    [Tooltip("左から右へ線を描いていくためのマスク")]
    [SerializeField] private RectMask2D chartRevealMask;
    [Tooltip("グラフ終点の印")]
    [SerializeField] private RectTransform chartEndMarker;

    [Header("=== ハイライト ===")]
    [SerializeField] private RectTransform[] highlightCards;
    [SerializeField] private Image bestItemIcon;
    [SerializeField] private TextMeshProUGUI bestItemNameText;
    [SerializeField] private TextMeshProUGUI bestItemRevenueText;
    [SerializeField] private TextMeshProUGUI streamRecordText;
    [SerializeField] private TextMeshProUGUI buzzCountText;

    [Header("=== ランクS演出 ===")]
    [SerializeField] private ResultConfetti confetti;
    [SerializeField] private RectTransform tokoPortrait;
    [SerializeField] private Image tokoImage;
    [Tooltip("登場の瞬間の表情（ドヤ顔）")]
    [SerializeField] private Sprite tokoEnterSprite;
    [Tooltip("ハンコを見て笑う表情（大笑い）")]
    [SerializeField] private Sprite tokoLaughSprite;
    [Tooltip("喜んだあと画面外へはけるまでの時間（秒）")]
    [SerializeField] private float tokoStaySeconds = 1.6f;

    [Header("=== SE（SoundManager のキー）===")]
    [SerializeField] private string cutInSe = "営業/SE_仕入れ完了";
    [SerializeField] private string rowSe = "営業/SE_数の増減";
    [SerializeField] private string highlightSe = "営業/SE_売上音";
    [SerializeField] private string stampSe = "営業/SE_開発完了";

    private static readonly Color PlusColor = new(0.62f, 0.88f, 0.45f);
    private static readonly Color MinusColor = new(1f, 0.52f, 0.45f);

    /// <summary>村へ戻るボタンが押された</summary>
    public Subject<Unit> OnGoToTitleClicked { get; } = new();

    /// <summary>もう一つのボタンが押された（どちらも村へ戻る）</summary>
    public Subject<Unit> OnRetryClicked { get; } = new();

    private Sequence _current;
    private bool _skipping;
    private readonly Dictionary<RectTransform, Vector2> _homePositions = new();

    private void Awake()
    {
        if (goToTitleButton != null)
            goToTitleButton.onClick.AddListener(() => OnGoToTitleClicked.OnNext(Unit.Default));

        if (retryButton != null)
            retryButton.onClick.AddListener(() => OnRetryClicked.OnNext(Unit.Default));

        if (skipCatcher != null)
            skipCatcher.Clicked += Skip;

        foreach (var rt in new[] { statsPanel, reviewPanel, cutInSign, tokoPortrait, buttonsRoot })
            if (rt != null) _homePositions[rt] = rt.anchoredPosition;
    }

    private void OnDestroy()
    {
        if (skipCatcher != null) skipCatcher.Clicked -= Skip;
    }

    // =====================================================
    // 準備
    // =====================================================

    /// <summary>
    /// 値を設定し、すべての要素を「出る前」の状態にする（シーン開始直後に呼ぶ）。
    /// </summary>
    public void Prepare(ResultStatisticsData data)
    {
        UpdateContent(data);

        SetAlpha(screenFader, 1f);
        SetAlpha(talkDimmer, 0f);
        SetAlpha(cutInRoot, 0f);
        Hide(statsPanel);
        Hide(reviewPanel);
        Hide(buttonsRoot);
        foreach (var row in Rows()) Hide(row);
        if (highlightCards != null) foreach (var c in highlightCards) Hide(c);
        Hide(rankStamp);
        Hide(rankLaurel);
        if (stampFlash != null) stampFlash.gameObject.SetActive(false);
        if (chartEndMarker != null) chartEndMarker.gameObject.SetActive(false);
        if (tokoPortrait != null) tokoPortrait.gameObject.SetActive(false);
        SetChartReveal(0f);
        SetButtonsInteractable(false);
        SetSkipEnabled(true);
    }

    /// <summary>リザルト画面の内容を（演出なしで）設定する。</summary>
    public void UpdateContent(ResultStatisticsData data)
    {
        if (data == null) return;

        SetText(finalMoneyText, FormatGold(data.FinalMoney));
        SetText(moneyDifferenceText, FormatSigned(data.MoneyDifference));
        if (moneyDifferenceText != null)
            moneyDifferenceText.color = data.MoneyDifference >= 0 ? PlusColor : MinusColor;
        SetText(stockValueText, FormatGold(data.TotalStockValue));
        SetText(netWorthText, FormatGold(data.NetWorth));
        SetText(totalTurnsText, $"{data.TotalTurns}");
        SetText(blacksmithLevelText, $"Lv.{data.BlacksmithLevel}");
        SetText(infoBrokerLevelText, $"Lv.{data.InfoBrokerLevel}");

        // --- ランク ---
        var rankSprite = GetRankSprite(data.Rank);
        if (rankLetterImage != null)
        {
            rankLetterImage.sprite = rankSprite;
            rankLetterImage.enabled = rankSprite != null;
            if (rankSprite != null) rankLetterImage.preserveAspect = true;
        }
        if (rankText != null)
        {
            rankText.text = data.Rank;
            rankText.color = GetRankColor(data.Rank);
            rankText.enabled = rankSprite == null || rankLetterImage == null;
        }

        // --- グラフ ---
        var history = data.MoneyHistory ?? new List<int>();
        var points = new List<int>(history);
        if (points.Count == 1) points.Insert(0, points[0]);
        if (moneyChart != null) moneyChart.SetData(points);
        if (moneyChartFill != null) moneyChartFill.SetData(points);

        // --- ハイライト ---
        bool hasBest = !string.IsNullOrEmpty(data.BestItemName);
        SetText(bestItemNameText, hasBest ? data.BestItemName : "-");
        SetText(bestItemRevenueText, hasBest ? FormatSigned(data.BestItemRevenue) : "");
        if (bestItemIcon != null)
        {
            bestItemIcon.sprite = data.BestItemIcon;
            bestItemIcon.enabled = data.BestItemIcon != null;
            bestItemIcon.preserveAspect = true;
        }
        SetText(streamRecordText, $"{data.StreamWins}勝 {data.StreamLosses}敗");
        SetText(buzzCountText, $"{data.BuzzCount}回");
    }

    /// <summary>演出が失敗したときの保険: すべてを演出なしで表示する。</summary>
    public void ShowAllImmediate(ResultStatisticsData data)
    {
        if (_current != null && _current.IsActive()) _current.Kill();
        UpdateContent(data);
        SetAlpha(screenFader, 0f);
        SetAlpha(cutInRoot, 0f);
        SetAlpha(talkDimmer, 0f);
        foreach (var rt in new[] { statsPanel, reviewPanel, buttonsRoot, rankStamp, rankLaurel }) ShowNow(rt);
        foreach (var row in Rows()) ShowNow(row);
        if (highlightCards != null) foreach (var c in highlightCards) ShowNow(c);
        SetChartReveal(1f);
        PlaceChartEndMarker();
    }

    private static void ShowNow(RectTransform rt)
    {
        if (rt == null) return;
        rt.gameObject.SetActive(true);
        rt.localScale = Vector3.one;
        Group(rt).alpha = 1f;
    }

    // =====================================================
    // 段階1: 黒フェード → 営業終了カットイン（約2.3秒）
    // =====================================================

    public async UniTask PlayCutInAsync(CancellationToken ct)
    {
        var seq = DOTween.Sequence();
        if (screenFader != null)
            seq.Insert(0f, screenFader.DOFade(0f, 0.35f));

        if (cutInRoot != null && cutInSign != null)
        {
            Vector2 home = Home(cutInSign);
            float w = ((RectTransform)transform).rect.width;
            cutInRoot.gameObject.SetActive(true);
            cutInSign.anchoredPosition = home + new Vector2(-w, 0f);
            if (cutInBand != null) cutInBand.localScale = new Vector3(1f, 0f, 1f);

            seq.Insert(0.15f, cutInRoot.DOFade(1f, 0.15f));
            if (cutInBand != null) seq.Insert(0.15f, cutInBand.DOScaleY(1f, 0.22f).SetEase(Ease.OutCubic));
            seq.Insert(0.3f, cutInSign.DOAnchorPos(home, 0.38f).SetEase(Ease.OutBack));
            seq.InsertCallback(0.55f, () => PlaySe(cutInSe));
            seq.Insert(0.68f, cutInSign.DOPunchScale(new Vector3(0.06f, 0.06f, 0f), 0.3f, 6, 0.6f));
            // 1.0秒見せてから右へ抜ける
            seq.Insert(1.75f, cutInSign.DOAnchorPos(home + new Vector2(w, 0f), 0.32f).SetEase(Ease.InBack));
            if (cutInBand != null) seq.Insert(1.95f, cutInBand.DOScaleY(0f, 0.2f).SetEase(Ease.InCubic));
            seq.Insert(2.0f, cutInRoot.DOFade(0f, 0.2f));
        }

        await RunAsync(seq, ct);
        if (cutInRoot != null) cutInRoot.gameObject.SetActive(false);
    }

    // =====================================================
    // 段階2: トコの会話中の暗幕
    // =====================================================

    public void SetTalkDim(bool on)
    {
        if (talkDimmer == null) return;
        talkDimmer.DOKill();
        talkDimmer.blocksRaycasts = false;
        talkDimmer.DOFade(on ? 1f : 0f, 0.25f).SetLink(talkDimmer.gameObject);
    }

    // =====================================================
    // 段階3: 統計 → グラフ・ハイライト → ハンコ（約3.9秒）
    // =====================================================

    public async UniTask PlayRevealAsync(ResultStatisticsData data, CancellationToken ct)
    {
        if (data == null) return;
        var seq = DOTween.Sequence();

        // パネルが左右から入ってくる
        InsertSlideIn(seq, statsPanel, 0f, new Vector2(-80f, 0f), 0.32f);
        InsertSlideIn(seq, reviewPanel, 0.08f, new Vector2(80f, 0f), 0.32f);

        // 統計を1項目ずつ（数字はカウントアップ）
        float t = 0.45f;
        const float rowStep = 0.26f;
        InsertRow(seq, ref t, rowStep, finalMoneyText, v => FormatGold(v), data.FinalMoney, 0.5f);
        InsertRow(seq, ref t, rowStep, moneyDifferenceText, v => FormatSigned(v), data.MoneyDifference, 0.5f);
        InsertRow(seq, ref t, rowStep, stockValueText, v => FormatGold(v), data.TotalStockValue, 0.5f);
        InsertRow(seq, ref t, rowStep + 0.12f, netWorthText, v => FormatGold(v), data.NetWorth, 0.8f, punch: true);
        InsertRow(seq, ref t, rowStep, totalTurnsText, v => $"{v}", data.TotalTurns, 0.3f);
        InsertRow(seq, ref t, rowStep, blacksmithLevelText, v => $"Lv.{v}", data.BlacksmithLevel, 0.2f);
        InsertRow(seq, ref t, rowStep, infoBrokerLevelText, v => $"Lv.{v}", data.InfoBrokerLevel, 0.2f);

        // 所持金グラフを左から描く（統計と並行）
        const float chartStart = 0.6f, chartDur = 1.6f;
        if (chartRevealMask != null)
            seq.Insert(chartStart, DOTween.To(() => 0f, SetChartReveal, 1f, chartDur).SetEase(Ease.InOutSine));
        if (chartEndMarker != null)
        {
            seq.InsertCallback(chartStart + chartDur, PlaceChartEndMarker);
            seq.Insert(chartStart + chartDur, chartEndMarker.DOScale(1f, 0.25f).From(0f).SetEase(Ease.OutBack));
        }

        // ハイライトのカードがポンポンと出る
        float hT = Mathf.Max(t, chartStart + chartDur) + 0.1f;
        if (highlightCards != null)
        {
            for (int i = 0; i < highlightCards.Length; i++)
            {
                var card = highlightCards[i];
                if (card == null) continue;
                float at = hT + i * 0.14f;
                seq.InsertCallback(at, () => { Show(card); PlaySe(highlightSe); });
                seq.Insert(at, card.DOScale(1f, 0.3f).From(0.6f).SetEase(Ease.OutBack));
                seq.Insert(at, Group(card).DOFade(1f, 0.15f).From(0f));
            }
            hT += highlightCards.Length * 0.14f;
        }

        // 最後にランクのハンコ
        float stampAt = hT + 0.35f;
        InsertStamp(seq, stampAt);

        bool skipped = await RunAsync(seq, ct);

        // 飛ばした場合でもハンコの音は1回鳴らす
        if (skipped) PlaySe(stampSe);

        // ボタンは最後に出す
        if (buttonsRoot != null)
        {
            Show(buttonsRoot);
            // Pop は対象の Tween を DOKill するので、位置の Tween より先に呼ぶ
            UIFx.Pop(buttonsRoot, 0.9f, 0.2f);
            var home = Home(buttonsRoot);
            buttonsRoot.anchoredPosition = home + new Vector2(0f, -40f);
            buttonsRoot.DOAnchorPos(home, 0.25f).SetEase(Ease.OutCubic).SetLink(buttonsRoot.gameObject);
            var cg = Group(buttonsRoot);
            cg.DOKill();
            cg.DOFade(1f, 0.2f).SetLink(buttonsRoot.gameObject);
        }
    }

    // =====================================================
    // ランクS演出（紙吹雪 + トコ）。待たない（ボタンはすぐ押せる）
    // =====================================================

    public void PlayRankSCelebration()
    {
        if (confetti != null) confetti.Play();

        if (tokoPortrait == null) return;
        var home = Home(tokoPortrait);
        tokoPortrait.DOKill();
        tokoPortrait.gameObject.SetActive(true);
        if (tokoImage != null && tokoEnterSprite != null) tokoImage.sprite = tokoEnterSprite;
        tokoPortrait.anchoredPosition = home + new Vector2(0f, -900f);
        tokoPortrait.localScale = Vector3.one;

        var seq = DOTween.Sequence().SetLink(tokoPortrait.gameObject);
        seq.Append(tokoPortrait.DOAnchorPos(home, 0.45f).SetEase(Ease.OutBack));
        seq.AppendInterval(0.5f);
        seq.AppendCallback(() =>
        {
            if (tokoImage != null && tokoLaughSprite != null) tokoImage.sprite = tokoLaughSprite;
        });
        seq.Append(tokoPortrait.DOPunchScale(new Vector3(0.06f, 0.06f, 0f), 0.4f, 5, 0.5f));
        // 小さく跳ねて喜ぶ
        seq.Append(tokoPortrait.DOAnchorPosY(home.y + 40f, 0.18f).SetEase(Ease.OutQuad).SetLoops(4, LoopType.Yoyo));
        // 結果を隠し続けないよう、ひとしきり喜んだら画面外へはける
        seq.AppendInterval(tokoStaySeconds);
        seq.Append(tokoPortrait.DOAnchorPos(home + new Vector2(0f, -1000f), 0.45f).SetEase(Ease.InBack));
        seq.AppendCallback(() => tokoPortrait.gameObject.SetActive(false));
    }

    // =====================================================
    // ボタン・スキップ
    // =====================================================

    public void SetButtonsInteractable(bool interactable)
    {
        if (goToTitleButton != null) goToTitleButton.interactable = interactable;
        if (retryButton != null) retryButton.interactable = interactable;
    }

    public void SetSkipEnabled(bool enabled)
    {
        if (skipCatcher != null) skipCatcher.gameObject.SetActive(enabled);
    }

    /// <summary>再生中の段階を最後まで飛ばす（効果音は鳴らさない）。</summary>
    public void Skip()
    {
        if (_current == null || !_current.IsActive()) return;
        _skipping = true;
        _current.Complete(true);
    }

    // =====================================================
    // 内部
    // =====================================================

    /// <summary>段階の Sequence を再生し終わるまで待つ。スキップされたら true。</summary>
    private async UniTask<bool> RunAsync(Sequence seq, CancellationToken ct)
    {
        seq.SetLink(gameObject);
        _current = seq;
        _skipping = false;
        try
        {
            while (seq.IsActive() && !seq.IsComplete())
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            return _skipping;
        }
        finally
        {
            if (_current == seq) _current = null;
            _skipping = false;
        }
    }

    private void InsertSlideIn(Sequence seq, RectTransform rt, float at, Vector2 offset, float dur)
    {
        if (rt == null) return;
        var home = Home(rt);
        seq.InsertCallback(at, () => Show(rt));
        seq.Insert(at, rt.DOAnchorPos(home, dur).From(home + offset).SetEase(Ease.OutCubic));
        seq.Insert(at, Group(rt).DOFade(1f, dur * 0.8f).From(0f));
    }

    private void InsertRow(Sequence seq, ref float t, float step, TextMeshProUGUI valueText,
        Func<int, string> format, int target, float countDur, bool punch = false)
    {
        if (valueText == null) return;
        var row = RowOf(valueText);
        float at = t;
        t += step;

        seq.InsertCallback(at, () =>
        {
            Show(row);
            valueText.text = format(0);
            PlaySe(rowSe);
        });
        seq.Insert(at, Group(row).DOFade(1f, 0.2f).From(0f));
        Vector2 home = row.anchoredPosition;
        seq.Insert(at, row.DOAnchorPos(home, 0.22f).From(home + new Vector2(-36f, 0f)).SetEase(Ease.OutCubic));
        seq.Insert(at + 0.05f, DOTween.To(() => 0f, v => valueText.text = format(Mathf.RoundToInt(v)), target, countDur).SetEase(Ease.OutCubic));
        if (punch)
            seq.Insert(at + 0.05f + countDur, valueText.transform.DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.3f, 6, 0.6f));
    }

    private void InsertStamp(Sequence seq, float at)
    {
        if (rankStamp == null) return;
        float baseRot = rankStamp.localEulerAngles.z;

        if (rankLaurel != null)
        {
            seq.InsertCallback(at - 0.2f, () => Show(rankLaurel));
            seq.Insert(at - 0.2f, Group(rankLaurel).DOFade(1f, 0.3f).From(0f));
            seq.Insert(at - 0.2f, rankLaurel.DOScale(1f, 0.35f).From(0.85f).SetEase(Ease.OutCubic));
        }

        // 大きく浮いた状態から一気に押す
        seq.InsertCallback(at, () => Show(rankStamp));
        seq.Insert(at, Group(rankStamp).DOFade(1f, 0.08f).From(0f));
        seq.Insert(at, rankStamp.DOScale(1f, 0.2f).From(2.8f).SetEase(Ease.InQuad));
        seq.Insert(at, rankStamp.DOLocalRotate(new Vector3(0f, 0f, baseRot), 0.2f).From(new Vector3(0f, 0f, baseRot - 28f)).SetEase(Ease.InQuad));

        float impact = at + 0.2f;
        seq.InsertCallback(impact, () => PlaySe(stampSe));
        seq.Insert(impact, rankStamp.DOPunchScale(new Vector3(-0.08f, -0.08f, 0f), 0.25f, 4, 0.5f));
        if (statsPanel != null)
            seq.Insert(impact, statsPanel.DOShakeAnchorPos(0.25f, 14f, 22, 90f, false, true));

        if (stampFlash != null)
        {
            var flashRt = stampFlash.rectTransform;
            seq.InsertCallback(impact, () => stampFlash.gameObject.SetActive(true));
            seq.Insert(impact, flashRt.DOScale(1.5f, 0.45f).From(1f).SetEase(Ease.OutCubic));
            seq.Insert(impact, stampFlash.DOFade(0f, 0.45f).From(0.7f));
            seq.InsertCallback(impact + 0.45f, () => stampFlash.gameObject.SetActive(false));
        }
    }

    private void PlaceChartEndMarker()
    {
        if (chartEndMarker == null) return;
        chartEndMarker.gameObject.SetActive(true);
        // 終点（ResultChartFill と PriceChartView は同じ配置で描いている）
        if (moneyChartFill != null && moneyChartFill.TryGetPoint(moneyChartFill.PointCount - 1, out var local))
            chartEndMarker.position = moneyChartFill.rectTransform.TransformPoint(local);
    }

    private void SetChartReveal(float progress)
    {
        if (chartRevealMask == null) return;
        float width = chartRevealMask.rectTransform.rect.width;
        var p = chartRevealMask.padding;
        p.z = width * (1f - Mathf.Clamp01(progress));
        chartRevealMask.padding = p;
    }

    private IEnumerable<RectTransform> Rows()
    {
        foreach (var t in new[] { finalMoneyText, moneyDifferenceText, stockValueText, netWorthText,
                     totalTurnsText, blacksmithLevelText, infoBrokerLevelText })
            if (t != null) yield return RowOf(t);
    }

    /// <summary>値テキストの親を「行」とみなす（親がルート直下のパネル等でなければ自身）。</summary>
    private RectTransform RowOf(TextMeshProUGUI valueText)
    {
        var parent = valueText.transform.parent as RectTransform;
        if (parent != null && parent.name.EndsWith("Row")) return parent;
        return valueText.rectTransform;
    }

    private Vector2 Home(RectTransform rt)
    {
        if (!_homePositions.TryGetValue(rt, out var home))
        {
            home = rt.anchoredPosition;
            _homePositions[rt] = home;
        }
        return home;
    }

    private static CanvasGroup Group(RectTransform rt)
    {
        var cg = rt.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void Hide(RectTransform rt)
    {
        if (rt == null) return;
        Group(rt).alpha = 0f;
    }

    private static void Show(RectTransform rt)
    {
        if (rt == null) return;
        rt.gameObject.SetActive(true);
    }

    private static void SetAlpha(CanvasGroup cg, float a)
    {
        if (cg == null) return;
        cg.DOKill();
        cg.alpha = a;
        cg.blocksRaycasts = false;
    }

    private void PlaySe(string key)
    {
        if (_skipping || string.IsNullOrEmpty(key)) return;
        SoundManager.Instance?.PlaySE(key);
    }

    private static void SetText(TextMeshProUGUI t, string s)
    {
        if (t != null) t.text = s;
    }

    private static string FormatGold(int v) => $"{v:#,0}G";

    private static string FormatSigned(int v) => v >= 0 ? $"+{v:#,0}G" : $"-{-v:#,0}G";

    private Sprite GetRankSprite(string rank)
    {
        if (rankSprites == null) return null;
        int idx = rank switch { "S" => 0, "A" => 1, "B" => 2, "C" => 3, "D" => 4, _ => -1 };
        return idx >= 0 && idx < rankSprites.Length ? rankSprites[idx] : null;
    }

    private static Color GetRankColor(string rank)
    {
        return rank switch
        {
            "S" => new Color(1f, 0.84f, 0f),
            "A" => new Color(0.9f, 0.3f, 0.3f),
            "B" => new Color(0.3f, 0.6f, 0.9f),
            "C" => new Color(0.3f, 0.8f, 0.3f),
            "D" => new Color(0.5f, 0.5f, 0.5f),
            _ => Color.white
        };
    }
}
