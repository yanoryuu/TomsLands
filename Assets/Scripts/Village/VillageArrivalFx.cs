using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// ラン終了→村へ帰還したときの「稼ぎ → 村資金」到着演出。
/// UIはランタイムで組み立てる（シーン配線はこのコンポーネントの追加とスプライト割り当てのみ）。
///
/// クリア: 金縁プレートに稼ぎ額 → 村への還元額に変わり、コインが村資金HUDへ飛んでカウントアップ。
/// 破産  : 色を落としたプレート・コインは少しだけ・トコ（悲しい）が画面下から覗く。
/// 表示する文字は金額のみ。クリック/任意キーでスキップ。演出中はプレイヤー入力とUIをブロックする。
/// 参照は全て null-safe（coinSprite 未設定なら CanPlay=false で Presenter が旧ポップへフォールバック）。
/// </summary>
public class VillageArrivalFx : MonoBehaviour
{
    [Header("スプライト")]
    [Tooltip("Assets/UI/Village/ArrivalFx/arrival_coin.png（必須）")]
    [SerializeField] private Sprite coinSprite;
    [Tooltip("Assets/UI/基本/3ゴールド.png（金額プレート）")]
    [SerializeField] private Sprite plaqueSprite;
    [Tooltip("Assets/UI/Village/ArrivalFx/arrival_glow.png（クリア時の後光）")]
    [SerializeField] private Sprite glowSprite;
    [Tooltip("Assets/UI/Village/ArrivalFx/arrival_sparkle.png（着弾のきらめき）")]
    [SerializeField] private Sprite sparkleSprite;
    [Tooltip("Assets/UI/Character/トコ/トコ_05_悲しい.png（破産時のみ）")]
    [SerializeField] private Sprite tokoSadSprite;
    [Tooltip("未設定なら村資金HUDのフォントを使う")]
    [SerializeField] private TMP_FontAsset fontOverride;

    [Header("調整")]
    [SerializeField] private int sortingOrderOffset = 50;
    [SerializeField] private Vector2 plaqueSize = new(509f, 156f); // 3ゴールド.png(424x130)の1.2倍
    [SerializeField] private float tokoHeight = 820f;

    // 3ゴールド.png 内のコイン中心（左上原点の正規化座標）
    private static readonly Vector2 PlaqueCoinUV = new(0.205f, 0.46f);

    private static readonly Color CreamText = new(1f, 0.95f, 0.84f, 1f);
    private static readonly Color Gold = new(0.91f, 0.64f, 0.24f, 1f); // #E8A33D

    public bool IsPlaying { get; private set; }

    /// <summary>演出に最低限必要な素材が揃っているか。</summary>
    public bool CanPlay => coinSprite != null;

    private RectTransform root;
    private Sequence mainSeq;
    private bool skipping;
    private float startTime;

    private Action<int> setFunds;
    private Action onComplete;
    private int fundsAfter;
    private Transform hudTarget;

    private readonly List<PlayerMove> lockedMovers = new();
    private readonly List<PlayerInput> lockedInputs = new();

    /// <summary>
    /// 到着演出を再生する。
    /// </summary>
    /// <param name="cleared">クリア帰還か（false=破産）</param>
    /// <param name="earned">稼ぎ（クリア=純資産 / 破産=手元現金）</param>
    /// <param name="converted">村資金へ変換された額</param>
    /// <param name="fundsAfterValue">変換後の村資金（現在値）</param>
    /// <param name="fundsText">村資金HUDのテキスト（コインの到達先）</param>
    /// <param name="setFundsDisplay">村資金HUDの表示更新（数値→表示）</param>
    /// <param name="completed">終了（スキップ含む）後に呼ばれる</param>
    public void Play(bool cleared, int earned, int converted, int fundsAfterValue,
        TMP_Text fundsText, Action<int> setFundsDisplay, Action completed)
    {
        if (IsPlaying) Cleanup(invokeComplete: false);

        setFunds = setFundsDisplay;
        onComplete = completed;
        fundsAfter = fundsAfterValue;
        hudTarget = fundsText != null ? fundsText.transform : null;

        var canvas = fundsText != null ? fundsText.canvas : GetComponentInParent<Canvas>();
        if (canvas != null) canvas = canvas.rootCanvas;
        if (canvas == null || !CanPlay)
        {
            setFunds?.Invoke(fundsAfter);
            var cb = onComplete;
            onComplete = null;
            cb?.Invoke();
            return;
        }

        IsPlaying = true;
        skipping = false;
        startTime = Time.unscaledTime;

        int fundsBefore = Mathf.Max(0, fundsAfter - Mathf.Max(0, converted));
        setFunds?.Invoke(fundsBefore);

        LockInput();
        BuildRoot(canvas);

        Vector2 target = ResolveTargetPos(fundsText);
        var font = fontOverride != null ? fontOverride : (fundsText != null ? fundsText.font : null);
        Color hudColor = fundsText != null ? fundsText.color : Gold;

        mainSeq = cleared
            ? BuildClearSequence(earned, converted, fundsBefore, target, font, hudColor)
            : BuildBankruptSequence(earned, converted, fundsBefore, target, font, hudColor);

        mainSeq.OnComplete(() => PlayOutro(cleared, target)).SetLink(gameObject);
        UIFx.PanelOpen(root.gameObject);
    }

