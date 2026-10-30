using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>演出の再生モード。<see cref="UIMotion.Mode"/> で全演出に効く。</summary>
public enum UIMotionMode
{
    /// <summary>そのままの長さで再生する。</summary>
    Normal = 0,
    /// <summary>「演出を速くする」（GameSettings.EffectDurationScale に従って短くする）。</summary>
    Fast = 1,
    /// <summary>省略。動きを出さずに即座に最終状態にする（オートプレイ・人格マラソン）。</summary>
    Skip = 2,
}

/// <summary>
/// UI アニメーションの共通部品の入口。仕様と使い方: Docs/UI_Motion.md
///
/// ・速さ／省略の切替はここ 1 箇所（<see cref="Mode"/>）。各部品は必ず <see cref="Run{T}"/> を通して Tween を再生するので、
///   通常 / 速い（Tween.timeScale を 1/EffectDurationScale に）/ 省略（Complete して即最終状態）が全部に効く。
/// ・省略モードでも最終状態（表示・非表示・数値・SetActive）は通常と同じにする。
/// ・CanvasGroup は alpha だけ触る（interactable / blocksRaycasts は触らない。オートプレイが押せない判定をするため）。
/// ・この部品の #1 ぽよん開閉 と #3 順番に並ぶ もこのクラスにある。
/// </summary>
public static class UIMotion
{
    // =====================================================
    // 速さ・省略モード
    // =====================================================

    private static readonly HashSet<string> SkipSources = new();
    private static readonly HashSet<string> ShowcaseSources = new();
    private static bool? fastCache;
    private static bool settingsHooked;

    /// <summary>強制モード（撮影ツール・テスト用）。null なら下の規則で決める。</summary>
    public static UIMotionMode? ForcedMode { get; set; }

    /// <summary>
    /// 「見せるための再生」か（録画中など）。エディタの録画（GameplayAutoRecorder.IsRecording）を
    /// UIMotionEditorHooks（Editor/UIMotionCapture.cs）が入れる。true の間は省略要求があっても通常（ユーザー設定）で再生する。
    /// </summary>
    public static Func<bool> ShowcaseProbe { get; set; }

    /// <summary>
    /// 今のモード。優先順: ForcedMode → 見せる再生（録画中・録画ありマラソン）ならユーザー設定 → 省略要求（オートプレイ・マラソン）があれば Skip → ユーザー設定（演出を速くする）。
    /// </summary>
    public static UIMotionMode Mode
    {
        get
        {
            if (ForcedMode.HasValue) return ForcedMode.Value;
            if (SkipSources.Count > 0 && !IsShowcase) return UIMotionMode.Skip;
            return FastSetting ? UIMotionMode.Fast : UIMotionMode.Normal;
        }
    }

    /// <summary>省略モードか（true なら演出を出さずに最終状態だけ作る）。</summary>
    public static bool IsSkip => Mode == UIMotionMode.Skip;

    /// <summary>Tween の再生速度の倍率（通常 1、速い 1/EffectDurationScale）。</summary>
    public static float Speed => Mode == UIMotionMode.Fast ? 1f / Mathf.Max(0.1f, GameSettings.EffectDurationScale) : 1f;

    /// <summary>演出の長さをモードに合わせる（UniTask.Delay で待つときなど。省略なら 0）。</summary>
    public static float Dur(float seconds) => IsSkip ? 0f : seconds / Speed;

    private static bool IsShowcase
    {
        get
        {
            if (ShowcaseSources.Count > 0) return true;
            if (ShowcaseProbe == null) return false;
            try { return ShowcaseProbe(); }
            catch (Exception e) { Debug.LogException(e); return false; }
        }
    }

    private static bool FastSetting
    {
        get
        {
            if (!settingsHooked)
            {
                settingsHooked = true;
                GameSettings.Changed += () => fastCache = null;
            }
            fastCache ??= GameSettings.FastEffects;
            return fastCache.Value;
        }
    }

