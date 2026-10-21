using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 魔王軍の配当通知書。勇者が負けたとき、配信リザルトのあとに防衛報酬の内訳を見せる。
/// お金はすでに精算済み（totalEarnings に含まれている）なので、この画面は表示だけ。
/// 演出: 通知書が開く → 明細が順に出る → 受取額カウントアップ → 封蝋とハンコをポン → ボタン。
/// 演出中にクリック（パッドは B）するとスキップして最終状態にする。
/// </summary>
public class DemonKingPayoutView : MonoBehaviour
{
    // 魔王のセリフ（のじゃ口調の短い1行）。表示ごとにランダム。吹き出しの折り返し位置は \n で決める
    private static readonly string[] DemonKingLines =
    {
        "我が軍への出資、\n大儲けじゃったな！",
        "勇者め、\nざまあみろじゃ！",
        "よう出資してくれた。\n配当じゃ！",
        "我が城は落ちぬ。\n次も頼んだのじゃ！",
        "ふはは！\n出資の見返りじゃ！",
    };

    private const float RowInterval = 0.16f;
    private const float CountUpDuration = 0.8f;
    private const float StampFromScale = 1.8f;
    private const float StampDuration = 0.2f;
    private const float DemonSlideOffset = 260f;

    [Header("ルート（初期は非アクティブ）")]
    [SerializeField] private GameObject panelRoot;
    [Tooltip("通知書本体（木枠＋看板）。開くときにポップさせる")]
    [SerializeField] private RectTransform windowRoot;

    [Header("明細")]
    [SerializeField] private TextMeshProUGUI dungeonText;
    [SerializeField] private CanvasGroup supportRow;
    [SerializeField] private TextMeshProUGUI supportValueText;
    [SerializeField] private CanvasGroup defenseRow;
    [SerializeField] private TextMeshProUGUI defenseValueText;
    [SerializeField] private CanvasGroup relicRow;
    [SerializeField] private TextMeshProUGUI relicLabelText;
    [SerializeField] private TextMeshProUGUI relicValueText;
    [SerializeField] private CanvasGroup totalRow;
    [SerializeField] private TextMeshProUGUI totalValueText;

    [Header("封蝋・回収倍率ハンコ")]
    [SerializeField] private RectTransform sealRoot;
    [SerializeField] private RectTransform stampRoot;
    [SerializeField] private TextMeshProUGUI stampText;

    [Header("魔王")]
    [SerializeField] private RectTransform demonRoot;
    [SerializeField] private RectTransform bubbleRoot;
    [SerializeField] private TextMeshProUGUI bubbleText;

    [Header("操作")]
    [SerializeField] private Button receiveButton;
    [Tooltip("演出中だけ有効にする全面の透明ボタン（クリックでスキップ）")]
    [SerializeField] private Button skipCatcher;

    private UniTaskCompletionSource _receiveTcs;
    private CancellationTokenSource _skipCts;
    private UIFocusScope _focusScope;
    private Tween _countTween;
    private Vector2 _demonBasePos;
    private float[] _rowSlotY;

    /// <summary>通知書を表示中（受け取り待ち・演出中）か。オートプレイが見る</summary>
    public bool IsShowing => _receiveTcs != null;

    private void Awake()
    {
        if (receiveButton != null) receiveButton.onClick.AddListener(OnReceiveClicked);
        if (skipCatcher != null) skipCatcher.onClick.AddListener(SkipAnimation);
        if (demonRoot != null) _demonBasePos = demonRoot.anchoredPosition;
        CacheRowSlots();

        // パッド/キー: 受け取るボタンへフォーカス。B は演出スキップ
        if (panelRoot != null)
        {
            _focusScope = UIFocusScope.GetOrAdd(panelRoot);
            _focusScope.Modal = true;
            _focusScope.Priority = 200;
            _focusScope.FirstSelected = receiveButton;
            _focusScope.SetCancelAction(SkipAnimation);
        }
    }

    private void OnDestroy()
    {
        _skipCts?.Dispose();
        _skipCts = null;
    }

    /// <summary>
    /// 配当通知書を表示し、「受け取る」が押されるまで待つ。閉じてから戻る。
    /// panelRoot が未配線なら何もせず戻る。
    /// </summary>
    public async UniTask ShowAsync(DemonKingPayoutData data, CancellationToken cancellation = default)
    {
        if (data == null) return;
        if (panelRoot == null)
        {
            Debug.LogWarning("[DemonKingPayoutView] panelRoot が未設定のため配当通知書をスキップします。");
            return;
        }

        _receiveTcs = new UniTaskCompletionSource();
        _finalSePlayed = false;
        ApplyTexts(data);
        LayoutRows(data);
        PrepareHiddenState(data);

        panelRoot.SetActive(true);
        UIFx.PanelOpen(panelRoot, 0.25f);

        _skipCts?.Dispose();
        _skipCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation, destroyCancellationToken);
        try
        {
            await PlayIntroAsync(data, _skipCts.Token);
        }
        catch (OperationCanceledException)
        {
            // スキップ以外（シーン破棄・呼び出し元のキャンセル）はそのまま抜ける
            if (cancellation.IsCancellationRequested || destroyCancellationToken.IsCancellationRequested) throw;
        }

