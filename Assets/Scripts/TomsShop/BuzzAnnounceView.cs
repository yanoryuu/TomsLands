using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バズ発生/終了時のキャラクターカットイン演出。
/// 四角いバナーではなく、背景透過のキャラ立ち絵（トコ）が左から飛び込み、
/// 横にタイトル/説明テキストがポップするパチンコ風カットイン。
/// </summary>
public class BuzzAnnounceView : MonoBehaviour
{
    [Header("UI要素")]
    [Tooltip("カットイン全体のコンテナ（CanvasGroup付き）")]
    [SerializeField] private RectTransform slidePanel;
    [Tooltip("slidePanelにアタッチされたCanvasGroup")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("キャラ立ち絵のImage（背景透過スプライト）")]
    [SerializeField] private Image characterImage;
    [SerializeField] private TextMeshProUGUI buzzTitleText;
    [SerializeField] private TextMeshProUGUI buzzDescriptionText;

    [Header("テキスト用マテリアル（任意）")]
    [Tooltip("超バズ時にタイトルへ適用する虹マテリアル（UI/BuzzFrame）。未設定なら色のみ")]
    [SerializeField] private Material rainbowTextMaterial;

    [Header("タイプ別カラー")]
    [SerializeField] private Color normalTitleColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color bigTitleColor = Color.white;
    [SerializeField] private Color flameTitleColor = new Color(1f, 0.3f, 0.2f);
    [SerializeField] private Color endedTitleColor = new Color(0.8f, 0.8f, 0.8f);
    [Tooltip("炎上時のキャラティント（青ざめ）")]
    [SerializeField] private Color flameCharacterTint = new Color(0.75f, 0.75f, 0.95f);
    [Tooltip("終了時のキャラティント（グレー）")]
    [SerializeField] private Color endedCharacterTint = new Color(0.65f, 0.65f, 0.65f);

