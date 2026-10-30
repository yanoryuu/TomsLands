using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// #11 カードめくり。Y 軸で回し、真横（90°）になった瞬間に showFront で表の絵に替え、残りを回して戻す（1 枚 0.35 秒）。
/// 何枚もあるときは <see cref="FlipAll"/> で 0.18 秒ずつずらす。レアは先に 0.4 秒震えて溜め、めくった後に金に光る。
/// 省略モードでは回さずに showFront → onDone を即呼ぶ（最終状態は同じ）。
/// </summary>
public static class CardFlip
{
    private const float FlipDuration = 0.35f;
    private const float RareCharge = 0.4f;

    /// <summary>1 枚めくる。showFront は表に替える処理（絵・文字の差し替え）。</summary>
    public static void Flip(RectTransform card, Action showFront, float delay = 0f, bool rare = false, Action onDone = null)
    {
        if (card == null)
        {
            showFront?.Invoke();
            onDone?.Invoke();
            return;
        }
        DOTween.Kill(card.gameObject);
        if (UIMotion.IsSkip || !card.gameObject.activeInHierarchy)
        {
            card.localRotation = Quaternion.identity;
            showFront?.Invoke();
            onDone?.Invoke();
            return;
        }

        Vector2 basePos = UIMotion.BasePosOf(card);
        var seq = DOTween.Sequence().SetId(card.gameObject);
        float t = delay;
        if (rare)
        {
            // 震えて溜める（少し浮いて左右に傾く）
            float s = RareCharge / 4f;
            seq.Insert(t, card.DOAnchorPosY(basePos.y + 6f, s));
            seq.Insert(t, card.DOLocalRotate(new Vector3(0f, 0f, -3f), s));
            seq.Insert(t + s, card.DOLocalRotate(new Vector3(0f, 0f, 3f), s));
            seq.Insert(t + s * 2f, card.DOLocalRotate(new Vector3(0f, 0f, -2f), s));
            seq.Insert(t + s * 2f, card.DOAnchorPosY(basePos.y + 10f, s * 2f));
            seq.Insert(t + s * 3f, card.DOLocalRotate(Vector3.zero, s));
            t += RareCharge;
        }
        seq.Insert(t, card.DOLocalRotate(new Vector3(0f, 90f, 0f), FlipDuration * 0.5f).SetEase(Ease.InQuad));
        seq.InsertCallback(t + FlipDuration * 0.5f, () =>
        {
            showFront?.Invoke();
            card.localRotation = Quaternion.Euler(0f, -90f, 0f);
        });
        seq.Insert(t + FlipDuration * 0.5f, card.DOLocalRotate(Vector3.zero, FlipDuration * 0.5f).SetEase(Ease.OutBack, 2.5f));
        if (rare)
        {
            seq.Insert(t + FlipDuration * 0.5f, card.DOAnchorPosY(basePos.y, FlipDuration * 0.5f).SetEase(Ease.OutQuad));
            seq.InsertCallback(t + FlipDuration * 0.6f, () => RareGlow(card));
        }
        seq.OnComplete(() => onDone?.Invoke());
        seq.OnKill(() =>
        {
            if (card == null) return;
            card.localRotation = Quaternion.identity;
            if (rare) card.anchoredPosition = basePos;
        });
        UIMotion.Run(seq, card.gameObject);
    }

    /// <summary>
    /// 並んだカードを左から順にめくる（0.18 秒ずつずらす）。showFront(i) で i 枚目を表にする。isRare(i) が true のものは溜めてからめくる。
    /// onDone は最後の 1 枚がめくれ終わったとき。
    /// </summary>
    public static void FlipAll(IList<RectTransform> cards, Action<int> showFront, Func<int, bool> isRare = null, Action onDone = null, float step = 0.18f)
    {
        if (cards == null || cards.Count == 0)
        {
            onDone?.Invoke();
            return;
        }
        int remaining = cards.Count;
        float delay = 0f;
        for (int i = 0; i < cards.Count; i++)
        {
            int index = i;
            bool rare = isRare != null && isRare(index);
            Flip(cards[i], () => showFront?.Invoke(index), delay, rare, () =>
            {
                if (--remaining == 0) onDone?.Invoke();
            });
            delay += step + (rare ? RareCharge : 0f);
        }
    }

    /// <summary>カードの後ろで金の光をふくらませて消す（めくった直後のレア）。</summary>
    private static void RareGlow(RectTransform card)
    {
        if (card == null || card.parent == null) return;
        var glow = UIMotionFx.NewImage("RareGlow", card.parent, UIMotionFx.GlowSprite, new Color(1f, 0.85f, 0.35f, 0f), card.rect.size * 1.7f);
        UIMotionFx.IgnoreLayout(glow.gameObject);
        var rt = glow.rectTransform;
        rt.anchorMin = card.anchorMin;
        rt.anchorMax = card.anchorMax;
        rt.pivot = card.pivot;
        rt.anchoredPosition = card.anchoredPosition;
        rt.SetSiblingIndex(card.GetSiblingIndex());
        rt.localScale = Vector3.one * 0.7f;

        var spark = UIMotionFx.NewImage("RareSpark", card, UIMotionFx.SparkleSprite, Color.white, new Vector2(64f, 64f));
        UIMotionFx.IgnoreLayout(spark.gameObject);
        var srt = spark.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.8f, 0.85f);
        srt.localScale = Vector3.zero;

        var seq = DOTween.Sequence();
        seq.Insert(0f, glow.DOFade(1f, 0.12f));
        seq.Insert(0f, rt.DOScale(1.1f, 0.3f).SetEase(Ease.OutQuad));
        seq.Insert(0.45f, glow.DOFade(0f, 0.4f));
        seq.Insert(0.05f, srt.DOScale(1.2f, 0.15f).SetEase(Ease.OutBack));
        seq.Insert(0.05f, srt.DOLocalRotate(new Vector3(0f, 0f, 90f), 0.5f, RotateMode.LocalAxisAdd));
        seq.Insert(0.35f, srt.DOScale(0f, 0.2f).SetEase(Ease.InQuad));
        seq.OnComplete(() =>
        {
            UIMotion.DestroySafe(glow.gameObject);
            UIMotion.DestroySafe(spark.gameObject);
        });
        seq.OnKill(() =>
        {
            if (glow != null) UIMotion.DestroySafe(glow.gameObject);
            if (spark != null) UIMotion.DestroySafe(spark.gameObject);
        });
        UIMotion.Run(seq, glow.gameObject);
    }
}