    // ───────────────────────── 組み立て ─────────────────────────

    private void BuildRoot(Canvas rootCanvas)
    {
        var go = new GameObject("VillageArrivalFx", typeof(RectTransform));
        go.layer = rootCanvas.gameObject.layer;
        root = (RectTransform)go.transform;
        root.SetParent(rootCanvas.transform, false);
        Stretch(root);
        root.SetAsLastSibling();

        // 村HUD・投資パネルより手前に出す（同じソーティングレイヤーで順序だけ上げる）
        var c = go.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingLayerID = rootCanvas.sortingLayerID;
        c.sortingOrder = rootCanvas.sortingOrder + sortingOrderOffset;
        go.AddComponent<GraphicRaycaster>();

        // 透明ブロッカー（演出中のUIクリックを遮断）
        var blocker = CreateImage("Blocker", root, null);
        Stretch(blocker.rectTransform);
        blocker.color = new Color(0f, 0f, 0f, 0f);
        blocker.raycastTarget = true;
    }

    private Sequence BuildClearSequence(int earned, int converted, int fundsBefore, Vector2 target,
        TMP_FontAsset font, Color hudColor)
    {
        var seq = DOTween.Sequence();
        Vector2 plaquePos = new(0f, 60f);

        // 村をうっすら落としてプレートを主役に。コインが飛び始めたら明るさを戻す
        var dim = AddDim(seq, 0.22f);

        var card = CreateGroup("Card", root, plaquePos, plaqueSize);

        // 後光
        if (glowSprite != null)
        {
            var glow = CreateImage("Glow", card.transform, glowSprite);
            glow.rectTransform.sizeDelta = new Vector2(plaqueSize.x * 2.0f, plaqueSize.x * 1.15f);
            glow.color = new Color(1f, 1f, 1f, 0f);
            glow.rectTransform.localScale = Vector3.one * 0.4f;
            seq.Insert(0f, glow.DOFade(1f, 0.35f));
            seq.Insert(0f, glow.rectTransform.DOScale(1f, 0.55f).SetEase(Ease.OutCubic));
            seq.Insert(0.55f, glow.rectTransform.DOScale(1.08f, 0.5f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));
        }

        var plaque = CreatePlaque(card.transform, font, Color.white);
        var amount = plaque.amount;
        var plaqueRt = plaque.rt;
        amount.color = CreamText;
        amount.text = FormatG(0);

        plaqueRt.localScale = Vector3.one * 0.3f;
        seq.InsertCallback(0f, () => PlaySE("営業/SE_売上音"));
        seq.Insert(0f, plaqueRt.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
        seq.Insert(0.1f, CountTween(v => amount.text = FormatG(v), 0, earned, 0.6f).SetEase(Ease.OutCubic));

        // 稼ぎ → 村への還元額（数字が縦に潰れて切り替わる）
        float morphAt = 0.85f;
        seq.Insert(morphAt, amount.rectTransform.DOScaleY(0f, 0.08f).SetEase(Ease.InQuad));
        seq.InsertCallback(morphAt + 0.08f, () =>
        {
            amount.text = FormatG(converted);
            amount.color = hudColor;
        });
        seq.Insert(morphAt + 0.08f, amount.rectTransform.DOScaleY(1f, 0.18f).SetEase(Ease.OutBack));
        seq.Insert(morphAt + 0.08f, plaqueRt.DOPunchScale(Vector3.one * 0.08f, 0.25f, 6, 0.6f));

        float launchAt = morphAt + 0.3f;
        if (dim != null) seq.Insert(launchAt - 0.1f, dim.DOFade(0f, 0.45f));
        int coins = converted > 0
            ? Mathf.Clamp(6 + Mathf.RoundToInt(Mathf.Log10(Mathf.Max(1, converted)) * 3f), 8, 22)
            : 0;
        Vector2 coinStart = plaquePos + PlaqueCoinLocal();

        AddCoinFlights(seq, coins, launchAt, coinStart, target,
            interval: Mathf.Min(0.07f, 0.9f / Mathf.Max(1, coins)), flight: 0.6f,
            coinSize: new Vector2(40f, 56f), arc: 300f, burst: 70f, spins: 2.5f,
            hudPop: 1.12f, seEvery: 3, sparkleScale: 0.45f, plaque.coinIcon);

        AddTransferCount(seq, coins, launchAt, Mathf.Min(0.07f, 0.9f / Mathf.Max(1, coins)), 0.6f,
            converted, fundsBefore, amount, hold: 0.6f);

        return seq;
    }

    private Sequence BuildBankruptSequence(int earned, int converted, int fundsBefore, Vector2 target,
        TMP_FontAsset font, Color hudColor)
    {
        var seq = DOTween.Sequence();
        Vector2 plaquePos = new(200f, 0f);

        // しょんぼり感を出すため強めに落とす（コインが飛ぶ頃に戻す）
        var dim = AddDim(seq, 0.4f);

        // トコ（画面下から覗く）
        if (tokoSadSprite != null)
        {
            var toko = CreateImage("Toko", root, tokoSadSprite);
            toko.preserveAspect = true;
            var rt = toko.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            float aspect = tokoSadSprite.rect.width / Mathf.Max(1f, tokoSadSprite.rect.height);
            rt.sizeDelta = new Vector2(tokoHeight * aspect, tokoHeight);
            float restY = -tokoHeight * 0.5f;
            rt.anchoredPosition = new Vector2(500f, -tokoHeight - 40f);
            seq.Insert(0f, rt.DOAnchorPosY(restY, 0.55f).SetEase(Ease.OutCubic));
            // しょんぼり（少し傾いて沈む）
            seq.Insert(0.6f, rt.DOLocalRotate(new Vector3(0f, 0f, -3f), 0.7f).SetEase(Ease.InOutSine));
            seq.Insert(0.6f, rt.DOAnchorPosY(restY - 14f, 0.7f).SetEase(Ease.InOutSine));
        }

        var card = CreateGroup("Card", root, plaquePos, plaqueSize * 0.9f);
        var cardGroup = card.GetComponent<CanvasGroup>();
        cardGroup.alpha = 0f;

        var plaque = CreatePlaque(card.transform, font, new Color(0.64f, 0.61f, 0.6f, 1f));
        var amount = plaque.amount;
        var plaqueRt = plaque.rt;
        amount.color = new Color(0.86f, 0.82f, 0.78f, 1f);
        amount.text = FormatG(0);
        if (plaque.coinIcon != null) plaque.coinIcon.color = new Color(0.8f, 0.76f, 0.72f, 1f);

        plaqueRt.localScale = Vector3.one * 0.92f;
        seq.Insert(0.15f, cardGroup.DOFade(1f, 0.3f));
        seq.Insert(0.15f, plaqueRt.DOScale(1f, 0.35f).SetEase(Ease.OutCubic));
        seq.Insert(0.25f, CountTween(v => amount.text = FormatG(v), 0, earned, 0.5f).SetEase(Ease.OutCubic));

        float morphAt = 0.95f;
        seq.Insert(morphAt, amount.rectTransform.DOScaleY(0f, 0.1f).SetEase(Ease.InQuad));
        seq.InsertCallback(morphAt + 0.1f, () =>
        {
            amount.text = FormatG(converted);
            amount.color = Color.Lerp(hudColor, new Color(0.8f, 0.76f, 0.72f, 1f), 0.35f);
        });
        seq.Insert(morphAt + 0.1f, amount.rectTransform.DOScaleY(1f, 0.2f).SetEase(Ease.OutCubic));
        seq.Insert(morphAt + 0.1f, plaqueRt.DOAnchorPosY(plaqueRt.anchoredPosition.y - 8f, 0.4f).SetEase(Ease.OutSine));

        float launchAt = morphAt + 0.35f;
        if (dim != null) seq.Insert(launchAt - 0.1f, dim.DOFade(0f, 0.6f));
        int coins = converted > 0 ? Mathf.Clamp(converted / 200 + 1, 1, 3) : 0;
        Vector2 coinStart = plaquePos + PlaqueCoinLocal() * 0.9f;

        AddCoinFlights(seq, coins, launchAt, coinStart, target,
            interval: 0.28f, flight: 0.95f,
            coinSize: new Vector2(34f, 48f), arc: 90f, burst: 30f, spins: 1f,
            hudPop: 1.06f, seEvery: 1, sparkleScale: 0f, plaque.coinIcon);

        AddTransferCount(seq, coins, launchAt, 0.28f, 0.95f, converted, fundsBefore, amount, hold: 0.8f);

        return seq;
    }

    /// <summary>画面を薄く暗くする（ブロッカーの直上・演出部品の下）。</summary>
    private Image AddDim(Sequence seq, float alpha)
    {
        if (root == null) return null;
        var dim = CreateImage("Dim", root, null);
        Stretch(dim.rectTransform);
        dim.color = new Color(0.08f, 0.05f, 0.03f, 0f);
        seq.Insert(0f, dim.DOFade(alpha, 0.3f));
        return dim;
    }

    /// <summary>プレートから村資金HUDへ飛ぶコイン群を追加する。</summary>
    private void AddCoinFlights(Sequence seq, int count, float launchAt, Vector2 start, Vector2 target,
        float interval, float flight, Vector2 coinSize, float arc, float burst, float spins,
        float hudPop, int seEvery, float sparkleScale, Image plaqueCoin)
    {
        if (count <= 0) return;

        var layer = CreateGroup("Coins", root, Vector2.zero, Vector2.zero);
        Stretch((RectTransform)layer.transform);

        for (int i = 0; i < count; i++)
        {
            var coin = CreateImage("Coin", layer.transform, coinSprite);
            coin.preserveAspect = true;
            var rt = coin.rectTransform;
            rt.sizeDelta = coinSize;
            rt.anchoredPosition = start;
            coin.gameObject.SetActive(false);

            // 放射状に少し弾けてから、弧を描いてHUDへ吸い込まれる
            float ang = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector2 burstPos = start + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f + 0.4f) * burst
                               * UnityEngine.Random.Range(0.6f, 1f);
            Vector2 mid = (burstPos + target) * 0.5f;
            Vector2 ctrl = mid + new Vector2(UnityEngine.Random.Range(-arc, arc) * 0.6f, arc * UnityEngine.Random.Range(0.6f, 1f));
            float phase = UnityEngine.Random.Range(0f, 1f);

            float t0 = launchAt + i * interval;
            int index = i;
            seq.InsertCallback(t0, () =>
            {
                coin.gameObject.SetActive(true);
                if (!skipping && plaqueCoin != null) UIFx.Pop(plaqueCoin.transform, 1.25f, 0.12f);
            });
            seq.Insert(t0, DOTween.To(() => 0f, u =>
            {
                const float burstT = 0.22f;
                Vector2 pos;
                float s;
                if (u < burstT)
                {
                    float k = 1f - Mathf.Pow(1f - u / burstT, 3f);
                    pos = Vector2.LerpUnclamped(start, burstPos, k);
                    s = Mathf.Lerp(0.3f, 1.1f, k);
                }
                else
                {
                    float k = (u - burstT) / (1f - burstT);
                    k = k * k * (3f - 2f * k) * 0.35f + k * k * 0.65f; // 後半で加速して吸い込まれる
                    float o = 1f - k;
                    pos = o * o * burstPos + 2f * o * k * ctrl + k * k * target;
                    s = Mathf.Lerp(1.1f, 0.7f, k);
                }
                rt.anchoredPosition = pos;
                float spin = Mathf.Max(0.18f, Mathf.Abs(Mathf.Cos((u * spins + phase) * Mathf.PI)));
                rt.localScale = new Vector3(s * spin, s, 1f);
            }, 1f, flight).SetEase(Ease.Linear));
            seq.InsertCallback(t0 + flight, () =>
            {
                coin.gameObject.SetActive(false);
                if (skipping) return;
                if (hudTarget != null) UIFx.Pop(hudTarget, hudPop, 0.14f);
                if (sparkleScale > 0f) SpawnSparkle(target, sparkleScale);
                if (seEvery > 0 && index % seEvery == 0) PlaySE("営業/SE_数の増減");
            });
        }
    }

