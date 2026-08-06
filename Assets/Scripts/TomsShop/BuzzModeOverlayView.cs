using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バズ持続中に画面へ常時表示するオーバーレイ（バズモード演出）。
/// パチンコの「激アツ金枠」「虹枠（プレミア）」を再現する:
/// - 通常バズ = 金枠（シャインスイープが走る）
/// - 超バズ   = 虹枠（シェーダーで色相が回るホログラフィック）
/// - 炎上     = 赤黒の炎枠
/// 登場時はズームパンチ＋シェイク、バナーは役物風に上から落下する。
/// 色相サイクル・シャインは UI/BuzzFrame シェーダーのマテリアルで表現する。
/// </summary>
public class BuzzModeOverlayView : MonoBehaviour
{
    [Header("UI要素")]
    [Tooltip("オーバーレイ全体のCanvasGroup（ルートにアタッチ）")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("画面外枠のフレームImage（全面ストレッチ配置）")]
    [SerializeField] private Image frameImage;
    [Tooltip("「バズ発生中」バナーの背景Image（金属プレート）")]
    [SerializeField] private Image bannerBackground;
    [Tooltip("バナーのテキスト（バズ発生中/超バズ発生中/炎上中）")]
    [SerializeField] private TextMeshProUGUI bannerText;
    [Tooltip("残りターン数の表示テキスト（任意）")]
    [SerializeField] private TextMeshProUGUI remainingTurnsText;

    [Header("フレームスプライト")]
    [Tooltip("通常バズ用の金枠")]
    [SerializeField] private Sprite normalFrameSprite;
    [Tooltip("超バズ用の虹枠")]
    [SerializeField] private Sprite bigFrameSprite;
    [Tooltip("炎上用の炎枠")]
    [SerializeField] private Sprite flameFrameSprite;

    [Header("フレームマテリアル（UI/BuzzFrame シェーダー）")]
    [Tooltip("金枠用: シャインスイープのみ")]
    [SerializeField] private Material goldFrameMaterial;
    [Tooltip("虹枠用: 色相サイクル＋シャイン")]
    [SerializeField] private Material rainbowFrameMaterial;
    [Tooltip("炎枠用: 速い明滅寄りのシャイン")]
    [SerializeField] private Material flameFrameMaterial;

    [Header("バナー色（プレートスプライトへのティント）")]
    [SerializeField] private Color normalBannerColor = new Color(1f, 0.82f, 0.3f, 1f);
    [SerializeField] private Color flameBannerColor = new Color(1f, 0.35f, 0.25f, 1f);

    [Header("登場演出設定")]
    [SerializeField] private float fadeDuration = 0.25f;
    [Tooltip("フレームのズームパンチ: 開始スケール")]
    [SerializeField] private float frameStartScale = 1.35f;
    [SerializeField] private float frameZoomDuration = 0.45f;
    [Tooltip("バナー着地後のシェイク強さ")]
    [SerializeField] private float shakeStrength = 12f;
    [SerializeField] private float shakeDuration = 0.5f;
    [Tooltip("バナー落下: 開始オフセット（上方向）")]
    [SerializeField] private float bannerDropOffset = 260f;
    [SerializeField] private float bannerDropDuration = 0.55f;

    [Header("持続演出設定")]
    [Tooltip("フレームのパルス点滅の片道時間")]
    [SerializeField] private float pulseDuration = 0.7f;
    [SerializeField] private float pulseMinAlpha = 0.72f;

    private Sequence _showSequence;
    private Tween _fadeTween;
    private Tween _pulseTween;
    private Vector2 _bannerBasePosition;
    private bool _bannerBaseCached;

    private void Awake()
    {
        // クリックを一切ブロックしない（演出専用オーバーレイ）
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        if (frameImage != null) frameImage.raycastTarget = false;
        if (bannerBackground != null)
        {
            bannerBackground.raycastTarget = false;
            _bannerBasePosition = bannerBackground.rectTransform.anchoredPosition;
            _bannerBaseCached = true;
        }
        if (bannerText != null) bannerText.raycastTarget = false;
        if (remainingTurnsText != null) remainingTurnsText.raycastTarget = false;
    }

