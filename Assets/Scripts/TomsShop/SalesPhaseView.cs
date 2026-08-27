using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 営業フェーズの簡易「自動進行」演出。
/// 「営業開始」押下時に画面を黒フェードで暗転させ、暗転しきったところで
/// コールバック（＝TurnEndSummary表示）を呼び、その後ゆっくり明転する。
/// 演出ターゲット未設定でも進行を止めない（短いディレイ後にコールバック）。
/// </summary>
public class SalesPhaseView : MonoBehaviour
{
    [Tooltip("フルスクリーンの黒オーバーレイ（暗転用）。Canvas の最前面に置く")]
    [SerializeField] private CanvasGroup overlay;
    [SerializeField] private RectTransform coinIcon; // 弾むコイン（任意）
    [Header("暗転タイミング")]
    [SerializeField] private float fadeInDuration = 0.5f;   // 暗転にかける時間
    [SerializeField] private float holdDuration = 0.3f;     // 真っ暗のまま保持する時間
    [SerializeField] private float fadeOutDuration = 0.4f;  // サマリー表示後の明転時間

    private Sequence _seq;

    public void PlayAndThen(Action onComplete)
    {
        _seq?.Kill();

        // ターゲット未設定でも進行は止めない
        if (overlay == null)
        {
            _seq = DOTween.Sequence()
                .AppendInterval(0.1f)
                .OnComplete(() => onComplete?.Invoke());
            return;
        }

        overlay.gameObject.SetActive(true);
        overlay.alpha = 0f;
        overlay.blocksRaycasts = true; // 演出中の誤クリック防止

        // 演出速度設定（速い=各時間を半分に）
        float scale = GameSettings.EffectDurationScale;

        _seq = DOTween.Sequence();

        // 黒フェードで暗転
        _seq.Append(overlay.DOFade(1f, fadeInDuration * scale).SetEase(Ease.InQuad));

        if (coinIcon != null)
        {
            coinIcon.localScale = Vector3.zero;
            _seq.Append(coinIcon.DOScale(1f, 0.3f * scale).SetEase(Ease.OutBack));
        }

        _seq.AppendInterval(holdDuration * scale);

        _seq.OnComplete(() =>
        {
            // 真っ暗の裏でサマリーを表示してから、ゆっくり明転して見せる
            onComplete?.Invoke();
            overlay.DOFade(0f, fadeOutDuration * scale)
                .SetEase(Ease.OutQuad)
                .SetLink(overlay.gameObject)
                .OnComplete(() =>
                {
                    overlay.blocksRaycasts = false;
                    overlay.gameObject.SetActive(false);
                });
        });
    }

    private void OnDestroy() => _seq?.Kill();
}
