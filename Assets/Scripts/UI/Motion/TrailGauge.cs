using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #10 ゲージの残像。
/// ・減る: 本体は 0.1 秒で新しい値へ。白い残像が 0.4 秒待ってから 0.5 秒で追いつく。ゲージ（本体の親）が少し揺れる
/// ・増える: 緑の先触れが先に 0.1 秒で伸び、本体が 0.4 秒待ってから 0.5 秒で追いつく
/// 本体の Image（Filled なら fillAmount、それ以外は Slider の Fill と同じく anchorMax.x）の後ろに残像と先触れの絵を足す。
/// 値の変更は必ず <see cref="SetValue"/> を通す（直接 fillAmount を書くと残像がずれる）。省略モードでは全部即座に新しい値。
/// </summary>
[DisallowMultipleComponent]
public sealed class TrailGauge : MonoBehaviour
{
    private static readonly Color TrailColor = new(1f, 1f, 1f, 0.9f);

    private Image fill;
    private Image trail;
    private Image lead;
    private bool useFillAmount;
    private float value;
    private Sequence running;

    /// <summary>今の値（0〜1）。</summary>
    public float Value => value;

    /// <summary>fill（ゲージ本体の Image）に残像を付ける。既に付いていればそれを返す。</summary>
    public static TrailGauge Attach(Image fill)
    {
        if (fill == null) return null;
        if (fill.TryGetComponent<TrailGauge>(out var g)) return g;
        g = fill.gameObject.AddComponent<TrailGauge>();
        g.Build(fill);
        return g;
    }

    private void Build(Image body)
    {
        fill = body;
        useFillAmount = body.type == Image.Type.Filled;
        value = Read(fill);
        trail = CreateShadowCopy("GaugeTrail", TrailColor);
        lead = CreateShadowCopy("GaugeLead", UIMotionFx.Green);
        Write(trail, value);
        Write(lead, value);
    }

    /// <summary>本体の後ろ（直前の兄弟）に同じ形の Image を作る。</summary>
    private Image CreateShadowCopy(string name, Color color)
    {
        var src = (RectTransform)fill.transform;
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = fill.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(src.parent, false);
        rt.SetSiblingIndex(src.GetSiblingIndex());
        rt.anchorMin = src.anchorMin;
        rt.anchorMax = src.anchorMax;
        rt.pivot = src.pivot;
        rt.anchoredPosition = src.anchoredPosition;
        rt.sizeDelta = src.sizeDelta;
        rt.localScale = src.localScale;
        rt.localRotation = src.localRotation;
        var img = go.AddComponent<Image>();
        img.sprite = fill.sprite;
        img.type = fill.type;
        img.fillMethod = fill.fillMethod;
        img.fillOrigin = fill.fillOrigin;
        img.fillClockwise = fill.fillClockwise;
        img.preserveAspect = fill.preserveAspect;
        img.pixelsPerUnitMultiplier = fill.pixelsPerUnitMultiplier;
        img.color = color;
        img.raycastTarget = false;
        UIMotionFx.IgnoreLayout(go);
        return img;
    }

    /// <summary>値（0〜1）を変える。animate=false なら即座に（初期化・画面を開いたとき）。</summary>
    public void SetValue(float normalized, bool animate = true)
    {
        normalized = Mathf.Clamp01(normalized);
        if (running != null)
        {
            // 途中で次の値が来た: 見えている位置から続ける（OnKill で前の目標へ飛ばさない）
            running.OnKill(null);
            running.Kill();
            running = null;
        }
        float from = Read(fill);
        value = normalized;

        if (!animate || UIMotion.IsSkip || !gameObject.activeInHierarchy || Mathf.Approximately(from, normalized))
        {
            Write(fill, normalized);
            Write(trail, normalized);
            Write(lead, normalized);
            return;
        }

        var seq = DOTween.Sequence().SetTarget(this);
        if (normalized < from)
        {
            // 減った: 本体が先に、白い残像が遅れて追う。先触れは本体と一緒に下げる
            Write(lead, normalized);
            Write(trail, Mathf.Max(Read(trail), from));
            seq.Insert(0f, Animate(fill, normalized, 0.1f, Ease.OutQuad));
            seq.Insert(0.4f, Animate(trail, normalized, 0.5f, Ease.InOutQuad));
            if (fill.transform.parent is RectTransform gauge) UIMotion.Shake(gauge, 5f, 0.18f);
        }
        else
        {
            // 増えた: 緑の先触れが先に伸び、本体が遅れて追う。残像は本体の後ろに隠れるので先に合わせる
            Write(trail, from);
            seq.Insert(0f, Animate(lead, normalized, 0.1f, Ease.OutQuad));
            seq.Insert(0.4f, Animate(fill, normalized, 0.5f, Ease.InOutQuad));
            seq.Insert(0.4f, Animate(trail, normalized, 0.5f, Ease.InOutQuad));
        }
        seq.OnKill(() =>
        {
            if (fill == null) return;
            Write(fill, value);
            Write(trail, value);
            Write(lead, value);
        });
        running = UIMotion.Run(seq, gameObject);
    }

    private Tween Animate(Image img, float to, float duration, Ease ease) =>
        DOVirtual.Float(Read(img), to, duration, v => Write(img, v)).SetEase(ease);

    private float Read(Image img)
    {
        if (img == null) return value;
        return useFillAmount ? img.fillAmount : ((RectTransform)img.transform).anchorMax.x;
    }

    private void Write(Image img, float v)
    {
        if (img == null) return;
        if (useFillAmount)
        {
            img.fillAmount = v;
            return;
        }
        var rt = (RectTransform)img.transform;
        var max = rt.anchorMax;
        max.x = Mathf.Max(rt.anchorMin.x, v);
        rt.anchorMax = max;
    }

    private void OnDestroy()
    {
        running?.Kill();
        if (trail != null) UIMotion.DestroySafe(trail.gameObject);
        if (lead != null) UIMotion.DestroySafe(lead.gameObject);
    }
}
