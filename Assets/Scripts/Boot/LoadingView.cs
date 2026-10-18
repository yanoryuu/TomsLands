using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

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
        gameObject.SetActive(false);
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
    }
}
