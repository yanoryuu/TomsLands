using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 営業フェーズの簡易「自動進行」演出。
/// 「営業開始」押下時に短い演出（オーバーレイのフェード＋コインのポップ）を再生し、
/// 完了後にコールバック（＝TurnEndSummary表示）を呼ぶ。
/// 演出ターゲット未設定でも進行を止めない（短いディレイ後にコールバック）。
/// </summary>
public class SalesPhaseView : MonoBehaviour
{
    [SerializeField] private CanvasGroup overlay;   // 演出オーバーレイ（任意）
    [SerializeField] private RectTransform coinIcon; // 弾むコイン（任意）
    [SerializeField] private float duration = 0.8f;

    private Sequence _seq;

    public void PlayAndThen(Action onComplete)
    {
        _seq?.Kill();

        // ターゲット未設定でも進行は止めない
        if (overlay == null && coinIcon == null)
        {
            _seq = DOTween.Sequence()
                .AppendInterval(0.1f)
                .OnComplete(() => onComplete?.Invoke());
            return;
        }

        _seq = DOTween.Sequence();

        if (overlay != null)
        {
            overlay.gameObject.SetActive(true);
            overlay.alpha = 0f;
            _seq.Append(overlay.DOFade(1f, 0.2f));
        }

        if (coinIcon != null)
        {
            coinIcon.localScale = Vector3.zero;
            _seq.Append(coinIcon.DOScale(1f, 0.3f).SetEase(Ease.OutBack));
            _seq.AppendInterval(Mathf.Max(0f, duration - 0.3f));
        }
        else
        {
            _seq.AppendInterval(duration);
        }

        _seq.OnComplete(() =>
        {
            if (overlay != null) overlay.gameObject.SetActive(false);
            onComplete?.Invoke();
        });
    }

    private void OnDestroy() => _seq?.Kill();
}
