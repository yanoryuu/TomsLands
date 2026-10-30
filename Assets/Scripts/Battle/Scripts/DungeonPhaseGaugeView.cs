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
    [Tooltip("進捗の塗り。Sliced なら anchorMax.x で幅を伸縮（端の角丸が潰れない）、Filled なら fillAmount で塗る")]
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
    private Tween _fillTween;
    // #10 ゲージの残像（共通部品）。Sliced の塗りは anchorMax.x で伸縮するので TrailGauge がそのまま使える
    private TrailGauge _trail;
    private Image _trailImage;
    private Image _leadImage;

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
            _fillTween?.Kill();
            ApplyFill(0f);
            EnsureTrail();
            if (_trail != null) _trail.SetValue(0f, animate: false);
            SyncTrailVisibility();
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
            _fillTween?.Kill();
            EnsureTrail();
            if (_trail != null)
            {
                // 進むと緑の先触れが先に伸び、塗りが追いつく（値は SetValue で。省略モードは即座）
                fillImage.enabled = true;
                _trail.SetValue(target);
                SyncTrailVisibility();
            }
            else if (fillImage.type == Image.Type.Filled)
            {
                fillImage.DOFillAmount(target, fillDuration).SetEase(Ease.OutCubic).SetLink(fillImage.gameObject);
            }
            else
            {
                float from = fillImage.rectTransform.anchorMax.x;
                _fillTween = DOTween.To(() => from, x => { from = x; ApplyFill(x); }, target, fillDuration)
                    .SetEase(Ease.OutCubic).SetLink(fillImage.gameObject);
            }
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

    private void EnsureTrail()
    {
        if (_trail != null || fillImage == null) return;
        _trail = TrailGauge.Attach(fillImage);
        if (_trail == null || fillImage.transform.parent == null) return;
        var parent = fillImage.transform.parent;
        var t = parent.Find("GaugeTrail");
        var l = parent.Find("GaugeLead");
        _trailImage = t != null ? t.GetComponent<Image>() : null;
        _leadImage = l != null ? l.GetComponent<Image>() : null;
    }

    private void LateUpdate()
    {
        if (_trail != null) SyncTrailVisibility();
    }

    /// <summary>Sliced の塗りは角丸より短い間は隠す（ApplyFill と同じ決まり）。残像・先触れも同じ。</summary>
    private void SyncTrailVisibility()
    {
        if (fillImage == null || fillImage.type == Image.Type.Filled) return;
        HideIfShort(fillImage);
        HideIfShort(_trailImage);
        HideIfShort(_leadImage);
    }

    private static void HideIfShort(Image img)
    {
        if (img == null) return;
        var rt = img.rectTransform;
        img.enabled = rt.rect.width >= rt.rect.height;
    }

    /// <summary>塗りを割合 t で即時反映する。Sliced は幅を伸縮し、角丸より短い間は非表示にする。</summary>
    private void ApplyFill(float t)
    {
        if (fillImage.type == Image.Type.Filled)
        {
            fillImage.fillAmount = t;
            return;
        }

        var rt = fillImage.rectTransform;
        rt.anchorMax = new Vector2(t, rt.anchorMax.y);
        fillImage.enabled = rt.rect.width >= rt.rect.height;
    }

    public void Hide()
    {
        if (root) root.SetActive(false);
    }
}
