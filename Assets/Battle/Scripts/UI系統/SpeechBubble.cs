using UnityEngine;
using TMPro;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

[RequireComponent(typeof(CanvasGroup))]
public class SpeechBubble : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    private CanvasGroup canvasGroup;
    private CancellationTokenSource cts;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        cts = new CancellationTokenSource();
        canvasGroup.alpha = 0f;
    }

    public void Show(string message, float duration)
    {
        // 既に実行中のアニメーションがあればキャンセルして、新しいものを開始
        cts.Cancel();
        cts.Dispose();
        cts = new CancellationTokenSource();
        ShowSequenceAsync(message, duration, cts.Token).Forget();
    }

    private async UniTaskVoid ShowSequenceAsync(string message, float duration, CancellationToken token)
    {
        messageText.text = message;
        try
        {
            await Fade(1f, 0.2f, token); // 0.2秒かけて表示
            await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: token);
            await Fade(0f, 0.2f, token); // 0.2秒かけて非表示
        }
        catch (OperationCanceledException)
        {
            // キャンセルされた場合は即座に透明にする
            canvasGroup.alpha = 0f;
        }
    }

    private async UniTask Fade(float targetAlpha, float fadeDuration, CancellationToken token)
    {
        float startAlpha = canvasGroup.alpha;
        float time = 0;
        while (time < fadeDuration)
        {
            canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / fadeDuration);
            time += Time.deltaTime;
            await UniTask.Yield(token);
        }
        canvasGroup.alpha = targetAlpha;
    }
}