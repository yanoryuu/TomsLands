using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダンジョンのフェーズ進行ゲージ。
/// フェーズ数に応じた丸い点をバー上に等間隔で表示し、
/// フェーズをクリアするたびにゲージが次の点までアニメーションで進む。
/// </summary>
public class DungeonPhaseGaugeView : MonoBehaviour
{
    [Tooltip("ゲージ全体のルート（戦闘中のみ表示）")]
    [SerializeField] private GameObject root;
    [Tooltip("進捗の塗り（Image.type=Filled / Horizontal）")]
    [SerializeField] private Image fillImage;
    [Tooltip("丸い点を並べる親（バーと同じ幅の RectTransform）")]
    [SerializeField] private RectTransform dotsContainer;
    [Tooltip("丸い点の雛形（非アクティブで配置。フェーズ数ぶん複製される）")]
    [SerializeField] private GameObject dotTemplate;

    [Header("色")]
    [SerializeField] private Color dotInactiveColor = new(1f, 1f, 1f, 0.45f);
    [SerializeField] private Color dotClearedColor = new(1f, 0.85f, 0.3f, 1f);

    [Header("アニメーション")]
    [SerializeField] private float fillDuration = 0.4f;

    private readonly List<Image> _dots = new();
    private int _phaseCount;

    /// <summary>フェーズ数に合わせてゲージを初期化する（点を生成、塗りを0に）。</summary>
    public void Setup(int phaseCount)
    {
        _phaseCount = Mathf.Max(1, phaseCount);

        if (root) root.SetActive(true);

        foreach (var d in _dots)
            if (d != null) Destroy(d.gameObject);
        _dots.Clear();

        if (fillImage)
        {
            fillImage.DOKill();
            fillImage.fillAmount = 0f;
        }

        if (dotsContainer == null || dotTemplate == null) return;

        // 点 i（1始まり）を幅の i/N 位置に配置（フェーズ i クリアでそこまで塗りが進む）
        float width = dotsContainer.rect.width;
        for (int i = 1; i <= _phaseCount; i++)
        {
            var dotGo = Instantiate(dotTemplate, dotsContainer);
            dotGo.SetActive(true);
            var rt = dotGo.transform as RectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(width * i / _phaseCount, 0f);

            var img = dotGo.GetComponent<Image>();
            if (img != null)
            {
                img.color = dotInactiveColor;
                _dots.Add(img);
            }
        }
    }

    /// <summary>クリア済みフェーズ数まで塗りを進め、通過した点を点灯させる。</summary>
    public void SetProgress(int clearedPhases)
    {
        int cleared = Mathf.Clamp(clearedPhases, 0, _phaseCount);
        float target = (float)cleared / _phaseCount;

        if (fillImage)
        {
            fillImage.DOKill();
            fillImage.DOFillAmount(target, fillDuration).SetEase(Ease.OutCubic).SetLink(fillImage.gameObject);
        }

        for (int i = 0; i < _dots.Count; i++)
        {
            var dot = _dots[i];
            if (dot == null) continue;

            bool lit = i < cleared;
            if (lit && dot.color != dotClearedColor)
            {
                dot.color = dotClearedColor;
                dot.transform.DOKill();
                dot.transform.localScale = Vector3.one * 1.5f;
                dot.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetLink(dot.gameObject);
            }
            else if (!lit)
            {
                dot.color = dotInactiveColor;
            }
        }
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
    }
}
