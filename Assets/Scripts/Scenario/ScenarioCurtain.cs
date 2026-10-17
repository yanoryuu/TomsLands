using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 会話の裏に敷く暗転幕。ランの開始会話の間、店の画面を見せないために使う。
/// 宴の会話UI（ScenarioSystem の UI Canvas, ScreenSpaceOverlay / sortingOrder 2000）のすぐ下に
/// 全画面の黒を実行時に生成するので、シーンやプレハブの配線は要らない。
/// 店側の UI はすべてこれより下の Canvas にあるため、幕の間はクリックも通さない。
/// </summary>
public class ScenarioCurtain : MonoBehaviour
{
    /// <summary>宴の会話UI（2000）より下、店のどの Canvas よりも上。</summary>
    private const int SortingOrder = 1990;

    private CanvasGroup _group;

    /// <summary>真っ黒の幕をすぐに張る。生成したフレームから描画される。</summary>
    public static ScenarioCurtain Show()
    {
        var go = new GameObject("ScenarioCurtain", typeof(RectTransform));

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        go.AddComponent<GraphicRaycaster>();

        var curtain = go.AddComponent<ScenarioCurtain>();
        curtain._group = go.AddComponent<CanvasGroup>();
        curtain._group.alpha = 1f;
        curtain._group.blocksRaycasts = true;

        var imageGo = new GameObject("Black", typeof(RectTransform));
        imageGo.transform.SetParent(go.transform, false);
        var rect = (RectTransform)imageGo.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = imageGo.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;

        return curtain;
    }

    /// <summary>幕をフェードアウトして破棄する。キャンセル・破棄済みでも幕が残らないようにする。</summary>
    public async UniTask FadeOutAndDestroyAsync(float duration, CancellationToken cancellationToken)
    {
        if (this == null) return;
        try
        {
            var tween = _group.DOFade(0f, duration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
            await UniTask.WaitWhile(() => tween.IsActive() && tween.IsPlaying(), cancellationToken: cancellationToken);
        }
        finally
        {
            if (this != null) Destroy(gameObject);
        }
    }
}
