using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 同時接続数の表示（アイコン＋数字）。数字はカウントアップ、増えた瞬間に小さく跳ねる。
/// 配信中の赤丸（LIVE ドット）はゆっくり明滅させる。
/// </summary>
public class StreamingViewerCountView : MonoBehaviour
{
    [SerializeField] private TMP_Text countText;
    [Tooltip("配信中を示す赤丸（任意）")]
    [SerializeField] private Graphic liveDot;
    [SerializeField] private float countTweenSeconds = 0.6f;

    private int _shown;
    private Tween _countTween;
    private Tween _dotTween;

    private void OnEnable()
    {
        if (liveDot != null)
        {
            _dotTween?.Kill();
            var c = liveDot.color;
            c.a = 1f;
            liveDot.color = c;
            _dotTween = liveDot.DOFade(0.35f, 0.8f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetLink(gameObject);
        }
    }

    private void OnDisable()
    {
        _dotTween?.Kill();
        _countTween?.Kill();
    }

    public void SetImmediate(int viewers)
    {
        _countTween?.Kill();
        _shown = viewers;
        Render(viewers);
    }

    public void SetViewers(int viewers)
    {
        if (viewers == _shown) return;
        bool up = viewers > _shown;
        _countTween?.Kill();
        int from = _shown;
        _countTween = DOTween.To(() => from, v => { from = v; _shown = v; Render(v); }, viewers, countTweenSeconds)
            .SetEase(Ease.OutCubic)
            .SetLink(gameObject);
        // 大きく増えたときだけ跳ねる（常時跳ねるとうるさい）
        if (up && countText != null && viewers - _shown >= Mathf.Max(5, _shown / 20))
            UIFx.Pop(countText.transform, 1.12f, 0.2f);
    }

    private void Render(int v)
    {
        if (countText != null) countText.text = v.ToString("N0");
    }
}