        ShowFinalState(data);

        try
        {
            await _receiveTcs.Task.AttachExternalCancellation(cancellation);
        }
        finally
        {
            _receiveTcs = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }

    /// <summary>演出を飛ばしてすぐ受け取る（オートプレイ用）。</summary>
    public void ReceiveImmediately()
    {
        SkipAnimation();
        OnReceiveClicked();
    }

    // =====================================================
    // 内容
    // =====================================================

    private void ApplyTexts(DemonKingPayoutData data)
    {
        if (dungeonText != null) dungeonText.text = $"{data.DungeonName} {DungeonLevels.Label(data.DungeonLevel)}";
        if (supportValueText != null) supportValueText.text = FormatGold(data.SupportAmount);
        if (defenseValueText != null) defenseValueText.text = FormatGold(data.BaseReward);
        if (relicLabelText != null) relicLabelText.text = $"レリック補正 <size=80%>×{data.RelicMultiplier:0.0#}</size>";
        if (relicValueText != null) relicValueText.text = "+" + FormatGold(data.RelicBonus);
        // 回収倍率は受取累計で見る。はじめて支援額を回収しきった回だけ「元本回収」
        if (stampText != null) stampText.text = data.JustRecoveredPrincipal ? "元本\n回収" : FormatRate(data.ReturnRate);
        if (bubbleText != null) bubbleText.text = DemonKingLines[UnityEngine.Random.Range(0, DemonKingLines.Length)];
        SetTotal(0);
    }

