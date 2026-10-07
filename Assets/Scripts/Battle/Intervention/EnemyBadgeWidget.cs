using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>カード裏の前列魔物1体（顔アイコン＋HP小バー。ボスは金縁で大きめ）。</summary>
public class EnemyBadgeWidget : MonoBehaviour
{
    [SerializeField] private Image backdrop;
    [SerializeField] private Image icon;
    [SerializeField] private Image ring;
    [SerializeField] private Image hpFill;
    [SerializeField] private Sprite ringNormal;
    [SerializeField] private Sprite ringBoss;
    [SerializeField] private Color ringNormalColor = new(0.78f, 0.62f, 0.86f, 1f);
    [SerializeField] private Color backdropNormal = new(0.2f, 0.1f, 0.22f, 1f);
    [SerializeField] private Color backdropBoss = new(0.42f, 0.08f, 0.1f, 1f);
    [SerializeField] private float bossScale = 1.22f;

    private float _hp = -1f;
    private Tween _hpTween;

    public void Bind(EnemyBadgeInfo info)
    {
        if (icon != null)
        {
            icon.sprite = info.Icon;
            icon.enabled = info.Icon != null;
            FitIcon(info.Icon);
        }
        if (ring != null)
        {
            var s = info.IsBoss ? ringBoss : ringNormal;
            if (s != null) ring.sprite = s;
            ring.color = info.IsBoss ? Color.white : ringNormalColor;
        }
        if (backdrop != null) backdrop.color = info.IsBoss ? backdropBoss : backdropNormal;
        transform.localScale = Vector3.one * (info.IsBoss ? bossScale : 1f);

        float hp = Mathf.Clamp01(info.Hp01);
        if (hpFill != null)
        {
            hpFill.color = HpColor(hp);
            if (_hp < 0f || !isActiveAndEnabled)
            {
                hpFill.fillAmount = hp;
            }
            else if (!Mathf.Approximately(_hp, hp))
            {
                _hpTween?.Kill();
                _hpTween = hpFill.DOFillAmount(hp, 0.25f).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            }
        }
        _hp = hp;
    }

    public void ResetState() => _hp = -1f;

    [Tooltip("顔枠に対する見える部分の大きさ（余白の多いドット絵でも枠いっぱいに見せる）")]
    [SerializeField] private float iconFill = 0.8f;

    /// <summary>
    /// スプライトの不透明部分（Tight メッシュの頂点）を顔枠の中央に合わせて拡大する。
    /// 戦闘用のドット絵は下寄せ・余白多めなので、そのままだと小さく見えるため。
    /// </summary>
    private void FitIcon(Sprite sp)
    {
        if (icon == null) return;
        var rt = icon.rectTransform;
        var face = rt.parent as RectTransform;
        if (sp == null || face == null) return;

        var verts = sp.vertices;
        var b = sp.bounds;
        if (verts == null || verts.Length == 0 || b.size.x <= 0f || b.size.y <= 0f) return;
        Vector2 min = verts[0], max = verts[0];
        foreach (var v in verts)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        float nx0 = (min.x - b.min.x) / b.size.x, nx1 = (max.x - b.min.x) / b.size.x;
        float ny0 = (min.y - b.min.y) / b.size.y, ny1 = (max.y - b.min.y) / b.size.y;
        float W = sp.rect.width, H = sp.rect.height;
        float visW = Mathf.Max(1f, (nx1 - nx0) * W), visH = Mathf.Max(1f, (ny1 - ny0) * H);
        float target = Mathf.Min(face.rect.width, face.rect.height) * iconFill;
        if (target <= 0f) target = 94f * iconFill;
        float k = target / Mathf.Max(visW, visH);

        icon.preserveAspect = false;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(W * k, H * k);
        rt.anchoredPosition = new Vector2(-((nx0 + nx1) * 0.5f - 0.5f) * W * k, -((ny0 + ny1) * 0.5f - 0.5f) * H * k);
    }

    private static Color HpColor(float hp)
    {
        if (hp > 0.5f) return new Color(0.45f, 0.82f, 0.36f, 1f);
        if (hp > 0.25f) return new Color(0.96f, 0.76f, 0.24f, 1f);
        return new Color(0.9f, 0.28f, 0.2f, 1f);
    }
}
