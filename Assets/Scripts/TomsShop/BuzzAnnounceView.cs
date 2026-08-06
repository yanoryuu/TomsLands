using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BuzzAnnounceView : MonoBehaviour
{
    [Header("UI要素")]
    [Tooltip("スライドアニメーションさせるパネル（バズテキストをまとめた子オブジェクト）")]
    [SerializeField] private RectTransform slidePanel;
    [SerializeField] private TextMeshProUGUI buzzTitleText;
    [SerializeField] private TextMeshProUGUI buzzDescriptionText;
    [Tooltip("slidePanelにアタッチされたCanvasGroup")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("slidePanelの背景Image")]
    [SerializeField] private Image slidePanelBackground;
    [Tooltip("バズタイプのアイコンを表示するImage")]
    [SerializeField] private Image buzzIconImage;

    [Header("演出設定")]
    [SerializeField] private float slideInDuration = 0.6f;
    [SerializeField] private float holdDuration = 1.5f;
    [SerializeField] private float slideOutDuration = 0.6f;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("スライド位置")]
    [SerializeField] private float startX = -1200f;
    [SerializeField] private float centerX = 0f;
    [SerializeField] private float endX = 1200f;

    [Header("背景スプライト")]
    [Tooltip("buzz_bg_flame.png")]
    [SerializeField] private Sprite flameBackgroundSprite;
    [Tooltip("buzz_bg_normal.png")]
    [SerializeField] private Sprite normalBuzzBackgroundSprite;
    [Tooltip("buzz_bg_big.png")]
    [SerializeField] private Sprite bigBuzzBackgroundSprite;
    [Tooltip("buzz_bg_ended.png")]
    [SerializeField] private Sprite buzzEndedBackgroundSprite;

    [Header("アイコンスプライト")]
    [Tooltip("buzz_icon_flame.png")]
    [SerializeField] private Sprite flameIconSprite;
    [Tooltip("buzz_icon_normal.png")]
    [SerializeField] private Sprite normalBuzzIconSprite;
    [Tooltip("buzz_icon_big.png")]
    [SerializeField] private Sprite bigBuzzIconSprite;
    [Tooltip("buzz_icon_ended.png")]
    [SerializeField] private Sprite buzzEndedIconSprite;

    private Sequence _currentSequence;

    private void Awake()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    public void ShowBuzzOccurred(BuzzType buzzType)
    {
        _currentSequence?.Kill();
        SetBuzzAppearance(buzzType);
        PlaySlideAnimation();
    }

    public void ShowBuzzEnded(BuzzType endedBuzzType)
    {
        _currentSequence?.Kill();

        string typeLabel = GetBuzzTypeLabel(endedBuzzType);
        if (buzzTitleText != null)
            buzzTitleText.text = $"{typeLabel} 終了";
        if (buzzDescriptionText != null)
            buzzDescriptionText.text = "バズの効果が終了しました";

        ApplySprites(buzzEndedBackgroundSprite, buzzEndedIconSprite);
        PlaySlideAnimation();
    }

    private void SetBuzzAppearance(BuzzType buzzType)
    {
        switch (buzzType)
        {
            case BuzzType.Flame:
                if (buzzTitleText != null) buzzTitleText.text = "炎上発生！";
                if (buzzDescriptionText != null) buzzDescriptionText.text = "売上が大幅に減少します…";
                ApplySprites(flameBackgroundSprite, flameIconSprite);
                break;

            case BuzzType.Normal:
                if (buzzTitleText != null) buzzTitleText.text = "バズ発生！";
                if (buzzDescriptionText != null) buzzDescriptionText.text = "注目が集まり売上がアップ！";
                ApplySprites(normalBuzzBackgroundSprite, normalBuzzIconSprite);
                break;

            case BuzzType.Big:
                if (buzzTitleText != null) buzzTitleText.text = "超バズ発生！！";
                if (buzzDescriptionText != null) buzzDescriptionText.text = "大注目！売上が大幅にアップ！";
                ApplySprites(bigBuzzBackgroundSprite, bigBuzzIconSprite);
                break;
        }
    }

    private void ApplySprites(Sprite background, Sprite icon)
    {
        if (slidePanelBackground != null)
        {
            slidePanelBackground.sprite = background;
            slidePanelBackground.color = Color.white;
        }
        if (buzzIconImage != null)
        {
            buzzIconImage.sprite = icon;
            buzzIconImage.color = Color.white;
        }
    }

    private string GetBuzzTypeLabel(BuzzType buzzType)
    {
        return buzzType switch
        {
            BuzzType.Flame => "炎上",
            BuzzType.Normal => "バズ",
            BuzzType.Big => "超バズ",
            _ => "バズ"
        };
    }

    private void PlaySlideAnimation()
    {
        if (slidePanel == null || canvasGroup == null) return;

        Vector2 pos = slidePanel.anchoredPosition;
        pos.x = startX;
        slidePanel.anchoredPosition = pos;
        canvasGroup.alpha = 1f;

        _currentSequence = DOTween.Sequence()
            .Append(slidePanel.DOAnchorPosX(centerX, slideInDuration).SetEase(Ease.OutBack))
            .AppendInterval(holdDuration)
            .Append(slidePanel.DOAnchorPosX(endX, slideOutDuration).SetEase(Ease.InCubic))
            .Join(canvasGroup.DOFade(0f, fadeDuration).SetDelay(slideOutDuration - fadeDuration))
            .OnComplete(() => _currentSequence = null);
    }

    private void OnDestroy()
    {
        _currentSequence?.Kill();
    }
}

