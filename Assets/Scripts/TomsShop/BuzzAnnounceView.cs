using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ターン開始時にバズが発生した場合の演出を表示するコンポーネント。
/// TurnAnnounceView と同様に DOTween を使用した左→右スライドアニメーション。
/// 炎上・通常バズ・大バズでテキスト・色・アイコンが変化する。
/// 
/// 【使い方】
/// TomsShopView から ShowBuzzAnnounce(BuzzType) を呼び出す。
/// GameFlowManager.NextTurn() 内のマーケティング処理でバズが発生した場合に表示。
/// </summary>
public class BuzzAnnounceView : MonoBehaviour
{
    [Header("UI要素")]
    [Tooltip("スライドアニメーションさせるパネル（バズテキストをまとめた子オブジェクト）")]
    [SerializeField] private RectTransform slidePanel;
    [SerializeField] private TextMeshProUGUI buzzTitleText;
    [SerializeField] private TextMeshProUGUI buzzDescriptionText;
    [Tooltip("slidePanelにアタッチされたCanvasGroup")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("slidePanelの背景Image（バズタイプで色が変わる）")]
    [SerializeField] private Image slidePanelBackground;

    [Header("演出設定")]
    [Tooltip("左からスライドインする時間")]
    [SerializeField] private float slideInDuration = 0.6f;

    [Tooltip("中央で停止する時間")]
    [SerializeField] private float holdDuration = 1.5f;

    [Tooltip("右へスライドアウトする時間")]
    [SerializeField] private float slideOutDuration = 0.6f;

    [Tooltip("フェードアウト時間")]
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("スライド位置")]
    [SerializeField] private float startX = -1200f;
    [SerializeField] private float centerX = 0f;
    [SerializeField] private float endX = 1200f;

    [Header("バズタイプ別カラー")]
    [Tooltip("炎上時の背景色")]
    [SerializeField] private Color flameColor = new Color(0.9f, 0.15f, 0.1f, 0.85f);

    [Tooltip("通常バズ時の背景色")]
    [SerializeField] private Color normalBuzzColor = new Color(0.1f, 0.6f, 1f, 0.85f);

    [Tooltip("大バズ時の背景色")]
    [SerializeField] private Color bigBuzzColor = new Color(1f, 0.75f, 0f, 0.85f);

    [Header("バズ終了時カラー")]
    [Tooltip("バズ終了時の背景色")]
    [SerializeField] private Color buzzEndedColor = new Color(0.5f, 0.5f, 0.5f, 0.85f);

    private Sequence _currentSequence;

    private void Awake()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// バズ発生演出を再生する。
    /// バズ発生時にTomsShopPresenter から呼び出される。
    /// </summary>
    /// <param name="buzzType">発生したバズの種類</param>
    public void ShowBuzzOccurred(BuzzType buzzType)
    {
        _currentSequence?.Kill();

        // バズタイプに応じたテキストと色を設定
        SetBuzzAppearance(buzzType);

        PlaySlideAnimation();
    }

    /// <summary>
    /// バズ終了演出を再生する。
    /// </summary>
    /// <param name="endedBuzzType">終了したバズの種類</param>
    public void ShowBuzzEnded(BuzzType endedBuzzType)
    {
        _currentSequence?.Kill();

        string typeLabel = GetBuzzTypeLabel(endedBuzzType);
        if (buzzTitleText != null)
            buzzTitleText.text = $"{typeLabel} 終了";

        if (buzzDescriptionText != null)
            buzzDescriptionText.text = "バズの効果が終了しました";

        if (slidePanelBackground != null)
            slidePanelBackground.color = buzzEndedColor;

        PlaySlideAnimation();
    }

    /// <summary>
    /// バズタイプに応じたテキスト・色を設定する
    /// </summary>
    private void SetBuzzAppearance(BuzzType buzzType)
    {
        switch (buzzType)
        {
            case BuzzType.Flame:
                if (buzzTitleText != null)
                    buzzTitleText.text = "🔥 炎上発生！";
                if (buzzDescriptionText != null)
                    buzzDescriptionText.text = "売上が大幅に減少します…";
                if (slidePanelBackground != null)
                    slidePanelBackground.color = flameColor;
                break;

            case BuzzType.Normal:
                if (buzzTitleText != null)
                    buzzTitleText.text = "📢 バズ発生！";
                if (buzzDescriptionText != null)
                    buzzDescriptionText.text = "注目が集まり売上がアップ！";
                if (slidePanelBackground != null)
                    slidePanelBackground.color = normalBuzzColor;
                break;

            case BuzzType.Big:
                if (buzzTitleText != null)
                    buzzTitleText.text = "🚀 大バズ発生！！";
                if (buzzDescriptionText != null)
                    buzzDescriptionText.text = "大注目！売上が大幅にアップ！";
                if (slidePanelBackground != null)
                    slidePanelBackground.color = bigBuzzColor;
                break;
        }
    }

    /// <summary>
    /// バズタイプのラベル文字列を取得する
    /// </summary>
    private string GetBuzzTypeLabel(BuzzType buzzType)
    {
        return buzzType switch
        {
            BuzzType.Flame => "🔥 炎上",
            BuzzType.Normal => "📢 バズ",
            BuzzType.Big => "🚀 大バズ",
            _ => "バズ"
        };
    }

    /// <summary>
    /// スライドアニメーションを再生する（左→中央→右）
    /// slidePanelのみを動かすので、他のUIに影響しない。
    /// </summary>
    private void PlaySlideAnimation()
    {
        if (slidePanel == null || canvasGroup == null) return;

        Vector2 pos = slidePanel.anchoredPosition;
        pos.x = startX;
        slidePanel.anchoredPosition = pos;
        canvasGroup.alpha = 1f;

        _currentSequence = DOTween.Sequence()
            // 左 → 中央へスライドイン
            .Append(slidePanel.DOAnchorPosX(centerX, slideInDuration).SetEase(Ease.OutBack))
            // 中央で停止（バズ情報を読む時間）
            .AppendInterval(holdDuration)
            // 中央 → 右へスライドアウト
            .Append(slidePanel.DOAnchorPosX(endX, slideOutDuration).SetEase(Ease.InCubic))
            // スライドアウト後半でフェードアウト
            .Join(canvasGroup.DOFade(0f, fadeDuration).SetDelay(slideOutDuration - fadeDuration))
            .OnComplete(() => _currentSequence = null);
    }

    private void OnDestroy()
    {
        _currentSequence?.Kill();
    }
}