    /// <summary>
    /// バズモードを表示する。バズ発生中は表示しっぱなしにする。
    /// </summary>
    public void Show(BuzzType buzzType)
    {
        if (canvasGroup == null) return;

        KillTweens();
        ApplyAppearance(buzzType);

        _showSequence = DOTween.Sequence();

        // フェードイン
        canvasGroup.alpha = 0f;
        _showSequence.Append(canvasGroup.DOFade(1f, fadeDuration));

        // フレーム: ズームパンチ（外から締まる）。
        // シェイクは適用しない（動かすと画面外にあるべきフレームの外周が見えてしまうため、
        // scale >= 1 のズームのみで登場のインパクトを出す）
        if (frameImage != null)
        {
            var frameRect = frameImage.rectTransform;
            frameRect.localScale = Vector3.one * frameStartScale;
            _showSequence.Join(frameRect.DOScale(1f, frameZoomDuration).SetEase(Ease.OutBack));
        }

        // バナー: 役物風に上から落下してバウンド → 着地後に小シェイク
        if (bannerBackground != null && _bannerBaseCached)
        {
            var bannerRect = bannerBackground.rectTransform;
            bannerRect.anchoredPosition = _bannerBasePosition + Vector2.up * bannerDropOffset;
            _showSequence.Join(bannerRect
                .DOAnchorPos(_bannerBasePosition, bannerDropDuration)
                .SetEase(Ease.OutBounce)
                .SetDelay(0.1f));
            _showSequence.Append(bannerRect
                .DOShakeAnchorPos(shakeDuration, shakeStrength, vibrato: 18, randomness: 90f, snapping: false, fadeOut: true));
        }

        // 着地後に持続パルス開始
        _showSequence.OnComplete(StartPulse);
    }

    /// <summary>
    /// バズモードを非表示にする（バズ終了時）。
    /// </summary>
    public void Hide()
    {
        if (canvasGroup == null) return;

        KillTweens();
        _fadeTween = canvasGroup.DOFade(0f, fadeDuration * 2f);
    }

    /// <summary>
    /// 残りターン数の表示を更新する。
    /// </summary>
    public void UpdateRemainingTurns(int remainingTurns)
    {
        if (remainingTurnsText == null) return;
        remainingTurnsText.text = remainingTurns > 0 ? $"残り{remainingTurns}ターン" : string.Empty;
    }

    private void ApplyAppearance(BuzzType buzzType)
    {
        Sprite frameSprite;
        Material frameMaterial;
        Color bannerColor;
        Material bannerMaterial;
        string label;

        switch (buzzType)
        {
            case BuzzType.Flame:
                frameSprite = flameFrameSprite;
                frameMaterial = flameFrameMaterial;
                bannerColor = flameBannerColor;
                bannerMaterial = flameFrameMaterial;
                label = "炎上中";
                break;

            case BuzzType.Big:
                frameSprite = bigFrameSprite;
                frameMaterial = rainbowFrameMaterial;
                // 虹枠はシェーダーの色相サイクルに任せるため素の色で表示
                bannerColor = Color.white;
                bannerMaterial = rainbowFrameMaterial;
                label = "超バズ発生中";
                break;

            default:
                frameSprite = normalFrameSprite;
                frameMaterial = goldFrameMaterial;
                bannerColor = normalBannerColor;
                bannerMaterial = goldFrameMaterial;
                label = "バズ発生中";
                break;
        }

        if (frameImage != null)
        {
            frameImage.gameObject.SetActive(frameSprite != null);
            if (frameSprite != null)
            {
                frameImage.sprite = frameSprite;
                frameImage.color = Color.white;
                frameImage.material = frameMaterial;
            }
        }

        if (bannerBackground != null)
        {
            bannerBackground.color = bannerColor;
            bannerBackground.material = bannerMaterial;
        }

        if (bannerText != null)
            bannerText.text = label;
    }

    /// <summary>
    /// フレームのパルス点滅（枠の明滅）。シャイン/虹はシェーダー側で常時動く。
    /// </summary>
    private void StartPulse()
    {
        if (frameImage == null || !frameImage.gameObject.activeSelf) return;

        _pulseTween = frameImage
            .DOFade(pulseMinAlpha, pulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo)
            .OnKill(() =>
            {
                if (frameImage != null)
                {
                    Color c = frameImage.color;
                    c.a = 1f;
                    frameImage.color = c;
                }
            });
    }

    private void KillTweens()
    {
        _showSequence?.Kill();
        _fadeTween?.Kill();
        _pulseTween?.Kill();
        _showSequence = null;
        _fadeTween = null;
        _pulseTween = null;

        // 途中killされた場合に備えて姿勢をリセット
        if (frameImage != null)
            frameImage.rectTransform.localScale = Vector3.one;
        if (bannerBackground != null && _bannerBaseCached)
            bannerBackground.rectTransform.anchoredPosition = _bannerBasePosition;
        var rootRect = transform as RectTransform;
        if (rootRect != null)
            rootRect.anchoredPosition = Vector2.zero;
    }

    private void OnDestroy()
    {
        KillTweens();
    }
}
