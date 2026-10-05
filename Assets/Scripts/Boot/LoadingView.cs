using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// ブートシーンのローディング画面。スピナー回転・ステータス表示・フェードアウトを提供する。
/// 各参照は任意（未設定でも進行を止めない）。
/// </summary>
public sealed class LoadingView : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform spinner;
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("スピナー回転速度（度/秒）")]
    [SerializeField] private float spinnerSpeed = 180f;
    [SerializeField] private float fadeOutDuration = 0.3f;

    private void Update()
    {
        if (spinner != null)
            spinner.Rotate(0f, 0f, -spinnerSpeed * Time.unscaledDeltaTime);
    }

    public void SetStatus(string text)
    {
        if (statusText != null) statusText.text = text;
    }

    /// <summary>フェードアウトして完了を待つ。CanvasGroup未設定なら即完了。</summary>
    public async UniTask FadeOutAsync(CancellationToken ct)
    {
        if (canvasGroup == null) return;

        canvasGroup.DOKill();
        canvasGroup.DOFade(0f, fadeOutDuration).SetUpdate(true);
        await UniTask.Delay(TimeSpan.FromSeconds(fadeOutDuration), ignoreTimeScale: true, cancellationToken: ct);
    }

    private void OnDestroy()
    {
        if (canvasGroup != null) canvasGroup.DOKill();
    }
}
