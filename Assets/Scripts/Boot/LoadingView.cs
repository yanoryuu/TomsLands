using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ローディング画面。SPUM の勇者が歩く演出・ステータス表示・フェードイン/アウトを提供する。
/// 実体は Resources/LoadingOverlay.prefab（SceneLoader が初回利用時に生成し、DontDestroyOnLoad で全シーンに持ち越す）。
/// 各参照は任意（未設定でも進行を止めない）。演出はすべて unscaled time（timeScale=0 中でも動く）。
/// </summary>
public sealed class LoadingView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("旧ローディング画面の回転スピナー（任意。勇者演出があるなら不要）")]
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("スピナー回転速度（度/秒）")]
    [SerializeField] private float spinnerSpeed = 180f;
    [SerializeField] private float fadeOutDuration = 0.3f;
    [SerializeField] private float fadeInDuration = 0.25f;

    [Header("勇者演出")]
    [Tooltip("SPUM の勇者を歩かせる撮影台（任意）")]
    [SerializeField] private LoadingHeroStage heroStage;
    [Tooltip("勇者の RawImage（表示時に軽く弾ませる。任意）")]
    [SerializeField] private RectTransform heroRect;
    [Tooltip("歩きに合わせて上下に揺らす幅（px）。0 で揺らさない")]
    [SerializeField] private float heroBobHeight = 6f;
    [Tooltip("上下に揺らす周期（秒）")]
    [SerializeField] private float heroBobPeriod = 0.5f;

    private Vector2 heroBasePos;
    private bool heroBaseCached;

    // #6 幕（Docs/UI_Motion.md）。プレハブは変えずに実行時に Canvas の中へ作る。
    // 幕を使うときは黒の Background を隠し、勇者と文言は閉じた幕の上（右下）に出す。
    private SceneCurtain curtain;
    private Graphic background;
    private CanvasGroup[] contentGroups;
    private bool curtainResolved;

    /// <summary>表示中（フェード中を含む）か。</summary>
    public bool IsVisible { get; private set; }

    private void Update()
    {
        if (spinner != null)
            spinner.Rotate(0f, 0f, -spinnerSpeed * Time.unscaledDeltaTime);

        if (heroRect != null && heroBaseCached && heroBobHeight > 0f && heroBobPeriod > 0f)
        {
            // 足音に合わせた小さな上下動（|sin| で地面から跳ねるように）
            float t = Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI / heroBobPeriod));
            heroRect.anchoredPosition = heroBasePos + new Vector2(0f, t * heroBobHeight);
        }
    }

    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    /// <summary>フェード無しで即座に全面表示する（起動直後など、裏の画面を一瞬も見せたくないとき）。</summary>
    public void ShowImmediate()
    {
        BeginShow();
        ResetCurtain();
        if (canvasGroup != null)
        {
            canvasGroup.DOKill();
            canvasGroup.alpha = 1f;
        }
    }

    /// <summary>フェードインして完了を待つ。CanvasGroup未設定なら即完了。</summary>
    public async UniTask FadeInAsync(CancellationToken ct)
    {
        BeginShow();
        if (canvasGroup == null) return;

        canvasGroup.DOKill();
        if (UIMotion.IsSkip)
        {
            // 省略モード（オートプレイ等）: 演出なしで即暗転（最終状態は通常と同じ）
            ResetCurtain();
            canvasGroup.alpha = 1f;
            return;
        }
        if (SceneCurtain.Enabled && EnsureCurtain())
        {
            // 幕: 背景の黒は出さず、幕が閉じきってから勇者と文言を出す
            SetBackgroundVisible(false);
            SetContentAlpha(0f);
            canvasGroup.alpha = 1f;
            await curtain.CloseAsync(ct);
            FadeContent(1f, 0.15f);
            return;
        }

        ResetCurtain();
        canvasGroup.alpha = 0f;
        var tween = canvasGroup.DOFade(1f, fadeInDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(gameObject);
        await WaitTweenAsync(tween, ct);
    }

    /// <summary>フェードアウトして完了を待つ。CanvasGroup未設定なら即完了。</summary>
    public async UniTask FadeOutAsync(CancellationToken ct)
    {
        if (canvasGroup == null) return;

        canvasGroup.DOKill();
        if (UIMotion.IsSkip)
        {
            ResetCurtain();
            canvasGroup.alpha = 0f;
            return;
        }
        if (curtain != null && curtain.IsClosed)
        {
            FadeContent(0f, 0.12f);
            await curtain.OpenAsync(ct);
            canvasGroup.alpha = 0f;
            return;
        }

        var tween = canvasGroup.DOFade(0f, fadeOutDuration)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .SetLink(gameObject);
        await WaitTweenAsync(tween, ct);
    }

    /// <summary>非表示にして勇者の撮影も止める（オブジェクトごと無効化して描画コストをゼロにする）。</summary>
    public void Hide()
    {
        IsVisible = false;
        if (canvasGroup != null)
        {
            canvasGroup.DOKill();
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
        if (heroStage != null) heroStage.Stop();
        ResetCurtain();
        gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- #6 幕

    /// <summary>幕を作る（初回だけ）。Canvas が見つからなければ false（従来のフェードで続ける）。</summary>
    private bool EnsureCurtain()
    {
        if (curtain != null) return true;
        if (curtainResolved) return false;
        curtainResolved = true;

        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas == null) return false;
        var bgTransform = canvas.transform.Find("Background");
        background = bgTransform != null ? bgTransform.GetComponent<Graphic>() : null;
        int index = bgTransform != null ? bgTransform.GetSiblingIndex() + 1 : 0;
        curtain = SceneCurtain.Create((RectTransform)canvas.transform, index);

        var groups = new System.Collections.Generic.List<CanvasGroup>();
        if (statusText != null) groups.Add(ContentGroup(statusText.gameObject));
        if (heroRect != null) groups.Add(ContentGroup(heroRect.gameObject));
        if (spinner != null) groups.Add(ContentGroup(spinner.gameObject));
        contentGroups = groups.ToArray();
        return true;
    }

    private static CanvasGroup ContentGroup(GameObject go)
    {
        if (!go.TryGetComponent<CanvasGroup>(out var cg)) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    /// <summary>幕を開いた状態に戻し、黒の背景と勇者・文言を元どおり見せる。</summary>
    private void ResetCurtain()
    {
        if (curtain != null) curtain.SetClosedImmediate(false);
        SetBackgroundVisible(true);
        SetContentAlpha(1f);
    }

    private void SetBackgroundVisible(bool visible)
    {
        if (background != null) background.enabled = visible;
    }

    private void SetContentAlpha(float alpha)
    {
        if (contentGroups == null) return;
        foreach (var g in contentGroups)
        {
            if (g == null) continue;
            g.DOKill();
            g.alpha = alpha;
        }
    }

    private void FadeContent(float to, float duration)
    {
        if (contentGroups == null) return;
        foreach (var g in contentGroups)
        {
            if (g == null) continue;
            g.DOKill();
            UIMotion.Run(g.DOFade(to, duration), gameObject, unscaled: true);
        }
    }

    /// <summary>Tween の完了（または Kill）をフレーム単位で待つ。timeScale に左右されない。</summary>
    private static UniTask WaitTweenAsync(Tween tween, CancellationToken ct) =>
        UniTask.WaitWhile(() => tween.IsActive() && tween.IsPlaying(), cancellationToken: ct);

    private void BeginShow()
    {
        IsVisible = true;
        gameObject.SetActive(true);
        if (canvasGroup != null) canvasGroup.blocksRaycasts = true; // 遷移中は裏の画面のクリックを通さない
        if (heroStage != null) heroStage.Play();
        if (spinner != null) spinner.gameObject.SetActive(heroStage == null);

        if (heroRect != null)
        {
            if (!heroBaseCached)
            {
                heroBasePos = heroRect.anchoredPosition;
                heroBaseCached = true;
            }
            heroRect.DOKill();
            heroRect.localScale = Vector3.one * 0.85f;
            heroRect.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(heroRect.gameObject);
        }
    }

    private void OnDestroy()
    {
        if (canvasGroup != null) canvasGroup.DOKill();
        if (heroRect != null) heroRect.DOKill();
        curtain?.Kill();
    }
}
