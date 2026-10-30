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
    // 魔王のセリフ（のじゃ口調の短い1行）。この回の儲け方で組を選び、組の中からランダム。吹き出しの折り返し位置は \n で決める
    // 大儲け: この回の受取が出資の 3 倍以上
    private static readonly string[] BigWinLines =
    {
        "我が軍への出資、\n大儲けじゃったな！",
        "大穴じゃ！\nよう読んだのう！",
        "ふはは！\n笑いが止まらぬわ！",
    };
    // ふつう: 出資より多く戻った（または出資なしで報酬だけ）
    private static readonly string[] WinLines =
    {
        "勇者め、\nざまあみろじゃ！",
        "よう出資してくれた。\n配当じゃ！",
        "我が城は落ちぬ。\n次も頼んだのじゃ！",
        "ふはは！\n出資の見返りじゃ！",
    };
    // 元本だけ: 元本保証で出資の合計まで引き上げた
    private static readonly string[] PrincipalOnlyLines =
    {
        "勝って当然の戦じゃ。\n元手だけは返そう",
        "損はさせぬ。\nじゃが儲けもないぞ",
        "読みは当たりじゃ。\n次は大穴を狙え！",
    };
    private const float BigWinRate = 3f;

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
    [Tooltip("見込みの段のオッズの行（任意）。未設定ならレリック補正の行を複製して、その下に作る")]
    [SerializeField] private CanvasGroup oddsRow;
    [SerializeField] private TextMeshProUGUI oddsLabelText;
    [SerializeField] private TextMeshProUGUI oddsValueText;
    [SerializeField] private CanvasGroup totalRow;
    [SerializeField] private TextMeshProUGUI totalValueText;

    [Header("明細が4行のとき（支援額・防衛報酬・レリック補正・オッズ）")]
    [Tooltip("明細と受取額の間の線。4行のときだけ下へずらして、明細欄を広げる")]
    [SerializeField] private RectTransform separatorRoot;
    [Tooltip("4行のときの行の間隔（3行以下はプレハブ上の3行の間隔のまま）")]
    [SerializeField] private float compactRowSpacing = 64f;
    [Tooltip("4行のときの行の中心のずれ（プレハブ上の3行の中心から。負で下へ）")]
    [SerializeField] private float compactRowCenterOffset = -6f;
    [Tooltip("4行のときの線のずれ（負で下へ）")]
    [SerializeField] private float compactSeparatorOffset = -12f;

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
    private Vector2 _separatorBasePos;

    /// <summary>通知書を表示中（受け取り待ち・演出中）か。オートプレイが見る</summary>
    public bool IsShowing => _receiveTcs != null;

    private void Awake()
    {
        if (receiveButton != null) receiveButton.onClick.AddListener(OnReceiveClicked);
        if (skipCatcher != null) skipCatcher.onClick.AddListener(SkipAnimation);
        // #4 演出のあとに出る「受け取る」がこの画面の主ボタン
        if (receiveButton != null) ButtonFeel.SetPrimary(receiveButton);
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
        if (data.HasOddsLine) EnsureOddsRow();
        ApplyTexts(data);
        LayoutRows(data);
        PrepareHiddenState(data);

        // #1 通知書がぽよんと開く（暗幕ごとフェード、木枠の通知書が弾む）。従来の PanelOpen + Pop(windowRoot) の置き換え
        UIMotion.Open(panelRoot, windowRoot);

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
            // #1 縮んで閉じる（0.15 秒。省略モードは即 SetActive(false)）。IsShowing はこの時点で false
            if (panelRoot != null) UIMotion.Close(panelRoot, null, windowRoot);
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
        // 見込みの段のオッズ（倍率 1 なら行ごと出さない）。オッズで変わった分をレリック補正に混ぜない
        if (oddsLabelText != null) oddsLabelText.text = data.OddsLabel;
        if (oddsValueText != null) oddsValueText.text = data.OddsValueNumber + "<size=70%>G</size>";
        // 回収倍率は受取累計で見る。はじめて支援額を回収しきった回だけ「元本回収」
        if (stampText != null) stampText.text = data.JustRecoveredPrincipal ? "元本\n回収" : FormatRate(data.ReturnRate);
        if (bubbleText != null)
        {
            var lines = PickLines(data);
            bubbleText.text = lines[UnityEngine.Random.Range(0, lines.Length)];
        }
        SetTotal(0);
    }

    /// <summary>この回の儲け方に合うセリフの組（元本だけ／大儲け／ふつう）。</summary>
    internal static string[] PickLines(DemonKingPayoutData data)
    {
        if (data.PrincipalGuaranteed) return PrincipalOnlyLines;
        if (data.SupportAmount > 0 && data.FinalReward >= data.SupportAmount * BigWinRate) return BigWinLines;
        return WinLines;
    }

    /// <summary>プレハブ上の明細3行の高さ（上から支援額・防衛報酬・レリック補正）を覚えておく。オッズの行はこの3行の間隔で詰めて並べる。</summary>
    private void CacheRowSlots()
    {
        var rows = new[] { supportRow, defenseRow, relicRow };
        _rowSlotY = new float[rows.Length];
        for (int i = 0; i < rows.Length; i++)
            _rowSlotY[i] = rows[i] != null ? ((RectTransform)rows[i].transform).anchoredPosition.y : 0f;
        if (separatorRoot != null) _separatorBasePos = separatorRoot.anchoredPosition;
    }

    /// <summary>
    /// オッズの行がプレハブに無ければ、レリック補正の行を複製して作る（文字は同じ位置の子から取る）。
    /// レリック補正の行も無ければ作らない（オッズの行は出ないが、受取額は正しい）。
    /// </summary>
    private void EnsureOddsRow()
    {
        if (oddsRow != null || relicRow == null) return;
        var clone = Instantiate(relicRow.gameObject, relicRow.transform.parent);
        clone.name = "OddsRow";
        oddsRow = clone.GetComponent<CanvasGroup>();
        oddsLabelText = FindSameChild(relicRow.transform, relicLabelText, clone.transform);
        oddsValueText = FindSameChild(relicRow.transform, relicValueText, clone.transform);
        clone.SetActive(false);
    }

    private static TextMeshProUGUI FindSameChild(Transform originalRoot, TextMeshProUGUI original, Transform cloneRoot)
    {
        if (original == null) return null;
        if (original.transform == originalRoot) return cloneRoot.GetComponent<TextMeshProUGUI>();
        string path = original.name;
        for (var t = original.transform.parent; t != null && t != originalRoot; t = t.parent) path = t.name + "/" + path;
        var found = cloneRoot.Find(path);
        return found != null ? found.GetComponent<TextMeshProUGUI>() : null;
    }

    /// <summary>
    /// 出す行だけを、明細欄の中央に詰めて並べ直す（支援額0・補正なしで隙間が空かないように）。
    /// 3行以下はプレハブ上の3行の間隔のまま。4行（オッズの行あり）は間隔を詰め、線を少し下げて枠からはみ出さないようにする。
    /// </summary>
    private void LayoutRows(DemonKingPayoutData data)
    {
        var rows = DetailRows();
        if (_rowSlotY == null || _rowSlotY.Length < 3) return;

        bool[] visible = { data.SupportAmount > 0, true, data.HasRelicBonus, data.HasOddsLine };
        int count = 0;
        for (int i = 0; i < visible.Length; i++) if (visible[i] && rows[i] != null) count++;

        bool compact = count > _rowSlotY.Length;
        float center = (_rowSlotY[0] + _rowSlotY[_rowSlotY.Length - 1]) * 0.5f;
        float spacing = _rowSlotY[0] - _rowSlotY[1];
        if (compact)
        {
            center += compactRowCenterOffset;
            spacing = compactRowSpacing;
        }
        if (separatorRoot != null)
            separatorRoot.anchoredPosition = _separatorBasePos + new Vector2(0f, compact ? compactSeparatorOffset : 0f);

        int index = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (!visible[i] || rows[i] == null) continue;
            var rt = (RectTransform)rows[i].transform;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, center + spacing * ((count - 1) * 0.5f - index));
            index++;
        }
    }

    private CanvasGroup[] DetailRows() => new[] { supportRow, defenseRow, relicRow, oddsRow };

    private void SetTotal(int value)
    {
        if (totalValueText != null) totalValueText.text = TotalText(value);
    }

    private static string TotalText(int value) => $"{value:N0}<size=55%>G</size>";

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
        HideRow(oddsRow);
        HideRow(totalRow);
        SetActive(sealRoot, false);
        SetActive(stampRoot, false);
        SetActive(bubbleRoot, false);
        if (receiveButton != null) receiveButton.gameObject.SetActive(false);
        if (skipCatcher != null) skipCatcher.gameObject.SetActive(true);
    }

    private async UniTask PlayIntroAsync(DemonKingPayoutData data, CancellationToken ct)
    {
        // 1. 通知書が開く（ShowAsync の UIMotion.Open で弾む。魔王は右から滑り込む）
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
        if (data.HasOddsLine) await RevealRowAsync(oddsRow, ct);

        // 3. 受取額のカウントアップ
        await RevealRowAsync(totalRow, ct);
        _countTween?.Kill();
        PlaySe(CountSe);
        // #2 明細から受取額へコインが飛び、数字が回る（共通部品。速い設定・省略は UIMotion が決める。最終の文字は SetTotal と同じ）
        _countTween = totalValueText != null
            ? UIMotion.CountTo(totalValueText, 0, data.FinalReward, v => TotalText((int)v), CountUpDuration)
            : null;
        if (totalValueText != null && defenseRow != null && data.FinalReward > 0)
            CoinBurst.Fly((RectTransform)defenseRow.transform, totalValueText.rectTransform,
                Mathf.Clamp(4 + (int)Mathf.Log10(Mathf.Max(1, data.FinalReward)), 4, 10),
                onEachArrive: () => UIMotion.Bump(totalValueText.transform, 1.06f, 0.16f));
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
        ShowRow(oddsRow, data.HasOddsLine);
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

    [ContextMenu("Debug: ダミー表示（オッズあり）")]
    private void DebugShowDummyOdds()
        => ShowAsync(new DemonKingPayoutData("魔王城", 3, 30000, 50000, 1.2f, 60000, SupportOddsTier.Unfavorable, 0.45f, 27000)).Forget();

    [ContextMenu("Debug: ダミー表示（オッズあり・楽勝 ×2.0）")]
    private void DebugShowDummyOddsUp()
        => ShowAsync(new DemonKingPayoutData("魔王城", 3, 30000, 50000, 1.2f, 60000, SupportOddsTier.Easy, 2f, 120000)).Forget();

    [ContextMenu("Debug: ダミー表示（絶望 ×0.3・元本保証）")]
    private void DebugShowDummyFloor()
        => ShowAsync(new DemonKingPayoutData("魔王城", 3, 50000, 150000, 1f, 150000, SupportOddsTier.Hopeless, 0.3f, 50000, 0, true)).Forget();
#endif
}
