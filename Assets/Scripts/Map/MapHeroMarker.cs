using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 地図上の勇者ドットキャラ。次の行き先のアイコンのそばに立ち、その場で足踏み（小さく跳ねる）し続ける。
/// 素材は SerializeField の Sprite で差し替えられる（コマが複数あれば跳ねるたびに切り替える）。
/// </summary>
public class MapHeroMarker : MonoBehaviour
{
    [SerializeField] private Image body;
    [SerializeField] private Image shadow;
    [SerializeField] private Sprite[] frames;

    [Header("足踏み")]
    [SerializeField] private float hopHeight = 14f;
    [SerializeField] private float hopDuration = 0.28f;
    [SerializeField] private float hopInterval = 0.12f;

    private RectTransform rect;
    private RectTransform Rect => rect != null ? rect : rect = transform as RectTransform;
    private Sequence loop;
    private int frameIndex;

    private void Awake()
    {
        rect = transform as RectTransform;
        if (body && frames != null && frames.Length > 0 && frames[0]) body.sprite = frames[0];
    }

    /// <summary>指定アイコンのそば（offset はアイコン中心からのずれ）に立たせる。</summary>
    public void Show(RectTransform anchor, Vector2 offset)
    {
        if (anchor == null || Rect == null)
        {
            Hide();
            return;
        }

        // アイコンと親が違っても同じ見た目の位置に来るよう、ワールド座標経由で合わせる
        rect.position = anchor.position;
        rect.anchoredPosition += offset;
        rect.SetAsLastSibling();

        gameObject.SetActive(true);
        UIFx.Pop(transform, 0.6f, 0.25f);
        PlayLoop();
    }

    public void Hide()
    {
        loop?.Kill();
        gameObject.SetActive(false);
    }

    private void PlayLoop()
    {
        if (body == null) return;
        var bodyRect = body.rectTransform;

        loop?.Kill();
        bodyRect.anchoredPosition = Vector2.zero;
        if (shadow) shadow.rectTransform.localScale = Vector3.one;

        loop = DOTween.Sequence()
            .AppendCallback(NextFrame)
            .Append(bodyRect.DOAnchorPosY(hopHeight, hopDuration * 0.5f).SetEase(Ease.OutQuad))
            .Append(bodyRect.DOAnchorPosY(0f, hopDuration * 0.5f).SetEase(Ease.InQuad))
            .AppendInterval(hopInterval)
            .SetLoops(-1)
            .SetLink(gameObject);

        if (shadow)
        {
            loop.Insert(0f, shadow.rectTransform.DOScale(0.75f, hopDuration * 0.5f).SetEase(Ease.OutQuad));
            loop.Insert(hopDuration * 0.5f, shadow.rectTransform.DOScale(1f, hopDuration * 0.5f).SetEase(Ease.InQuad));
        }
    }

    // コマが複数あれば順に切り替え、1枚だけなら左右反転で足踏みに見せる
    private void NextFrame()
    {
        if (body == null) return;
        if (frames != null && frames.Length > 1)
        {
            frameIndex = (frameIndex + 1) % frames.Length;
            if (frames[frameIndex]) body.sprite = frames[frameIndex];
        }
        else
        {
            var s = body.rectTransform.localScale;
            s.x = -s.x;
            body.rectTransform.localScale = s;
        }
    }

    private void OnDisable()
    {
        loop?.Kill();
    }
}
