using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 介入カードのボタン1つ（アイコン＋金額＋残り回数ドット＋クールダウン）。
/// 状態の合成（押せるか・グレーか）は InterventionCardView が行い、ここは見た目だけを持つ。
/// 未配線の参照があっても例外を出さない。
/// </summary>
public class InterventionButtonWidget : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text priceLabel;
    [Tooltip("Filled(Radial360) の暗幕。fillAmount=残りクールダウン")]
    [SerializeField] private Image cooldownFill;
    [Tooltip("残り回数ドット（左から）。UsesLeft=-1 なら非表示")]
    [SerializeField] private Image[] usesDots;
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite disabledSprite;

    [SerializeField] private Color priceColor = Color.white;
    [SerializeField] private Color priceDisabledColor = new(0.86f, 0.84f, 0.8f, 1f);
    [SerializeField] private Color iconDisabledColor = new(0.55f, 0.55f, 0.55f, 0.85f);
    [SerializeField] private Color dotOnColor = new(1f, 0.82f, 0.36f, 1f);
    [SerializeField] private Color dotOffColor = new(0.16f, 0.1f, 0.08f, 0.85f);

    private int _price = -1;
    private int _usesLeft = -1;
    private bool _visualEnabled = true;
    private Tween _pulse;
    private bool _pulsing;

    public Button Button => button;

    public void SetPrice(int price)
    {
        if (_price == price) return;
        _price = price;
        if (priceLabel != null) priceLabel.text = price.ToString("N0") + "G";
    }

    public void SetUsesLeft(int usesLeft)
    {
        _usesLeft = usesLeft;
        if (usesDots == null) return;
        bool show = usesLeft >= 0;
        for (int i = 0; i < usesDots.Length; i++)
        {
            var d = usesDots[i];
            if (d == null) continue;
            d.gameObject.SetActive(show);
            d.color = i < usesLeft ? dotOnColor : dotOffColor;
        }
    }

    /// <summary>見た目の有効/グレー。</summary>
    public void SetVisualEnabled(bool on)
    {
        _visualEnabled = on;
        if (background != null)
        {
            var s = on ? normalSprite : disabledSprite;
            if (s != null) background.sprite = s;
        }
        if (icon != null) icon.color = on ? Color.white : iconDisabledColor;
        if (priceLabel != null) priceLabel.color = on ? priceColor : priceDisabledColor;
    }

    public void SetClickable(bool on)
    {
        if (button != null) button.interactable = on;
    }

    public void SetCooldown(float remaining01)
    {
        if (cooldownFill == null) return;
        float r = Mathf.Clamp01(remaining01);
        cooldownFill.fillAmount = r;
        cooldownFill.enabled = r > 0.001f;
    }

    /// <summary>実行待ちの指示を示す（アイコンをゆっくり脈動）。</summary>
    public void SetPendingPulse(bool on)
    {
        if (icon == null || on == _pulsing) return;
        _pulsing = on;
        var t = icon.rectTransform;
        _pulse?.Kill();
        _pulse = null;
        t.localScale = Vector3.one;
        if (on)
        {
            icon.color = Color.white;
            _pulse = t.DOScale(1.12f, 0.45f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true).SetLink(gameObject);
        }
    }

    public void PlayPressed()
    {
        UIFx.Pop(transform, 0.9f, 0.18f);
    }

    [Tooltip("自動発動時に光らせる白いオーバーレイ（任意）")]
    [SerializeField] private Image flashOverlay;

    /// <summary>押していないのに発動した（視聴者の赤スパ等）ことを、ボタンを光らせて知らせる。</summary>
    public void PlayAutoTrigger()
    {
        if (!isActiveAndEnabled) return;
        UIFx.Pop(transform, 1.12f, 0.3f);
        if (flashOverlay == null) return;
        flashOverlay.DOKill();
        var c = flashOverlay.color;
        c.a = 0f;
        flashOverlay.color = c;
        flashOverlay.enabled = true;
        DOTween.Sequence()
            .Append(flashOverlay.DOFade(0.75f, 0.08f))
            .Append(flashOverlay.DOFade(0f, 0.35f).SetEase(Ease.OutQuad))
            .SetLoops(2)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() => flashOverlay.enabled = false);
    }

    private void OnDisable()
    {
        _pulse?.Kill();
        _pulse = null;
        _pulsing = false;
        if (icon != null) icon.rectTransform.localScale = Vector3.one;
    }
}