    /// <summary>プレハブ上の明細3行の高さ（上から支援額・防衛報酬・レリック補正）を覚えておく。</summary>
    private void CacheRowSlots()
    {
        var rows = DetailRows();
        _rowSlotY = new float[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            _rowSlotY[i] = rows[i] != null ? ((RectTransform)rows[i].transform).anchoredPosition.y : 0f;
    }

    /// <summary>出す行だけを、明細欄の中央に詰めて並べ直す（支援額0・補正なしで隙間が空かないように）。</summary>
    private void LayoutRows(DemonKingPayoutData data)
    {
        var rows = DetailRows();
        if (_rowSlotY == null || _rowSlotY.Length < rows.Length) return;

        bool[] visible = { data.SupportAmount > 0, true, data.HasRelicBonus };
        int count = 0;
        foreach (bool v in visible) if (v) count++;

        float center = (_rowSlotY[0] + _rowSlotY[rows.Length - 1]) * 0.5f;
        float spacing = _rowSlotY[0] - _rowSlotY[1];
        int index = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (!visible[i] || rows[i] == null) continue;
            var rt = (RectTransform)rows[i].transform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, center + spacing * ((count - 1) * 0.5f - index));
            index++;
        }
    }

    private CanvasGroup[] DetailRows() => new[] { supportRow, defenseRow, relicRow };

    private void SetTotal(int value)
    {
        if (totalValueText != null) totalValueText.text = $"{value:N0}<size=55%>G</size>";
    }

    private static string FormatGold(int value) => $"{value:N0}<size=70%>G</size>";

    private static string FormatRate(float rate) => rate >= 10f ? $"{rate:0}倍" : $"{rate:0.0}倍";

    // =====================================================
    // 演出
    // =====================================================

    /// <summary>演出開始前の状態（明細・封蝋・ハンコ・吹き出し・ボタンを隠す）。</summary>
    private void PrepareHiddenState(DemonKingPayoutData data)
    {
        HideRow(supportRow);
        HideRow(defenseRow);
        HideRow(relicRow);
        HideRow(totalRow);
        SetActive(sealRoot, false);
        SetActive(stampRoot, false);
        SetActive(bubbleRoot, false);
        if (receiveButton != null) receiveButton.gameObject.SetActive(false);
        if (skipCatcher != null) skipCatcher.gameObject.SetActive(true);
    }

    private async UniTask PlayIntroAsync(DemonKingPayoutData data, CancellationToken ct)
    {
        // 1. 通知書が開く（魔王は右から滑り込む）
        if (windowRoot != null) UIFx.Pop(windowRoot, 0.9f, 0.28f);
        if (demonRoot != null)
        {
            demonRoot.DOKill();
            demonRoot.anchoredPosition = _demonBasePos + new Vector2(DemonSlideOffset, 0f);
            demonRoot.DOAnchorPos(_demonBasePos, 0.4f).SetEase(Ease.OutCubic).SetLink(gameObject);
        }
        await DelaySeconds(0.3f, ct);

        // 2. 明細の行が順に出る
        if (data.SupportAmount > 0) await RevealRowAsync(supportRow, ct);
        await RevealRowAsync(defenseRow, ct);
        if (data.HasRelicBonus) await RevealRowAsync(relicRow, ct);

        // 3. 受取額のカウントアップ
        await RevealRowAsync(totalRow, ct);
        int shown = 0;
        _countTween?.Kill();
        float countDuration = CountUpDuration * GameSettings.EffectDurationScale;
        PlaySe(CountSe);
        _countTween = DOTween.To(() => shown, v => { shown = v; SetTotal(v); }, data.FinalReward, countDuration)
            .SetEase(Ease.OutCubic)
            .SetLink(gameObject);
        await DelaySeconds(CountUpDuration + 0.05f, ct);
        PlayFinalSeOnce();

        // 4. 封蝋とハンコを「ポン」
        StampIn(sealRoot);
        await DelaySeconds(StampDuration + 0.05f, ct);
        if (data.SupportAmount > 0)
        {
            StampIn(stampRoot);
            await DelaySeconds(StampDuration + 0.1f, ct);
        }

        // 魔王のひとこと
        if (bubbleRoot != null)
        {
            bubbleRoot.gameObject.SetActive(true);
            UIFx.Pop(bubbleRoot, 0.6f, 0.22f);
            await DelaySeconds(0.2f, ct);
        }
    }

    /// <summary>演出をすべて終えた状態にする（スキップ時もここに来る）。最後にボタンを出す。</summary>
    private void ShowFinalState(DemonKingPayoutData data)
    {
        _countTween?.Kill();
        SetTotal(data.FinalReward);
        PlayFinalSeOnce(); // スキップしたときも確定の音は1回だけ鳴らす

        ShowRow(supportRow, data.SupportAmount > 0);
        ShowRow(defenseRow, true);
        ShowRow(relicRow, data.HasRelicBonus);
        ShowRow(totalRow, true);

        ResetScale(windowRoot);
        ResetScale(bubbleRoot);
        SetActive(bubbleRoot, bubbleRoot != null);
        ResetScale(sealRoot);
        SetActive(sealRoot, true);
        ResetScale(stampRoot);
        SetActive(stampRoot, data.SupportAmount > 0);
        if (demonRoot != null)
        {
            demonRoot.DOKill();
            demonRoot.anchoredPosition = _demonBasePos;
        }

        // 5. ボタンが出る
        if (skipCatcher != null) skipCatcher.gameObject.SetActive(false);
        if (receiveButton != null)
        {
            receiveButton.gameObject.SetActive(true);
            UIFx.Pop(receiveButton.transform, 0.8f, 0.2f);
        }
        if (_focusScope != null) _focusScope.Refocus(true);
    }

    private async UniTask RevealRowAsync(CanvasGroup row, CancellationToken ct)
    {
        if (row == null) return;
        row.gameObject.SetActive(true);
        row.DOKill();
        row.alpha = 0f;
        row.DOFade(1f, 0.18f).SetLink(row.gameObject);
        UIFx.Pop(row.transform, 0.92f, 0.18f);
        await DelaySeconds(RowInterval, ct);
    }

    private static void StampIn(RectTransform target)
    {
        if (target == null) return;
        target.gameObject.SetActive(true);
        UIFx.Pop(target, StampFromScale, StampDuration);
    }

    private static void HideRow(CanvasGroup row)
    {
        if (row == null) return;
        row.DOKill();
        row.alpha = 0f;
        row.gameObject.SetActive(false);
    }

    private static void ShowRow(CanvasGroup row, bool visible)
    {
        if (row == null) return;
        row.DOKill();
        row.alpha = 1f;
        row.transform.DOKill();
        row.transform.localScale = Vector3.one;
        row.gameObject.SetActive(visible);
    }

    private static void ResetScale(Transform target)
    {
        if (target == null) return;
        target.DOKill();
        target.localScale = Vector3.one;
    }

    private static void SetActive(Component target, bool active)
    {
        if (target != null) target.gameObject.SetActive(active);
    }

    /// <summary>演出の待ち。設定「演出を速くする」で半分になる。</summary>
    private static UniTask DelaySeconds(float seconds, CancellationToken ct)
        => UniTask.Delay(TimeSpan.FromSeconds(seconds * GameSettings.EffectDurationScale), ignoreTimeScale: true, cancellationToken: ct);

    // =====================================================
    // 入力
    // =====================================================

    private void SkipAnimation()
    {
        if (_skipCts != null && !_skipCts.IsCancellationRequested) _skipCts.Cancel();
    }

    private void OnReceiveClicked() => _receiveTcs?.TrySetResult();

    // =====================================================
    // 音（ボタンの押下音は ButtonSoundHandler が鳴らすので、ここは演出の音だけ）
    // =====================================================

    private const string CountSe = "営業/SE_数の増減";
    private const string FinalSe = "営業/SE_売上音";
    private bool _finalSePlayed;

    private void PlayFinalSeOnce()
    {
        if (_finalSePlayed) return;
        _finalSePlayed = true;
        PlaySe(FinalSe);
    }

    private static void PlaySe(string key)
    {
        // Instance は見つからないと LogError を出すので、鳴らすときだけ取得する
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlaySE(key);
    }

#if UNITY_EDITOR
    /// <summary>見た目確認用（Play 中に Inspector の右クリックメニューから）。</summary>
    [ContextMenu("Debug: ダミー表示")]
    private void DebugShowDummy()
        => ShowAsync(new DemonKingPayoutData("魔王城", 3, 30000, 50000, 1.2f, 60000)).Forget();
#endif
}