    [Header("演出設定")]
    [SerializeField] private float slideInDuration = 0.45f;
    [SerializeField] private float holdDuration = 1.6f;
    [SerializeField] private float slideOutDuration = 0.45f;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("キャラ配置")]
    [Tooltip("キャラのスライド開始X（画面外左）")]
    [SerializeField] private float charStartX = -1500f;
    [Tooltip("キャラの停止X（画面中央より左）")]
    [SerializeField] private float charEndX = -430f;
    [Tooltip("キャラの退場X（画面外左へ戻る）")]
    [SerializeField] private float charExitX = -1500f;
    [Tooltip("登場時のキャラ傾き（度）")]
    [SerializeField] private float charTiltAngle = -6f;
    [Tooltip("超バズ時のキャラ拡大率")]
    [SerializeField] private float bigCharScale = 1.15f;

    private Sequence _currentSequence;
    private Material _defaultTitleMaterial;
    private float _defaultCharScale = 1f;
    private Vector3 _titleBaseScale = Vector3.one;

    private void Awake()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (buzzTitleText != null)
        {
            _defaultTitleMaterial = buzzTitleText.fontSharedMaterial;
            _titleBaseScale = buzzTitleText.rectTransform.localScale;
        }
        if (characterImage != null)
            _defaultCharScale = characterImage.rectTransform.localScale.x;
    }

    public void ShowBuzzOccurred(BuzzType buzzType)
    {
        switch (buzzType)
        {
            case BuzzType.Flame:
                Play("炎上発生！", "売上が大幅に減少します…", flameTitleColor,
                    characterTint: flameCharacterTint, useRainbow: false, charScaleFactor: 1f, flameShake: true);
                break;

            case BuzzType.Big:
                Play("超バズ発生！！", "大注目！売上が大幅にアップ！", bigTitleColor,
                    characterTint: Color.white, useRainbow: true, charScaleFactor: bigCharScale, flameShake: false);
                break;

            default:
                Play("バズ発生！", "注目が集まり売上がアップ！", normalTitleColor,
                    characterTint: Color.white, useRainbow: false, charScaleFactor: 1f, flameShake: false);
                break;
        }
    }

    public void ShowBuzzEnded(BuzzType endedBuzzType)
    {
        string typeLabel = GetBuzzTypeLabel(endedBuzzType);
        Play($"{typeLabel} 終了", "効果が終了しました", endedTitleColor,
            characterTint: endedCharacterTint, useRainbow: false, charScaleFactor: 1f, flameShake: false);
    }

    private void Play(string title, string description, Color titleColor,
        Color characterTint, bool useRainbow, float charScaleFactor, bool flameShake)
    {
        if (canvasGroup == null) return;

        _currentSequence?.Kill();
        ResetPose();

        // テキスト設定
        if (buzzTitleText != null)
        {
            buzzTitleText.text = title;
            buzzTitleText.color = titleColor;
            buzzTitleText.fontSharedMaterial = useRainbow && rainbowTextMaterial != null
                ? rainbowTextMaterial
                : _defaultTitleMaterial;
        }
        if (buzzDescriptionText != null)
            buzzDescriptionText.text = description;

        // キャラ初期状態（画面外左・傾き強め）
        RectTransform charRect = null;
        if (characterImage != null)
        {
            charRect = characterImage.rectTransform;
            characterImage.color = characterTint;
            var pos = charRect.anchoredPosition;
            pos.x = charStartX;
            charRect.anchoredPosition = pos;
            charRect.localRotation = Quaternion.Euler(0, 0, charTiltAngle * 2f);
            charRect.localScale = Vector3.one * (_defaultCharScale * charScaleFactor);
        }

        // テキスト初期状態（タイトルはポップ用に0、説明はフェード用に透明）
        if (buzzTitleText != null) buzzTitleText.rectTransform.localScale = Vector3.zero;
        if (buzzDescriptionText != null) buzzDescriptionText.alpha = 0f;

        canvasGroup.alpha = 1f;

        _currentSequence = DOTween.Sequence();

        // 1. キャラが左から飛び込む（傾きを戻しながらOutBack）
        if (charRect != null)
        {
            _currentSequence.Append(charRect.DOAnchorPosX(charEndX, slideInDuration).SetEase(Ease.OutBack));
            _currentSequence.Join(charRect
                .DOLocalRotate(new Vector3(0, 0, charTiltAngle), slideInDuration)
                .SetEase(Ease.OutCubic));
        }

        // 2. タイトルがポップ（元のスケールへ）、説明がフェードイン
        if (buzzTitleText != null)
            _currentSequence.Append(buzzTitleText.rectTransform.DOScale(_titleBaseScale, 0.3f).SetEase(Ease.OutBack));
        if (buzzDescriptionText != null)
            _currentSequence.Join(buzzDescriptionText.DOFade(1f, 0.25f));

        // 3. 表示キープ（炎上時はキャラが小刻みに震える）
        if (flameShake && charRect != null)
        {
            _currentSequence.Append(charRect
                .DOShakeAnchorPos(holdDuration, strength: 10f, vibrato: 30, randomness: 90f, snapping: false, fadeOut: false));
        }
        else
        {
            _currentSequence.AppendInterval(holdDuration);
        }

        // 超バズ: タイトルの色相を回して虹文字にする（SDFマテリアルはそのまま）
        if (useRainbow && buzzTitleText != null)
        {
            float hue = 0f;
            var rainbowTween = DOTween.To(() => hue, h =>
                {
                    hue = h;
                    buzzTitleText.color = Color.HSVToRGB(h % 1f, 0.7f, 1f);
                }, 1f, 1.2f)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Restart);
            // シーケンス終了時に止まるよう寿命を紐付ける
            _currentSequence.OnKill(() => rainbowTween.Kill());
        }

        // 4. 退場: キャラは左へ引っ込み、全体フェードアウト
        if (charRect != null)
            _currentSequence.Append(charRect.DOAnchorPosX(charExitX, slideOutDuration).SetEase(Ease.InBack));
        _currentSequence.Join(canvasGroup.DOFade(0f, fadeDuration).SetDelay(slideOutDuration - fadeDuration));

        _currentSequence.OnComplete(() =>
        {
            _currentSequence = null;
            ResetPose();
        });
    }

    /// <summary>
    /// 途中killや連続再生に備えて各要素の姿勢を初期状態へ戻す。
    /// </summary>
    private void ResetPose()
    {
        if (characterImage != null)
        {
            characterImage.rectTransform.localRotation = Quaternion.identity;
            characterImage.rectTransform.localScale = Vector3.one * _defaultCharScale;
        }
        if (buzzTitleText != null)
            buzzTitleText.rectTransform.localScale = _titleBaseScale;
        if (buzzDescriptionText != null)
            buzzDescriptionText.alpha = 1f;
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

    private void OnDestroy()
    {
        _currentSequence?.Kill();
    }
}