    /// <summary>
    /// 省略を要求する／やめる（source ごとに数える。どれか 1 つでも立っていれば省略）。
    /// AutoPlayController の開始/終了、PersonaMarathon（録画なし）の開始/終了から呼ぶ。
    /// </summary>
    public static void SetSkip(string source, bool on)
    {
        if (string.IsNullOrEmpty(source)) return;
        bool changed = on ? SkipSources.Add(source) : SkipSources.Remove(source);
        if (changed) Debug.Log($"[UIMotion] 省略要求 {(on ? "ON" : "OFF")}: {source} → {Mode}");
    }

    /// <summary>見せる再生（録画ありマラソンなど）を宣言する／やめる。立っている間は省略要求を無視する。</summary>
    public static void SetShowcase(string source, bool on)
    {
        if (string.IsNullOrEmpty(source)) return;
        bool changed = on ? ShowcaseSources.Add(source) : ShowcaseSources.Remove(source);
        if (changed) Debug.Log($"[UIMotion] 見せる再生 {(on ? "ON" : "OFF")}: {source} → {Mode}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // ドメインの再読み込みを切った Play でも前回の要求を持ち越さない（エディタの ShowcaseProbe は残す）
        SkipSources.Clear();
        ShowcaseSources.Clear();
        ForcedMode = null;
        fastCache = null;
        BaseScales.Clear();
        BasePositions.Clear();
        Nudges.Clear();
        Closing.Clear();
        Counters.Clear();
        UIMotionFx.ResetLayers();
    }

    // =====================================================
    // 撮影ツール用（Editor/UIMotionCapture.cs）
    // =====================================================

    /// <summary>true なら全 Tween を UpdateType.Manual にする（編集モードで DOTween.ManualUpdate で 1 コマずつ進めるため）。</summary>
    public static bool ManualUpdate { get; set; }

    /// <summary>撮影時だけ設定する。コード生成の Canvas を ScreenSpaceCamera でこのカメラに描く。</summary>
    public static Camera CaptureCamera { get; set; }

    // =====================================================
    // Tween の再生（全部品の共通の通り道）
    // =====================================================

    /// <summary>
    /// Tween をモードに合わせて再生する。SetLink・更新方式・速さを付け、省略モードなら即 Complete（OnComplete も呼ぶ）。
    /// unscaled=true は timeScale に左右されない（一時停止中も動く：トースト・幕・祝い）。
    /// </summary>
    public static T Run<T>(T tween, GameObject link, bool unscaled = false) where T : Tween
    {
        if (tween == null) return null;
        if (link != null) tween.SetLink(link);
        if (ManualUpdate) tween.SetUpdate(UpdateType.Manual, unscaled);
        else if (unscaled) tween.SetUpdate(true);
        tween.timeScale = Speed;
        if (IsSkip) tween.Complete(true);
        return tween;
    }

    /// <summary>
    /// ずっと繰り返す Tween（アイドル・脈打ち）を再生する。省略モードでは再生せず Kill して null を返す（ループは最終状態が無いため）。
    /// </summary>
    public static T RunLoop<T>(T tween, GameObject link, bool unscaled = false) where T : Tween
    {
        if (tween == null) return null;
        if (IsSkip)
        {
            tween.Kill();
            return null;
        }
        if (link != null) tween.SetLink(link);
        if (ManualUpdate) tween.SetUpdate(UpdateType.Manual, unscaled);
        else if (unscaled) tween.SetUpdate(true);
        tween.timeScale = Speed;
        return tween;
    }

    /// <summary>モードに合わせた遅延呼び出し（省略なら即呼ぶ）。</summary>
    public static Tween Delay(float seconds, Action action, GameObject link, bool unscaled = false)
    {
        if (IsSkip || seconds <= 0f)
        {
            action?.Invoke();
            return null;
        }
        return Run(DOVirtual.DelayedCall(seconds, () => action?.Invoke(), false), link, unscaled);
    }

    // =====================================================
    // #1 ぽよん開閉
    // =====================================================

    private static readonly HashSet<GameObject> Closing = new();

    /// <summary>
    /// #1 ぽよんと開く: SetActive(true) → 窓が 0.6→1.06→1 に弾む（0.25 秒）＋ フェード。
    /// window を省くとパネル自身を拡縮する（ルートの Canvas ならフェードだけ）。閉じ演出中なら取り消して開く。
    /// </summary>
    public static void Open(GameObject panel, RectTransform window = null)
    {
        if (panel == null) return;
        CancelClose(panel);
        panel.SetActive(true);
        if (!panel.activeInHierarchy) return; // 親が閉じている。表示状態だけ合わせる

        var group = EnsureGroup(panel);
        var win = window != null ? window : ScalableRoot(panel);
        // 先に前の開閉を止める（その OnKill が窓を元の大きさに戻す）。止める前に読むと、窓がパネルと別の矩形のときは
        // 「動いている」と分からず、開きかけの縮んだ大きさを基準として覚えてしまい、縮んだまま残る
        DOTween.Kill(panel);
        Vector3 baseScale = win != null ? BaseScaleOf(win) : Vector3.one;

        if (IsSkip)
        {
            group.alpha = 1f;
            if (win != null) win.localScale = baseScale;
            return;
        }

        group.alpha = 0f;
        var seq = DOTween.Sequence().SetId(panel);
        seq.Insert(0f, group.DOFade(1f, 0.18f).SetEase(Ease.OutQuad));
        if (win != null)
        {
            win.localScale = baseScale * 0.6f;
            seq.Insert(0f, win.DOScale(baseScale * 1.06f, 0.17f).SetEase(Ease.OutQuad));
            seq.Insert(0.17f, win.DOScale(baseScale, 0.08f).SetEase(Ease.InOutSine));
        }
        seq.OnKill(() =>
        {
            if (group != null) group.alpha = 1f;
            if (win != null) win.localScale = baseScale;
        });
        Run(seq, panel);
    }

    /// <summary>
    /// #1 縮んで消える（0.15 秒）→ SetActive(false) → 拡縮・alpha を元に戻す → onDone。
    /// 省略モード・非表示中は即 SetActive(false)。閉じ演出中かは <see cref="IsClosing"/> で分かる。
    /// </summary>
    public static void Close(GameObject panel, Action onDone = null, RectTransform window = null)
    {
        if (panel == null)
        {
            onDone?.Invoke();
            return;
        }
        if (Closing.Contains(panel)) return; // 二重に閉じない（onDone は最初の呼び出しのものだけ）

        var win = window != null ? window : ScalableRoot(panel);
        DOTween.Kill(panel); // 開きかけなら先に止めて元の大きさに戻してから基準を読む（Open と同じ理由）
        Vector3 baseScale = win != null ? BaseScaleOf(win) : Vector3.one;
        panel.TryGetComponent<CanvasGroup>(out var group);

        if (IsSkip || !panel.activeInHierarchy)
        {
            FinishClose(panel, group, win, baseScale);
            onDone?.Invoke();
            return;
        }

        group = EnsureGroup(panel);
        Closing.Add(panel);
        var seq = DOTween.Sequence().SetId(panel);
        seq.Insert(0f, group.DOFade(0f, 0.15f).SetEase(Ease.InQuad));
        if (win != null) seq.Insert(0f, win.DOScale(baseScale * 0.85f, 0.15f).SetEase(Ease.InBack));
        seq.OnComplete(() =>
        {
            FinishClose(panel, group, win, baseScale);
            onDone?.Invoke();
        });
        seq.OnKill(() =>
        {
            Closing.Remove(panel);
            // 途中で止められても縮んだまま・薄いまま残さない（閉じ終わりは FinishClose が同じ値にしている）
            if (group != null) group.alpha = 1f;
            if (win != null) win.localScale = baseScale;
        });
        Run(seq, panel);
    }

    /// <summary>閉じ演出中（見た目はまだ出ているが、閉じたものとして扱うべき）か。</summary>
    public static bool IsClosing(GameObject panel) => panel != null && Closing.Contains(panel);

    /// <summary>閉じ演出を取り消して元の見た目に戻す（表示状態はそのまま）。UIFx.PanelOpen からも呼ぶ。</summary>
    public static void CancelClose(GameObject panel)
    {
        if (panel == null || !Closing.Remove(panel)) return;
        DOTween.Kill(panel);
        if (panel.TryGetComponent<CanvasGroup>(out var group)) group.alpha = 1f;
        var win = ScalableRoot(panel);
        if (win != null && BaseScales.TryGetValue(win.GetInstanceID(), out var s)) win.localScale = s;
    }

    private static void FinishClose(GameObject panel, CanvasGroup group, RectTransform win, Vector3 baseScale)
    {
        Closing.Remove(panel);
        panel.SetActive(false);
        if (group != null) group.alpha = 1f;
        if (win != null) win.localScale = baseScale;
    }

    // =====================================================
    // #3 順番に並んで出る
    // =====================================================

    /// <summary>
    /// #3 子の行を上から順に出す（1 行 0.25 秒・0.07 秒ずつ遅れ・左から 28px）。非表示の子は数えない。
    /// 位置は覚えず横のずらしだけを足すので、レイアウトグループの下・作り直した直後・途中で閉じた場合でも、最後はレイアウトどおりの位置に戻る。
    /// </summary>
    public static void Stagger(Transform parent, float step = 0.07f, float distance = 28f, float duration = 0.25f, float delay = 0f)
    {
        if (parent == null) return;
        var rows = new List<RectTransform>(parent.childCount);
        foreach (Transform child in parent)
            if (child.gameObject.activeSelf && child is RectTransform rt) rows.Add(rt);
        Stagger(rows, step, distance, duration, delay);
    }

    /// <summary>#3 渡した行を順に出す。</summary>
    public static void Stagger(IList<RectTransform> rows, float step = 0.07f, float distance = 28f, float duration = 0.25f, float delay = 0f)
    {
        if (rows == null || rows.Count == 0) return;
        var parent = rows[0] != null ? rows[0].parent as RectTransform : null;
        if (parent != null && parent.TryGetComponent<LayoutGroup>(out _))
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null) continue;
            // 前の登場の途中なら止める（その OnKill がずらしを外す）
            DOTween.Kill(row.gameObject);
            var group = EnsureGroup(row.gameObject);
            // 位置は「呼んだ時点の座標」を覚えて戻すのではなく、そこからの横のずらしだけを持つ（Nudge）。
            // 一覧を作り直すと Destroy した古い行がフレームの終わりまで残り、新しい行は一瞬その下に並ぶ。
            // 覚えた座標へ戻すと、レイアウトが正しい位置に直した後も古い座標（下）へ戻してしまい、行が下にずれて押せなくなる
            var nudge = NudgeOf(row);
            nudge.Clear();
            if (IsSkip)
            {
                group.alpha = 1f;
                continue;
            }
            group.alpha = 0f;
            var seq = DOTween.Sequence().SetId(row.gameObject);
            float at = delay + step * i;
            seq.Insert(at, group.DOFade(1f, duration * 0.6f));
            if (!Mathf.Approximately(distance, 0f))
            {
                float x = -distance;
                nudge.Set(new Vector2(x, 0f));
                seq.Insert(at, DOTween.To(() => x, v => { x = v; nudge.Set(new Vector2(v, 0f)); }, 0f, duration).SetEase(Ease.OutBack, 1.2f));
            }
            seq.OnKill(() =>
            {
                if (group != null) group.alpha = 1f;
                nudge.Clear();
            });
            Run(seq, row.gameObject);
        }
    }

    // =====================================================
    // 位置のずらし（レイアウトの位置を覚え込まない）
    // =====================================================

    private static readonly Dictionary<int, Nudge> Nudges = new();

    /// <summary>rt の位置のずらしを持つもの（rt ごとに 1 つ）。</summary>
    internal static Nudge NudgeOf(RectTransform rt)
    {
        int id = rt.GetInstanceID();
        if (!Nudges.TryGetValue(id, out var n) || !n.IsFor(rt))
        {
            n = new Nudge(rt);
            Nudges[id] = n;
        }
        return n;
    }

    /// <summary>
    /// 演出の間だけ anchoredPosition に足す「ずらし」。元の位置は覚えず、毎回「いまの位置 − 自分が足した分」を元の位置とみなす。
    /// 演出の途中でレイアウト（や他のコード）が位置を書き直したら、その位置を新しい元の位置として受け入れる。
    /// これで、作り直した一覧・移動した窓でも、演出が終われば必ず正しい位置に戻る（2 回目・途中で閉じた場合も）。
    /// </summary>
    internal sealed class Nudge
    {
        private readonly RectTransform rt;
        private Vector2 offset;
        private Vector2 written;
        private bool applied;

        public Nudge(RectTransform rt) => this.rt = rt;

        public bool IsFor(RectTransform t) => rt != null && rt == t;

        /// <summary>ずらしを外したときの位置。</summary>
        public Vector2 Rest
        {
            get
            {
                if (rt == null) return default;
                Vector2 cur = rt.anchoredPosition;
                // 最後に自分が書いた位置のままなら、自分のずらしを引く。違えば誰かが書き直したので、それが元の位置
                return applied && (cur - written).sqrMagnitude < 1e-4f ? written - offset : cur;
            }
        }

        public void Set(Vector2 o)
        {
            if (rt == null) return;
            Vector2 rest = Rest;
            offset = o;
            written = rest + o;
            applied = true;
            rt.anchoredPosition = written;
        }

        public void Clear()
        {
            if (rt == null || !applied) return;
            Vector2 rest = Rest;
            applied = false;
            offset = Vector2.zero;
            rt.anchoredPosition = rest;
        }
    }

    // =====================================================
    // 小さな共通動作
    // =====================================================

    /// <summary>弾む（基準の大きさ → peak → 少し縮む → 基準）。財布・数字・アイコンの「受け取った」合図。</summary>
    public static Tween Bump(Transform target, float peak = 1.12f, float duration = 0.2f, bool unscaled = false)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return null;
        Vector3 b = BaseScaleOf(target);
        target.DOKill();
        if (IsSkip)
        {
            target.localScale = b;
            return null;
        }
        float dip = 1f - (peak - 1f) * 0.25f;
        var seq = DOTween.Sequence().SetTarget(target);
        seq.Append(target.DOScale(b * peak, duration * 0.35f).SetEase(Ease.OutQuad));
        seq.Append(target.DOScale(b * dip, duration * 0.35f).SetEase(Ease.InOutSine));
        seq.Append(target.DOScale(b, duration * 0.3f).SetEase(Ease.OutSine));
        seq.OnKill(() => { if (target != null) target.localScale = b; });
        return Run(seq, target.gameObject, unscaled);
    }

    /// <summary>
    /// 横に首振り（押せないボタン・ゲージが減ったとき）。GameSettings.ReduceShake なら幅を 3 割に。
    /// 位置は必ず元に戻す。
    /// </summary>
    public static Tween Shake(RectTransform target, float strength = 6f, float duration = 0.25f, bool unscaled = false)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return null;
        DOTween.Kill(ShakeId(target));
        // 位置は覚えず、横のずらしだけ（レイアウトの中のボタンでも、揺れている間に並びが変わっても正しい位置に戻る）
        var nudge = NudgeOf(target);
        nudge.Clear();
        if (IsSkip) return null;
        if (GameSettings.ReduceShake) strength *= 0.3f;
        float s = duration / 5f;
        float x = 0f;
        Tween Step(float to) => DOTween.To(() => x, v => { x = v; nudge.Set(new Vector2(v, 0f)); }, to, s);
        var seq = DOTween.Sequence().SetId(ShakeId(target));
        seq.Append(Step(-strength));
        seq.Append(Step(strength));
        seq.Append(Step(-strength * 0.66f));
        seq.Append(Step(strength * 0.5f));
        seq.Append(Step(0f));
        seq.OnKill(() => nudge.Clear());
        return Run(seq, target.gameObject, unscaled);
    }

    private static string ShakeId(Transform t) => "UIMotion.Shake." + t.GetInstanceID();

    // =====================================================
    // #2 の一部: 数字を回す
    // =====================================================

    private static readonly Dictionary<int, Tween> Counters = new();

    /// <summary>
    /// 数字を from → to へ数え上げる（既定 0.8 秒）。同じテキストで前のカウントが残っていれば止めて差し替える。
    /// format 省略時は「1,234」。最終的な文字は必ず format(to)。
    /// </summary>
    public static Tween CountTo(TMP_Text text, long from, long to, Func<long, string> format = null, float duration = 0.8f, bool unscaled = false)
    {
        if (text == null) return null;
        format ??= v => v.ToString("N0");
        int key = text.GetInstanceID();
        if (Counters.TryGetValue(key, out var prev) && prev.IsActive()) prev.Kill();
        Counters.Remove(key);

        if (IsSkip || from == to || !text.gameObject.activeInHierarchy)
        {
            text.text = format(to);
            return null;
        }
        long shown = long.MinValue;
        var tween = DOVirtual.Float(0f, 1f, duration, t =>
            {
                long v = from + (long)Math.Round((to - from) * (double)t);
                if (v == shown) return;
                shown = v;
                text.text = format(v);
            })
            .SetEase(Ease.OutCubic)
            .OnComplete(() => text.text = format(to));
        Counters[key] = tween;
        return Run(tween, text.gameObject, unscaled);
    }

    // =====================================================
    // 下回り
    // =====================================================

    private static readonly Dictionary<int, Vector3> BaseScales = new();
    private static readonly Dictionary<int, Vector2> BasePositions = new();

    /// <summary>
    /// 演出前の大きさ。動いていない（Tween が無い）ときは今の値を基準として覚え直し、動いている途中なら覚えた基準を返す。
    /// </summary>
    internal static Vector3 BaseScaleOf(Transform t)
    {
        int id = t.GetInstanceID();
        if (!IsMoving(t) || !BaseScales.TryGetValue(id, out var s))
        {
            s = t.localScale;
            BaseScales[id] = s;
        }
        return s;
    }

    /// <summary>演出前の anchoredPosition（<see cref="BaseScaleOf"/> と同じ考え方）。</summary>
    internal static Vector2 BasePosOf(RectTransform t)
    {
        int id = t.GetInstanceID();
        if (!IsMoving(t) || !BasePositions.TryGetValue(id, out var p))
        {
            p = t.anchoredPosition;
            BasePositions[id] = p;
        }
        return p;
    }

    private static bool IsMoving(Transform t) =>
        DOTween.IsTweening(t) || DOTween.IsTweening(t.gameObject) || DOTween.IsTweening(ShakeId(t));

    /// <summary>CanvasGroup を用意する（無ければ付ける）。alpha だけ使う。</summary>
    internal static CanvasGroup EnsureGroup(GameObject go)
    {
        if (!go.TryGetComponent<CanvasGroup>(out var cg)) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    /// <summary>拡縮してよい Transform（ルートの Canvas は Canvas が大きさを決めるので拡縮しない）。</summary>
    private static RectTransform ScalableRoot(GameObject panel)
    {
        if (panel.TryGetComponent<Canvas>(out var canvas) && canvas.isRootCanvas) return null;
        return panel.transform as RectTransform;
    }

    /// <summary>Play 中は Destroy、編集モード（撮影ツール）では DestroyImmediate。</summary>
    internal static void DestroySafe(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
        else UnityEngine.Object.DestroyImmediate(obj);
    }
}
