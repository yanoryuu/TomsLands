using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// ターン切り替え時に画面を左から右へ横切るターン数演出を表示するコンポーネント。
/// DOTween を使用して実装。
/// </summary>
public class TurnAnnounceView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI announceText;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("演出設定")]
    [SerializeField] private float slideDuration = 1.0f;
    [SerializeField] private float holdDuration = 0.5f;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("スライド位置")]
    [SerializeField] private float startX = -960f;
    [SerializeField] private float centerX = 0f;
    [SerializeField] private float endX = 960f;

    private RectTransform rectTransform;
    private Sequence currentSequence;

    private void Awake()
    {
        rectTransform = announceText.GetComponent<RectTransform>();
        canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// ターン表示演出を再生する
    /// </summary>
    public void Show(int turn)
    {
        announceText.text = $"— ターン {turn} —";
        announceText.color = Color.white;
        PlaySlideAnimation();
    }

    private void PlaySlideAnimation()
    {
        currentSequence?.Kill();

        Vector2 pos = rectTransform.anchoredPosition;
        pos.x = startX;
        rectTransform.anchoredPosition = pos;
        canvasGroup.alpha = 1f;

        currentSequence = DOTween.Sequence()
            .Append(rectTransform.DOAnchorPosX(centerX, slideDuration).SetEase(Ease.OutCubic))
            .AppendInterval(holdDuration)
            .Append(rectTransform.DOAnchorPosX(endX, slideDuration).SetEase(Ease.InCubic))
            .Join(canvasGroup.DOFade(0f, fadeDuration).SetDelay(slideDuration - fadeDuration))
            .OnComplete(() => currentSequence = null);

        // 演出速度設定（速い=2倍速で再生）
        currentSequence.timeScale = 1f / Mathf.Max(0.1f, GameSettings.EffectDurationScale);
    }

    private void OnDestroy()
    {
        currentSequence?.Kill();
    }
}
