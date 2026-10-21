using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 配信画面（戦闘が映っている領域）にだけ重ねるウェーブ演出。
/// - ウェーブクリア: 金白フラッシュ＋光の帯2本が横切る＋放射光とキラキラ
/// - 次ウェーブへ: 木色の帯がワイプインし「WAVE n/m」が飛び込んで抜ける
/// - ボス到達: 赤いビネットの明滅＋赤い警告帯スクロール＋警告マーク「！」＋警告音
/// - 決着（SUMMARY E-1）: 勇者勝利は金のクリア演出＋「CLEAR!」の帯、魔王側の配当は紫の明滅＋「配当確定」の帯
/// 設定「演出を速くする」で速くなり、「画面シェイクを軽減」で揺れを出さない（M-11）。
/// 見た目の部品は Awake で自分の子に生成する（このオブジェクトの矩形＝配信画面に RectMask2D でクリップ）。
/// 待ち時間は呼び出し側から渡す speed（戦闘スピード倍率）で縮み、BattlePauseController のポーズ中は止まる。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BattleWaveFxView : MonoBehaviour
{
    [Header("フォント")]
    [Tooltip("「WAVE n/m」用（太縁の MPLUSRounded1c-Bold SDFPlusPadding）")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("警告マーク「！」用（MPLUSRounded1c-Bold SDF）")]
    [SerializeField] private TMP_FontAsset markFont;

    [Header("ウェーブクリア")]
    [SerializeField] private Color clearFlashColor = new(1f, 0.96f, 0.78f, 0.6f);
    [SerializeField] private Color clearBandColor = new(1f, 0.88f, 0.45f, 0.75f);
    [SerializeField] private Color clearRayColor = new(1f, 0.9f, 0.5f, 0.85f);
    [SerializeField] private Color sparkleColor = new(1f, 0.97f, 0.7f, 1f);
    [Tooltip("クリア演出の長さ（秒・等倍時）")]
    [SerializeField] private float clearDuration = 0.8f;

    [Header("次ウェーブ")]
    [SerializeField] private Color bannerColor = new(0.27f, 0.15f, 0.07f, 0.97f);
    [SerializeField] private Color bannerEdgeColor = new(1f, 0.8f, 0.3f, 1f);
    [SerializeField] private Color bannerTextColor = new(1f, 0.93f, 0.62f, 1f);
    [Tooltip("帯の高さ（配信画面の高さに対する割合）")]
    [SerializeField, Range(0.1f, 0.6f)] private float bannerHeightRatio = 0.26f;
    [Tooltip("帯のワイプイン／アウト秒")]
    [SerializeField] private float bannerWipeSeconds = 0.22f;
    [Tooltip("「WAVE n/m」の静止秒")]
    [SerializeField] private float bannerHoldSeconds = 0.6f;

    [Header("ボス到達")]
    [SerializeField] private Color bossVignetteColor = new(0.95f, 0.05f, 0.05f, 0.9f);
    [SerializeField] private Color bossTintColor = new(0.85f, 0f, 0f, 0.28f);
    [SerializeField] private Color bossStripeColor = new(0.85f, 0.05f, 0.05f, 0.95f);
    [SerializeField] private Color bossStripeDarkColor = new(0.12f, 0.02f, 0.02f, 0.9f);
    [SerializeField] private Color bossSignColor = new(1f, 0.82f, 0.1f, 1f);
    [SerializeField] private Color bossSignEdgeColor = new(0.55f, 0.03f, 0.03f, 1f);
    [SerializeField] private Color bossMarkColor = new(0.45f, 0.02f, 0.02f, 1f);
    [Tooltip("赤い明滅の回数")]
    [SerializeField] private int bossPulseCount = 3;
    [Tooltip("明滅1回の秒（等倍時）")]
    [SerializeField] private float bossPulseSeconds = 0.5f;
    [Tooltip("SoundLibrary の SE キー（空なら鳴らさない）")]
    [SerializeField] private string bossWarningSeKey = "ボス警告";

    [Header("決着（配信リザルトの前に配信画面のまま見せる溜め）")]
    [SerializeField] private Color finaleHeroTextColor = new(1f, 0.86f, 0.32f, 1f);
    [SerializeField] private Color finaleDemonBannerColor = new(0.2f, 0.07f, 0.3f, 0.97f);
    [SerializeField] private Color finaleDemonEdgeColor = new(0.78f, 0.48f, 1f, 1f);
    [SerializeField] private Color finaleDemonTextColor = new(0.93f, 0.8f, 1f, 1f);
    [SerializeField] private Color finaleDemonTintColor = new(0.45f, 0.1f, 0.75f, 0.32f);
    [SerializeField] private Color finaleDemonVignetteColor = new(0.42f, 0.05f, 0.7f, 0.85f);
    [Tooltip("決着の帯の静止秒（等倍時）。全体でおよそ 1.8〜2 秒")]
    [SerializeField] private float finaleHoldSeconds = 0.9f;

    // ---- 生成した部品 ----
    private RectTransform _area;
    private Image _flash;
    private readonly List<Image> _bands = new();
    private readonly List<Image> _rays = new();
    private readonly List<Image> _sparkles = new();
    private RectTransform _bannerRoot;
    private Image _banner;
    private TextMeshProUGUI _bannerText;
    private readonly List<Image> _bannerEdges = new();
    private RawImage _vignette;
    private Image _bossTint;
    private RawImage _stripeTop, _stripeBottom;
    private RectTransform _signRoot;
    private Image _signEdge, _sign;
    private TextMeshProUGUI _signMark;
    private bool _built;

    private Sequence _current;

    private void Awake()
    {
        Build();
        HideAll();
    }

    private void OnDestroy() => _current?.Kill();

    // ================= 公開 API =================

    /// <summary>
    /// ウェーブクリア → 次ウェーブ（WAVE nextWave/totalWaves）を続けて再生する。
    /// speed は戦闘スピード倍率（1 = 等倍。BattleUIView.ScaleSeconds(1) の逆数）。
    /// </summary>
    public async UniTask PlayWaveTransitionAsync(int nextWave, int totalWaves, float speed, BattlePauseController pause, CancellationToken token)
    {
        if (!Prepare()) return;
        var seq = DOTween.Sequence();
        seq.Append(BuildClearSequence());
        // クリアの余韻に重ねて帯を入れる
        seq.Insert(clearDuration * 0.7f, BuildBannerSequence($"WAVE {nextWave}/{totalWaves}"));
        await RunAsync(seq, speed, pause, token);
    }

    /// <summary>ボス到達の警告演出（赤い明滅＋警告帯＋「！」＋警告音）。</summary>
    public async UniTask PlayBossWarningAsync(float speed, BattlePauseController pause, CancellationToken token)
    {
        if (!Prepare()) return;
        if (!string.IsNullOrEmpty(bossWarningSeKey))
        {
            // Instance は見つからないと LogError を出すので、鳴らすときだけ取得する
            var sound = SoundManager.Instance;
            if (sound != null) sound.PlaySE(bossWarningSeKey);
        }
        await RunAsync(BuildBossSequence(), speed, pause, token);
    }

    /// <summary>
    /// 決着の溜め。heroWon=true は金のクリア演出＋帯、false は紫の明滅＋帯（魔王側の配当）。
    /// skipRequested が true を返したら（クリック・決定）すぐ終える。戦闘は止まっているのでポーズは見ない。
    /// </summary>
    public async UniTask PlayFinaleAsync(bool heroWon, string label, CancellationToken token, System.Func<bool> skipRequested = null)
    {
        if (!Prepare())
        {
            Debug.LogWarning("[BattleWaveFxView] 決着の演出を出せませんでした（非アクティブ）。");
            return;
        }
        float startedAt = Time.unscaledTime;
        var seq = DOTween.Sequence();
        if (heroWon)
        {
            seq.Append(BuildClearSequence());
            seq.Insert(clearDuration * 0.3f, BuildBannerSequence(label, finaleHoldSeconds, bannerColor, bannerEdgeColor, finaleHeroTextColor));
        }
        else
        {
            seq.Append(BuildDemonTintSequence());
            seq.Insert(0.15f, BuildBannerSequence(label, finaleHoldSeconds, finaleDemonBannerColor, finaleDemonEdgeColor, finaleDemonTextColor));
        }
        _current = seq;
        seq.timeScale = 1f / Mathf.Max(0.1f, GameSettings.EffectDurationScale);
        seq.SetLink(gameObject);
        seq.OnKill(HideAll);
        try
        {
            while (seq.IsActive() && !seq.IsComplete())
            {
                if (skipRequested != null && skipRequested()) break;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        finally
        {
            if (seq.IsActive()) seq.Kill();
            if (_current == seq) _current = null;
            Debug.Log($"[BattleWaveFxView] 決着の演出「{label}」{Time.unscaledTime - startedAt:0.00}秒");
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Debug: 決着（CLEAR!）")]
    private void DebugFinaleHero() => PlayFinaleAsync(true, "CLEAR!", destroyCancellationToken).Forget();

    [ContextMenu("Debug: 決着（配当確定）")]
    private void DebugFinaleDemon() => PlayFinaleAsync(false, "配当確定", destroyCancellationToken).Forget();

    [ContextMenu("Debug: 宣言的中（勇者側）")]
    private void DebugDeclarationHero() => PlayBannerAsync("宣言的中！", false, destroyCancellationToken).Forget();

    [ContextMenu("Debug: 宣言的中（魔王側）")]
    private void DebugDeclarationDemon() => PlayBannerAsync("宣言的中！", true, destroyCancellationToken).Forget();
#endif

    /// <summary>
    /// 決着の帯だけをもう一度出す（村の広場 Lv5「勝負配信の宣言」が当たったときの「宣言的中！」）。
    /// demonSide=false は金の帯（決着「CLEAR!」と同じ色）、true は紫の帯（「配当確定」と同じ色）。
    /// 決着の溜めを飛ばした入力で一緒に飛ばないよう、1フレーム待ってから飛ばしの入力を見る。
    /// </summary>
    public async UniTask PlayBannerAsync(string label, bool demonSide, CancellationToken token, System.Func<bool> skipRequested = null)
    {
        if (!Prepare()) return;
        var seq = demonSide
            ? BuildBannerSequence(label, finaleHoldSeconds, finaleDemonBannerColor, finaleDemonEdgeColor, finaleDemonTextColor)
            : BuildBannerSequence(label, finaleHoldSeconds, bannerColor, bannerEdgeColor, finaleHeroTextColor);
        _current = seq;
        seq.timeScale = 1f / Mathf.Max(0.1f, GameSettings.EffectDurationScale);
        seq.SetLink(gameObject);
        seq.OnKill(HideAll);
        try
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token);
            while (seq.IsActive() && !seq.IsComplete())
            {
                if (skipRequested != null && skipRequested()) break;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        finally
        {
            if (seq.IsActive()) seq.Kill();
            if (_current == seq) _current = null;
        }
    }

    /// <summary>紫の全面ティントとビネットを2回明滅させる（魔王側の決着）。</summary>
    private Sequence BuildDemonTintSequence()
    {
        var seq = DOTween.Sequence();
        _vignette.gameObject.SetActive(true);
        _vignette.color = WithAlpha(finaleDemonVignetteColor, 0f);
        _bossTint.gameObject.SetActive(true);
        _bossTint.color = WithAlpha(finaleDemonTintColor, 0f);
        const float p = 0.45f;
        for (int i = 0; i < 2; i++)
        {
            float t = i * p;
            seq.Insert(t, _vignette.DOFade(finaleDemonVignetteColor.a, p * 0.3f).SetEase(Ease.OutQuad));
            seq.Insert(t + p * 0.3f, _vignette.DOFade(finaleDemonVignetteColor.a * 0.4f, p * 0.7f).SetEase(Ease.InQuad));
            seq.Insert(t, _bossTint.DOFade(finaleDemonTintColor.a, p * 0.2f));
            seq.Insert(t + p * 0.2f, _bossTint.DOFade(finaleDemonTintColor.a * 0.3f, p * 0.6f));
        }
        return seq;
    }

    // ================= 再生の共通処理 =================

    /// <summary>前の演出を止めて部品を隠す（部品の初期化はこの後の Build*Sequence で行う）。</summary>
    private bool Prepare()
    {
        if (!Build() || !isActiveAndEnabled) return false;
        _current?.Kill();
        _current = null;
        HideAll();
        return true;
    }

    private async UniTask RunAsync(Sequence seq, float speed, BattlePauseController pause, CancellationToken token)
    {
        _current = seq;
        // speed は戦闘スピード倍率。設定「演出を速くする」（EffectDurationScale 0.5）ならさらに倍速
        seq.timeScale = Mathf.Max(0.05f, speed) / Mathf.Max(0.1f, GameSettings.EffectDurationScale);
        seq.SetLink(gameObject);
        seq.OnKill(HideAll);

        try
        {
            while (seq.IsActive() && !seq.IsComplete())
            {
                if (pause != null)
                {
                    if (pause.IsPaused && seq.IsPlaying()) seq.Pause();
                    else if (!pause.IsPaused && !seq.IsPlaying()) seq.Play();
                }
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
        finally
        {
            if (seq.IsActive()) seq.Kill();
            if (_current == seq) _current = null;
        }
    }

    // ================= ウェーブクリア =================

    private Sequence BuildClearSequence()
    {
        var size = _area.rect.size;
        float d = clearDuration;
        var seq = DOTween.Sequence();

        // フラッシュ
        _flash.gameObject.SetActive(true);
        _flash.color = WithAlpha(clearFlashColor, 0f);
        seq.Insert(0f, _flash.DOFade(clearFlashColor.a, d * 0.08f).SetEase(Ease.OutQuad));
        seq.Insert(d * 0.08f, _flash.DOFade(0f, d * 0.5f).SetEase(Ease.InQuad));

        // 光の帯（斜め・2本が時間差で左→右）
        for (int i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i];
            var rt = band.rectTransform;
            float w = size.x * (i == 0 ? 0.22f : 0.1f);
            rt.sizeDelta = new Vector2(w, size.y * 2.2f);
            rt.anchoredPosition = new Vector2(-size.x * 0.75f, 0f);
            band.color = clearBandColor;
            band.gameObject.SetActive(true);
            float start = i * d * 0.12f;
            seq.Insert(start, rt.DOAnchorPosX(size.x * 0.75f, d * 0.6f).SetEase(Ease.InOutSine));
            seq.InsertCallback(start + d * 0.6f, () => band.gameObject.SetActive(false));
        }

        // 放射光
        float rayLen = Mathf.Max(size.x, size.y) * 0.75f;
        for (int i = 0; i < _rays.Count; i++)
        {
            var ray = _rays[i];
            var rt = ray.rectTransform;
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, 360f * i / _rays.Count + 7f);
            rt.sizeDelta = new Vector2(i % 2 == 0 ? 14f : 8f, rayLen * (i % 2 == 0 ? 1f : 0.7f));
            rt.localScale = new Vector3(1f, 0f, 1f);
            ray.color = clearRayColor;
            ray.gameObject.SetActive(true);
            seq.Insert(0f, rt.DOScaleY(1f, d * 0.35f).SetEase(Ease.OutCubic));
            seq.Insert(d * 0.15f, ray.DOFade(0f, d * 0.45f).SetEase(Ease.InQuad));
        }

        // キラキラ（中心から外へ）
        var rnd = new System.Random(12345);
        for (int i = 0; i < _sparkles.Count; i++)
        {
            var sp = _sparkles[i];
            var rt = sp.rectTransform;
            float ang = (360f * i / _sparkles.Count + (float)rnd.NextDouble() * 18f) * Mathf.Deg2Rad;
            float dist = Mathf.Lerp(0.25f, 0.48f, (float)rnd.NextDouble()) * size.x;
            var to = new Vector2(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist * (size.y / size.x) * 1.4f);
            float s = Mathf.Lerp(10f, 22f, (float)rnd.NextDouble());
            rt.sizeDelta = new Vector2(s, s);
            rt.anchoredPosition = to * 0.15f;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            sp.color = sparkleColor;
            sp.gameObject.SetActive(true);
            seq.Insert(0f, rt.DOAnchorPos(to, d * 0.7f).SetEase(Ease.OutCubic));
            seq.Insert(0f, rt.DOLocalRotate(new Vector3(0f, 0f, 45f + 180f), d * 0.7f, RotateMode.FastBeyond360));
            seq.Insert(d * 0.35f, rt.DOScale(0f, d * 0.4f).SetEase(Ease.InBack));
        }

        return seq;
    }

    // ================= 次ウェーブ =================

    private Sequence BuildBannerSequence(string label)
        => BuildBannerSequence(label, bannerHoldSeconds, bannerColor, bannerEdgeColor, bannerTextColor);

    private Sequence BuildBannerSequence(string label, float holdSeconds, Color bandColor, Color edgeColor, Color textColor)
    {
        var size = _area.rect.size;
        float h = size.y * bannerHeightRatio;
        _bannerRoot.sizeDelta = new Vector2(0f, h);
        _bannerRoot.gameObject.SetActive(true);
        _banner.color = bandColor;
        foreach (var edge in _bannerEdges) edge.color = edgeColor;
        _banner.rectTransform.sizeDelta = new Vector2(size.x, 0f);
        _banner.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        _banner.rectTransform.pivot = new Vector2(0f, 0.5f);
        _banner.rectTransform.anchoredPosition = new Vector2(-size.x * 0.5f, 0f);

        _bannerText.text = label;
        _bannerText.color = textColor;
        _bannerText.fontSize = h * 0.62f;
        var textRt = _bannerText.rectTransform;
        textRt.anchoredPosition = new Vector2(size.x, 0f);
        textRt.localScale = Vector3.one;

        float wipe = bannerWipeSeconds;
        float fly = 0.28f;
        var seq = DOTween.Sequence();
        // 帯が左から伸びる
        seq.Append(_banner.rectTransform.DOScaleX(1f, wipe).SetEase(Ease.OutCubic));
        // 文字が右から飛び込む
        seq.Insert(wipe * 0.5f, textRt.DOAnchorPosX(0f, fly).SetEase(Ease.OutBack, 1.2f));
        seq.Insert(wipe * 0.5f + fly, textRt.DOPunchScale(Vector3.one * 0.12f, 0.25f, 6, 0.6f));
        float outAt = wipe * 0.5f + fly + holdSeconds;
        // 文字は左へ抜け、帯は右側へ縮んで消える
        seq.Insert(outAt, textRt.DOAnchorPosX(-size.x, fly).SetEase(Ease.InBack, 1.2f));
        seq.InsertCallback(outAt + fly * 0.4f, () =>
        {
            _banner.rectTransform.pivot = new Vector2(1f, 0.5f);
            _banner.rectTransform.anchoredPosition = new Vector2(size.x * 0.5f, 0f);
        });
        seq.Insert(outAt + fly * 0.4f, _banner.rectTransform.DOScaleX(0f, wipe).SetEase(Ease.InCubic));
        seq.AppendCallback(() => _bannerRoot.gameObject.SetActive(false));
        return seq;
    }

    // ================= ボス到達 =================

    private Sequence BuildBossSequence()
    {
        var size = _area.rect.size;
        float p = bossPulseSeconds;
        int pulses = Mathf.Max(1, bossPulseCount);
        float total = p * pulses;
        var seq = DOTween.Sequence();

        // 赤いビネット・赤い全面ティントの明滅
        _vignette.gameObject.SetActive(true);
        _vignette.color = WithAlpha(bossVignetteColor, 0f);
        _bossTint.gameObject.SetActive(true);
        _bossTint.color = WithAlpha(bossTintColor, 0f);
        for (int i = 0; i < pulses; i++)
        {
            float t = i * p;
            seq.Insert(t, _vignette.DOFade(bossVignetteColor.a, p * 0.25f).SetEase(Ease.OutQuad));
            seq.Insert(t + p * 0.25f, _vignette.DOFade(bossVignetteColor.a * 0.35f, p * 0.75f).SetEase(Ease.InQuad));
            seq.Insert(t, _bossTint.DOFade(bossTintColor.a, p * 0.15f));
            seq.Insert(t + p * 0.15f, _bossTint.DOFade(0f, p * 0.6f));
        }
        seq.Insert(total, _vignette.DOFade(0f, p * 0.6f));

        // 上下の警告帯（スライドイン → 縞がスクロール → スライドアウト）
        float stripeH = Mathf.Max(28f, size.y * 0.1f);
        SetupStripe(_stripeTop, stripeH, true, bossStripeColor, size);
        SetupStripe(_stripeBottom, stripeH, false, bossStripeColor, size);
        float aspect = size.x / stripeH;
        _stripeTop.uvRect = new Rect(0f, 0f, aspect / 2f, 1f);
        _stripeBottom.uvRect = new Rect(0f, 0f, aspect / 2f, 1f);
        var topRt = _stripeTop.rectTransform;
        var botRt = _stripeBottom.rectTransform;
        seq.Insert(0f, topRt.DOAnchorPosY(0f, 0.2f).SetEase(Ease.OutCubic));
        seq.Insert(0f, botRt.DOAnchorPosY(0f, 0.2f).SetEase(Ease.OutCubic));
        float scroll = 0f;
        seq.Insert(0f, DOTween.To(() => scroll, v =>
        {
            scroll = v;
            var r1 = _stripeTop.uvRect; r1.x = v; _stripeTop.uvRect = r1;
            var r2 = _stripeBottom.uvRect; r2.x = -v; _stripeBottom.uvRect = r2;
        }, total * 2.5f, total + p * 0.6f).SetEase(Ease.Linear));
        seq.Insert(total, topRt.DOAnchorPosY(stripeH, 0.25f).SetEase(Ease.InCubic));
        seq.Insert(total, botRt.DOAnchorPosY(-stripeH, 0.25f).SetEase(Ease.InCubic));

        // 警告マーク「！」（ドンと出て、明滅に合わせて脈打ち、最後に縮んで消える）
        float signSize = Mathf.Min(size.y * 0.5f, size.x * 0.4f);
        _signRoot.sizeDelta = new Vector2(signSize, signSize);
        _signRoot.anchoredPosition = Vector2.zero;
        _signRoot.localScale = Vector3.one * 2.4f;
        _signRoot.localRotation = Quaternion.identity;
        _signEdge.color = WithAlpha(bossSignEdgeColor, 0f);
        _sign.color = WithAlpha(bossSignColor, 0f);
        _signMark.color = bossMarkColor;
        _signMark.alpha = 0f;
        _signMark.fontSize = signSize * 0.62f;
        _signRoot.gameObject.SetActive(true);

        seq.Insert(0.05f, _signRoot.DOScale(1f, 0.28f).SetEase(Ease.OutBack, 2.2f));
        seq.Insert(0.05f, _signEdge.DOFade(1f, 0.12f));
        seq.Insert(0.05f, _sign.DOFade(1f, 0.12f));
        seq.Insert(0.05f, DOTween.To(() => _signMark.alpha, a => _signMark.alpha = a, 1f, 0.12f));
        if (!GameSettings.ReduceShake) // 設定「画面シェイクを軽減」
            seq.Insert(0.33f, _signRoot.DOShakeAnchorPos(0.3f, signSize * 0.08f, 30, 90f, false, true));
        for (int i = 1; i < pulses; i++)
            seq.Insert(i * p, _signRoot.DOPunchScale(Vector3.one * 0.14f, p * 0.5f, 4, 0.5f));
        seq.Insert(total, _signRoot.DOScale(0.6f, 0.25f).SetEase(Ease.InBack));
        seq.Insert(total + 0.05f, _signEdge.DOFade(0f, 0.2f));
        seq.Insert(total + 0.05f, _sign.DOFade(0f, 0.2f));
        seq.Insert(total + 0.05f, DOTween.To(() => _signMark.alpha, a => _signMark.alpha = a, 0f, 0.2f));

        seq.AppendCallback(HideAll);
        return seq;
    }

    private void SetupStripe(RawImage img, float h, bool top, Color color, Vector2 size)
    {
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.sizeDelta = new Vector2(0f, h);
        rt.anchoredPosition = new Vector2(0f, top ? h : -h);
        img.color = Color.white;
        img.gameObject.SetActive(true);
    }

    // ================= 部品生成 =================

    private bool Build()
    {
        if (_built) return true;
        _area = transform as RectTransform;
        if (_area == null) return false;

        if (!TryGetComponent<RectMask2D>(out _)) gameObject.AddComponent<RectMask2D>();

        var white = MakeSprite(Texture2D.whiteTexture);
        var hGrad = MakeSprite(MakeBandTexture());
        var vGrad = MakeSprite(MakeRayTexture());

        _flash = NewImage("Flash", _area, null);
        Stretch(_flash.rectTransform);

        _bossTint = NewImage("BossTint", _area, null);
        Stretch(_bossTint.rectTransform);

        _vignette = NewRaw("BossVignette", _area, MakeVignetteTexture());
        Stretch(_vignette.rectTransform);

        for (int i = 0; i < 2; i++)
        {
            var band = NewImage($"Band{i}", _area, hGrad);
            band.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -18f);
            _bands.Add(band);
        }

        for (int i = 0; i < 16; i++)
        {
            var ray = NewImage($"Ray{i}", _area, vGrad);
            ray.rectTransform.pivot = new Vector2(0.5f, 0f);
            _rays.Add(ray);
        }

        for (int i = 0; i < 14; i++)
            _sparkles.Add(NewImage($"Sparkle{i}", _area, white));

        // 次ウェーブの帯
        var bannerGo = new GameObject("WaveBanner", typeof(RectTransform));
        _bannerRoot = bannerGo.GetComponent<RectTransform>();
        _bannerRoot.SetParent(_area, false);
        _bannerRoot.anchorMin = new Vector2(0f, 0.5f);
        _bannerRoot.anchorMax = new Vector2(1f, 0.5f);
        _banner = NewImage("Band", _bannerRoot, null);
        _banner.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        _banner.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        var edgeTop = NewImage("EdgeTop", _banner.rectTransform, null);
        var edgeBottom = NewImage("EdgeBottom", _banner.rectTransform, null);
        foreach (var (edge, y) in new[] { (edgeTop, 1f), (edgeBottom, 0f) })
        {
            var rt = edge.rectTransform;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, 5f);
            edge.color = bannerEdgeColor;
            edge.gameObject.SetActive(true);
            _bannerEdges.Add(edge);
        }
        _bannerText = NewText("WaveText", _bannerRoot, font);
        _bannerText.characterSpacing = 6f;

        // ボスの警告帯
        var stripeTex = MakeStripeTexture();
        _stripeTop = NewRaw("StripeTop", _area, stripeTex);
        _stripeBottom = NewRaw("StripeBottom", _area, stripeTex);

        // 警告マーク
        var signGo = new GameObject("WarningSign", typeof(RectTransform));
        _signRoot = signGo.GetComponent<RectTransform>();
        _signRoot.SetParent(_area, false);
        var tri = MakeSprite(MakeTriangleTexture(256, 0f));
        var triInner = MakeSprite(MakeTriangleTexture(256, 0.14f));
        _signEdge = NewImage("Edge", _signRoot, tri);
        Stretch(_signEdge.rectTransform);
        _sign = NewImage("Fill", _signRoot, triInner);
        Stretch(_sign.rectTransform);
        _signMark = NewText("Mark", _signRoot, markFont != null ? markFont : font);
        Stretch(_signMark.rectTransform);
        _signMark.text = "!";
        _signMark.fontStyle = FontStyles.Bold;
        _signMark.margin = new Vector4(0f, 0f, 0f, 0f);
        _signMark.rectTransform.anchorMin = new Vector2(0f, 0f);
        _signMark.rectTransform.anchorMax = new Vector2(1f, 0.82f);

        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

        _built = true;
        return true;
    }

    private void HideAll()
    {
        if (!_built) return;
        for (int i = 0; i < _area.childCount; i++)
            _area.GetChild(i).gameObject.SetActive(false);
    }

    private Image NewImage(string name, RectTransform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private RawImage NewRaw(string name, RectTransform parent, Texture tex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI NewText(string name, RectTransform parent, TMP_FontAsset fontAsset)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null) t.font = fontAsset;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.sizeDelta = Vector2.zero;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // ---- 手続き生成テクスチャ ----

    private static Sprite MakeSprite(Texture2D tex)
        => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);

    /// <summary>横方向に 透明→白→透明 の帯。</summary>
    private static Texture2D MakeBandTexture()
    {
        const int w = 64;
        var tex = NewTex(w, 4);
        for (int x = 0; x < w; x++)
        {
            float u = x / (w - 1f);
            float a = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>根元が濃く先端へ消える縦長の光線（横方向も中心が濃い）。</summary>
    private static Texture2D MakeRayTexture()
    {
        const int w = 16, h = 64;
        var tex = NewTex(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float v = y / (h - 1f);
            float u = Mathf.Abs(x / (w - 1f) - 0.5f) * 2f;
            float a = Mathf.Clamp01(1f - u) * Mathf.Clamp01(1f - v) * Mathf.SmoothStep(0f, 1f, v * 6f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>中心が透明で縁が濃い楕円ビネット。</summary>
    private static Texture2D MakeVignetteTexture()
    {
        const int s = 128;
        var tex = NewTex(s, s);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = (x / (s - 1f) - 0.5f) * 2f;
            float dy = (y / (s - 1f) - 0.5f) * 2f;
            float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.15f, d));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>赤×黒の斜め縞（横に繰り返して使う）。</summary>
    private Texture2D MakeStripeTexture()
    {
        const int s = 64;
        var tex = NewTex(s, s);
        tex.wrapMode = TextureWrapMode.Repeat;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float k = Mathf.Repeat((x + y) / (float)s, 1f);
            // 境界を少しぼかしてジャギーを抑える
            float edge = Mathf.Min(Mathf.Abs(k - 0.5f), Mathf.Min(k, 1f - k)) * s;
            Color c = k < 0.5f ? bossStripeColor : bossStripeDarkColor;
            if (edge < 1f)
                c = Color.Lerp(Color.Lerp(bossStripeColor, bossStripeDarkColor, 0.5f), c, edge);
            // 上下端に細い暗線
            if (y < 3 || y >= s - 3) c = bossStripeDarkColor;
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>角の丸い正三角形（inset で内側に縮めた版を作る）。</summary>
    private static Texture2D MakeTriangleTexture(int s, float inset)
    {
        var tex = NewTex(s, s);
        // 頂点（UV空間）
        var a = new Vector2(0.5f, 0.95f);
        var b = new Vector2(0.04f, 0.12f);
        var c = new Vector2(0.96f, 0.12f);
        var center = (a + b + c) / 3f;
        float scale = 1f - inset * 1.6f;
        a = center + (a - center) * scale;
        b = center + (b - center) * scale;
        c = center + (c - center) * scale;
        float round = 0.05f * scale;
        float aa = 1.5f / s;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            var p = new Vector2((x + 0.5f) / s, (y + 0.5f) / s);
            float d = SdTriangle(p, a, b, c) - round;
            float alpha = Mathf.Clamp01(0.5f - d / aa);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        tex.Apply();
        return tex;
    }

    // 三角形の符号付き距離（内側が負）。頂点を内側に寄せてから round を引いて角丸にする
    private static float SdTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        var center = (a + b + c) / 3f;
        float shrink = 0.06f;
        a = Vector2.Lerp(a, center, shrink);
        b = Vector2.Lerp(b, center, shrink);
        c = Vector2.Lerp(c, center, shrink);
        float d = Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, c), SegDist(p, c, a)));
        float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
        bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        return inside ? -d : d;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var pa = p - a; var ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
        return (pa - ba * h).magnitude;
    }

    private static Texture2D NewTex(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        return tex;
    }
}