    /// <summary>プレートの額が減り、村資金HUDが増える（コインの着弾区間に合わせる）。</summary>
    private void AddTransferCount(Sequence seq, int coins, float launchAt, float interval, float flight,
        int converted, int fundsBefore, TMP_Text amount, float hold)
    {
        if (coins <= 0)
        {
            seq.AppendInterval(hold);
            return;
        }

        float firstArrive = launchAt + flight;
        float window = Mathf.Max(0.2f, (coins - 1) * interval + 0.05f);
        seq.Insert(firstArrive - 0.05f, DOTween.To(() => 0f, x =>
        {
            amount.text = FormatG(Mathf.RoundToInt(Mathf.Lerp(converted, 0f, x)));
            setFunds?.Invoke(Mathf.RoundToInt(Mathf.Lerp(fundsBefore, fundsAfter, x)));
        }, 1f, window).SetEase(Ease.Linear));
        seq.AppendInterval(0.25f);
    }

    // ───────────────────────── 終了・スキップ ─────────────────────────

    private void Update()
    {
        if (!IsPlaying || mainSeq == null || skipping) return;
        if (Time.unscaledTime - startTime < 0.2f) return;
        if (SkipPressed()) Skip();
    }

    /// <summary>演出を最終状態まで飛ばす（外部からも呼べる）。</summary>
    public void Skip()
    {
        if (!IsPlaying || skipping) return;
        skipping = true;
        if (mainSeq != null && mainSeq.IsActive()) mainSeq.Complete(true);
    }

