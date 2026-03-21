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
        currentSequence?.Kill();

        announceText.text = $"— ターン {turn} —";

        // 初期位置を開始位置に設定
        Vector2 pos = rectTransform.anchoredPosition;
        pos.x = startX;
        rectTransform.anchoredPosition = pos;
        canvasGroup.alpha = 1f;

        currentSequence = DOTween.Sequence()
            // 左 → 中央へスライド
            .Append(rectTransform.DOAnchorPosX(centerX, slideDuration).SetEase(Ease.OutCubic))
            // 中央で停止
            .AppendInterval(holdDuration)
            // 中央 → 右へスライド（後半フェードアウト）
            .Append(rectTransform.DOAnchorPosX(endX, slideDuration).SetEase(Ease.InCubic))
            .Join(canvasGroup.DOFade(0f, fadeDuration).SetDelay(slideDuration - fadeDuration))
            .OnComplete(() => currentSequence = null);
    }

    private void OnDestroy()
    {
        currentSequence?.Kill();
    }
}
