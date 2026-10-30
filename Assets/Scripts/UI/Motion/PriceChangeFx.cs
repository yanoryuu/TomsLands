using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #12 値段の変化。
/// ・上がる: 数字が緑に光って膨らみ（1.3 倍）、緑の▲が跳ねる
/// ・下がる: 数字が赤く光って沈み、よろける（左右に傾く）。赤い▼が落ちてくる
/// 矢印は数字の右に足す子（PriceArrow）。1.2 秒見せて消える。background を渡すとその色も一瞬光らせる。
/// 文字の色・大きさ・位置・回転は必ず元に戻す。省略モードでは何もしない（数字の差し替えは呼び出し側）。
/// </summary>
public static class PriceChangeFx
{
    private static readonly Color UpFlash = new Color32(0xc9, 0xf2, 0xc0, 0xff);
    private static readonly Color DownFlash = new Color32(0xff, 0xd0, 0xc4, 0xff);
    private static readonly Dictionary<int, Color> BaseColors = new();

    /// <summary>direction: +1 上がった / -1 下がった（0 は何もしない）。</summary>
    public static void Play(TMP_Text price, int direction, Graphic background = null)
    {
        if (price == null || direction == 0 || UIMotion.IsSkip || !price.gameObject.activeInHierarchy) return;
        bool up = direction > 0;
        var rt = price.rectTransform;
        int id = price.GetInstanceID();
        bool moving = DOTween.IsTweening(price.gameObject);
        if (!moving || !BaseColors.ContainsKey(id)) BaseColors[id] = price.color;
        Color baseColor = BaseColors[id];
        Vector3 baseScale = UIMotion.BaseScaleOf(rt);
        Vector2 basePos = UIMotion.BasePosOf(rt);
        DOTween.Kill(price.gameObject);

        var seq = DOTween.Sequence().SetId(price.gameObject);
        Color flash = up ? UIMotionFx.Green : UIMotionFx.Red;
        price.color = flash;
        seq.Insert(0.3f, price.DOColor(baseColor, 0.5f));
        if (up)
        {
            seq.Insert(0f, rt.DOScale(baseScale * 1.3f, 0.1f).SetEase(Ease.OutQuad));
            seq.Insert(0.1f, rt.DOScale(baseScale, 0.22f).SetEase(Ease.OutBack, 2f));
        }
        else
        {
            seq.Insert(0f, rt.DOAnchorPosY(basePos.y - 5f, 0.12f).SetEase(Ease.OutQuad));
            seq.Insert(0f, rt.DOScale(baseScale * 0.9f, 0.12f));
            seq.Insert(0.12f, rt.DOAnchorPosY(basePos.y, 0.15f).SetEase(Ease.InOutSine));
            seq.Insert(0.12f, rt.DOScale(baseScale, 0.15f));
            seq.Insert(0.12f, rt.DOLocalRotate(new Vector3(0f, 0f, -4f), 0.1f));
            seq.Insert(0.22f, rt.DOLocalRotate(new Vector3(0f, 0f, 3f), 0.1f));
            seq.Insert(0.32f, rt.DOLocalRotate(Vector3.zero, 0.1f));
        }

        Color bg = background != null ? background.color : Color.white;
        if (background != null)
        {
            background.color = Color.Lerp(bg, up ? UpFlash : DownFlash, 0.85f);
            seq.Insert(0.15f, background.DOColor(bg, 0.6f));
        }

        seq.OnKill(() =>
        {
            if (background != null) background.color = bg;
            if (price == null) return;
            price.color = baseColor;
            rt.localScale = baseScale;
            rt.anchoredPosition = basePos;
            rt.localRotation = Quaternion.identity;
        });
        UIMotion.Run(seq, price.gameObject);
        PlayArrow(price, up);
    }

    private static void PlayArrow(TMP_Text price, bool up)
    {
        var rt = price.rectTransform;
        var arrowRt = rt.Find("PriceArrow") as RectTransform;
        Image arrow;
        if (arrowRt == null)
        {
            float size = Mathf.Clamp(price.fontSize * 0.6f, 14f, 40f);
            arrow = UIMotionFx.NewImage("PriceArrow", rt, UIMotionFx.TriangleSprite, Color.white, new Vector2(size, size));
            arrowRt = arrow.rectTransform;
            UIMotionFx.IgnoreLayout(arrow.gameObject);
        }
        else
        {
            arrow = arrowRt.GetComponent<Image>();
        }
        // 文字の右端（中身の幅で）に置く
        float textRight = Mathf.Min(price.textBounds.max.x, rt.rect.xMax);
        if (float.IsNaN(textRight) || float.IsInfinity(textRight) || price.textBounds.size.x <= 0f) textRight = rt.rect.xMax;
        arrowRt.anchorMin = arrowRt.anchorMax = new Vector2(0.5f, 0.5f);
        float x = textRight + arrowRt.sizeDelta.x * 0.7f - rt.rect.center.x;
        arrowRt.localRotation = Quaternion.Euler(0f, 0f, up ? 0f : 180f);
        arrow.color = up ? UIMotionFx.Green : UIMotionFx.Red;
        DOTween.Kill(arrowRt.gameObject);

        var seq = DOTween.Sequence().SetId(arrowRt.gameObject);
        arrowRt.gameObject.SetActive(true);
        var c = arrow.color;
        c.a = 0f;
        arrow.color = c;
        seq.Insert(0f, arrow.DOFade(1f, 0.1f));
        if (up)
        {
            arrowRt.anchoredPosition = new Vector2(x, -8f);
            seq.Insert(0f, arrowRt.DOAnchorPosY(6f, 0.14f).SetEase(Ease.OutQuad));
            seq.Insert(0.14f, arrowRt.DOAnchorPosY(0f, 0.1f).SetEase(Ease.InQuad));
            seq.Insert(0.3f, arrowRt.DOAnchorPosY(4f, 0.08f).SetEase(Ease.OutQuad));
            seq.Insert(0.38f, arrowRt.DOAnchorPosY(0f, 0.08f).SetEase(Ease.InQuad));
        }
        else
        {
            arrowRt.anchoredPosition = new Vector2(x, 8f);
            seq.Insert(0f, arrowRt.DOAnchorPosY(-4f, 0.16f).SetEase(Ease.OutQuad));
            seq.Insert(0.16f, arrowRt.DOAnchorPosY(0f, 0.12f).SetEase(Ease.InOutSine));
        }
        seq.Insert(1.2f, arrow.DOFade(0f, 0.3f));
        seq.OnComplete(() => { if (arrowRt != null) arrowRt.gameObject.SetActive(false); });
        UIMotion.Run(seq, arrowRt.gameObject);
    }
}
