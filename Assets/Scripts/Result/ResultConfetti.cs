using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIの紙吹雪（ランクS演出）。
/// 自分の RectTransform（全画面を想定）の上端から小片を降らせる。パーティクル不要・Overlay Canvas でも描画される。
/// 小片は初回に生成して使い回す。
/// </summary>
public class ResultConfetti : MonoBehaviour
{
    [SerializeField] private int pieceCount = 70;
    [SerializeField] private Vector2 pieceSize = new(18f, 10f);
    [SerializeField] private float minDuration = 2.4f;
    [SerializeField] private float maxDuration = 3.8f;
    [Tooltip("全体の発生をずらす幅（秒）")]
    [SerializeField] private float spawnSpread = 0.9f;
    [Tooltip("小片の色（既存UIの配色: 金・赤・紙・緑・橙）")]
    [SerializeField] private Color[] colors =
    {
        new(0.910f, 0.639f, 0.239f), // #E8A33D 金
        new(0.659f, 0.200f, 0.169f), // #A8332B 赤
        new(0.961f, 0.914f, 0.835f), // #F5E9D5 紙
        new(0.420f, 0.690f, 0.302f), // 緑
        new(0.960f, 0.780f, 0.300f), // 明るい金
    };

    private RectTransform[] _pieces;

    /// <summary>紙吹雪を一度降らせる。</summary>
    public void Play()
    {
        var area = transform as RectTransform;
        if (area == null) return;
        gameObject.SetActive(true);
        EnsurePieces();

        Rect r = area.rect;
        for (int i = 0; i < _pieces.Length; i++)
        {
            var p = _pieces[i];
            p.DOKill();
            var img = p.GetComponent<Image>();
            img.DOKill();

            float x = Random.Range(r.xMin, r.xMax);
            float startY = r.yMax + Random.Range(20f, 160f);
            float endY = r.yMin - 60f;
            float dur = Random.Range(minDuration, maxDuration);
            float delay = Random.Range(0f, spawnSpread);
            float drift = Random.Range(-160f, 160f);

            p.gameObject.SetActive(true);
            p.anchoredPosition = new Vector2(x, startY);
            p.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float scale = Random.Range(0.7f, 1.3f);
            p.localScale = new Vector3(scale, scale, 1f);
            var c = colors.Length > 0 ? colors[Random.Range(0, colors.Length)] : Color.white;
            img.color = c;

            p.DOAnchorPos(new Vector2(x + drift, endY), dur).SetDelay(delay).SetEase(Ease.InSine).SetLink(p.gameObject)
                .OnComplete(() => p.gameObject.SetActive(false));
            // ひらひら: Z回転 + Xスケールの反転で裏返る感じ
            p.DOLocalRotate(new Vector3(0f, 0f, Random.Range(360f, 900f) * (Random.value < 0.5f ? 1 : -1)), dur, RotateMode.FastBeyond360)
                .SetDelay(delay).SetEase(Ease.Linear).SetLink(p.gameObject);
            p.DOScaleX(-scale, Random.Range(0.25f, 0.5f)).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine)
                .SetDelay(delay).SetLink(p.gameObject);
        }
    }

    /// <summary>降っている紙吹雪を即座に消す。</summary>
    public void Stop()
    {
        if (_pieces == null) return;
        foreach (var p in _pieces)
        {
            if (p == null) continue;
            p.DOKill();
            p.gameObject.SetActive(false);
        }
    }

    private void EnsurePieces()
    {
        if (_pieces != null && _pieces.Length == pieceCount) return;
        _pieces = new RectTransform[pieceCount];
        for (int i = 0; i < pieceCount; i++)
        {
            var go = new GameObject("Confetti", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Random.value < 0.3f ? new Vector2(pieceSize.y, pieceSize.y) : pieceSize;
            go.GetComponent<Image>().raycastTarget = false;
            go.SetActive(false);
            _pieces[i] = rt;
        }
    }
}