    private void PlayOutro(bool cleared, Vector2 target)
    {
        mainSeq = null;
        setFunds?.Invoke(fundsAfter);

        if (hudTarget != null) UIFx.Pop(hudTarget, cleared ? 1.22f : 1.08f, cleared ? 0.22f : 0.16f);
        if (cleared)
        {
            SpawnSparkle(target, 1.1f);
            PlaySE("営業/SE_仕入れ完了");
        }

        if (root == null)
        {
            Cleanup(invokeComplete: true);
            return;
        }

        var cg = root.GetComponent<CanvasGroup>();
        if (cg == null) cg = root.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = true;
        float dur = skipping ? 0.18f : (cleared ? 0.3f : 0.45f);
        cg.DOKill();
        var outro = DOTween.Sequence();
        outro.Insert(0f, cg.DOFade(0f, dur).SetEase(Ease.InQuad));
        var card = root.Find("Card");
        if (card != null) outro.Insert(0f, card.DOScale(cleared ? 1.06f : 0.96f, dur).SetEase(Ease.OutQuad));
        var toko = root.Find("Toko") as RectTransform;
        if (toko != null) outro.Insert(0f, toko.DOAnchorPosY(toko.anchoredPosition.y - 120f, dur).SetEase(Ease.InQuad));
        outro.OnComplete(() => Cleanup(invokeComplete: true)).SetLink(root.gameObject);
    }

    private void Cleanup(bool invokeComplete)
    {
        if (mainSeq != null && mainSeq.IsActive()) mainSeq.Kill();
        mainSeq = null;
        if (root != null)
        {
            root.DOKill(true);
            Destroy(root.gameObject);
            root = null;
        }
        UnlockInput();
        IsPlaying = false;
        skipping = false;

        var cb = onComplete;
        onComplete = null;
        if (invokeComplete)
        {
            setFunds?.Invoke(fundsAfter);
            cb?.Invoke();
        }
    }

    private void OnDestroy()
    {
        if (mainSeq != null && mainSeq.IsActive()) mainSeq.Kill();
        mainSeq = null;
        UnlockInput();
    }

    private static bool SkipPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
        var mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
        var touch = Touchscreen.current;
        if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
        var pad = Gamepad.current;
        if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)) return true;
        return false;
    }

    // ───────────────────────── 入力ロック ─────────────────────────

    private void LockInput()
    {
        lockedInputs.Clear();
        foreach (var pi in PlayerInput.all)
        {
            if (pi == null || !pi.inputIsActive) continue;
            pi.DeactivateInput(); // 進行中のMoveはキャンセルされ、移動入力がゼロに戻る
            lockedInputs.Add(pi);
        }

        lockedMovers.Clear();
        foreach (var mover in FindObjectsByType<PlayerMove>(FindObjectsSortMode.None))
        {
            if (mover == null || !mover.enabled) continue;
            mover.enabled = false;
            lockedMovers.Add(mover);
        }
    }

    private void UnlockInput()
    {
        foreach (var pi in lockedInputs)
            if (pi != null) pi.ActivateInput();
        lockedInputs.Clear();

        foreach (var mover in lockedMovers)
            if (mover != null) mover.enabled = true;
        lockedMovers.Clear();
    }

    // ───────────────────────── 部品 ─────────────────────────

    private (RectTransform rt, TextMeshProUGUI amount, Image coinIcon) CreatePlaque(Transform parent,
        TMP_FontAsset font, Color tint)
    {
        var plaque = CreateImage("Plaque", parent, plaqueSprite);
        var rt = plaque.rectTransform;
        rt.sizeDelta = ((RectTransform)parent).sizeDelta;
        if (plaqueSprite != null) plaque.color = tint;
        else plaque.color = new Color(0.33f * tint.r, 0.19f * tint.g, 0.08f * tint.b, 0.95f);

        // 3ゴールド.pngにはコインが描かれているので、それを「発射口」として扱う。
        // 素材が無いときだけコインを重ねて描く。
        Image coinIcon = null;
        if (plaqueSprite == null && coinSprite != null)
        {
            coinIcon = CreateImage("Coin", rt, coinSprite);
            coinIcon.preserveAspect = true;
            coinIcon.rectTransform.sizeDelta = new Vector2(rt.sizeDelta.y * 0.4f, rt.sizeDelta.y * 0.56f);
            coinIcon.rectTransform.anchoredPosition = PlaqueCoinLocal(rt.sizeDelta);
        }

        var go = new GameObject("Amount", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var trt = (RectTransform)go.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = new Vector2(0.33f, 0.12f);
        trt.anchorMax = new Vector2(0.93f, 0.88f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.raycastTarget = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 24f;
        tmp.fontSizeMax = rt.sizeDelta.y * 0.42f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.characterSpacing = 2f;

        return (rt, tmp, coinIcon);
    }

    private Vector2 PlaqueCoinLocal() => PlaqueCoinLocal(plaqueSize);

    private static Vector2 PlaqueCoinLocal(Vector2 size)
        => new((PlaqueCoinUV.x - 0.5f) * size.x, (0.5f - PlaqueCoinUV.y) * size.y);

    private void SpawnSparkle(Vector2 pos, float scale)
    {
        if (sparkleSprite == null || root == null) return;
        var img = CreateImage("Sparkle", root, sparkleSprite);
        var rt = img.rectTransform;
        rt.sizeDelta = new Vector2(96f, 96f);
        rt.anchoredPosition = pos + UnityEngine.Random.insideUnitCircle * 10f;
        rt.localScale = Vector3.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-20f, 20f));
        var go = img.gameObject;
        var s = DOTween.Sequence();
        s.Insert(0f, rt.DOScale(scale, 0.14f).SetEase(Ease.OutBack));
        s.Insert(0f, rt.DOLocalRotate(new Vector3(0f, 0f, 45f), 0.34f, RotateMode.LocalAxisAdd));
        s.Insert(0.14f, img.DOFade(0f, 0.2f));
        s.Insert(0.14f, rt.DOScale(scale * 0.4f, 0.2f).SetEase(Ease.InQuad));
        s.OnComplete(() => { if (go != null) Destroy(go); }).SetLink(go);
    }

    private Vector2 ResolveTargetPos(TMP_Text fundsText)
    {
        if (fundsText == null || root == null) return new Vector2(0f, 400f);

        Canvas.ForceUpdateCanvases(); // 初回フレームでもレイアウト確定後の位置を取る
        var trt = fundsText.rectTransform;
        Vector3 local = trt.rect.center;
        fundsText.ForceMeshUpdate();
        var b = fundsText.textBounds;
        if (b.size.x > 0.01f)
        {
            // 「村資金 12,345G」の左脇に着弾させる（カウントアップ中の数字をきらめきで隠さない）
            local = new Vector3(b.min.x - 30f, b.center.y, 0f);
        }
        Vector3 world = trt.TransformPoint(local);
        return root.InverseTransformPoint(world);
    }

    private GameObject CreateGroup(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var cg = go.GetComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;
        return go;
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static Tween CountTween(Action<int> apply, int from, int to, float duration)
        => DOTween.To(() => 0f, x => apply(Mathf.RoundToInt(Mathf.Lerp(from, to, x))), 1f, duration);

    private static string FormatG(int value) => $"{value:N0}G";


    private static void PlaySE(string key) => SoundManager.Instance?.PlaySE(key);
}
