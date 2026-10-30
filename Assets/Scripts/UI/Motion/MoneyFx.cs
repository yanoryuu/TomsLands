using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// #2 お金が飛んで数字が回る。
/// ・<see cref="Gain"/>: 一式（+N が浮く → コインが 0.08 秒ずつ財布へ飛ぶ → 着くたびに財布が弾む → 数字が 0.8 秒で回る）
/// ・<see cref="FloatText"/>: 「+1,200G」などを浮かせて消す
/// ・数字だけ回すなら <see cref="UIMotion.CountTo"/>、コインだけなら <see cref="CoinBurst.Fly"/>
/// 省略モード: 何も飛ばさず、数字は即 format(after)、onDone も即呼ぶ。
/// </summary>
public static class MoneyFx
{
    /// <summary>
    /// お金が入る演出一式。from（売れた品・報酬の札など）から wallet（所持金の枠）へコインを飛ばし、walletText を before→after に回す。
    /// coins を省くと増えた額から 4〜10 枚を決める。onDone は数字が回り終わったとき。
    /// </summary>
    public static void Gain(RectTransform from, RectTransform wallet, TMP_Text walletText, long before, long after,
        Func<long, string> format = null, int coins = -1, Action onDone = null)
    {
        format ??= v => $"{v:N0}G";
        long delta = after - before;
        if (UIMotion.IsSkip || wallet == null)
        {
            if (walletText != null) walletText.text = format(after);
            onDone?.Invoke();
            return;
        }

        if (from != null && delta != 0)
            FloatText(from, delta > 0 ? $"+{delta:N0}G" : $"-{-delta:N0}G", delta > 0 ? UIMotionFx.Gold2 : UIMotionFx.Red);

        if (coins < 0) coins = Mathf.Clamp(4 + (int)Math.Log10(Math.Max(1, Math.Abs(delta))), 4, 10);
        if (from == null || delta <= 0 || coins == 0)
        {
            UIMotion.Bump(wallet);
            var t = UIMotion.CountTo(walletText, before, after, format);
            if (t != null) t.OnComplete(() => onDone?.Invoke());
            else onDone?.Invoke();
            return;
        }

        bool counting = false;
        CoinBurst.Fly(from, wallet, coins,
            onEachArrive: () =>
            {
                UIMotion.Bump(wallet);
                if (counting) return;
                counting = true;
                var t = UIMotion.CountTo(walletText, before, after, format);
                if (t != null) t.OnComplete(() => onDone?.Invoke());
                else onDone?.Invoke();
            },
            onAllArrived: null);
    }

    /// <summary>anchor の上に文字を出し、上へ浮かせて消す（0.9 秒）。</summary>
    public static void FloatText(RectTransform anchor, string text, Color color, float fontSize = 44f, float rise = 40f)
    {
        if (UIMotion.IsSkip || anchor == null || string.IsNullOrEmpty(text)) return;
        var layer = UIMotionFx.Layer(UIMotionFx.OverlayOrder);
        var label = UIMotionFx.NewText("MotionFloatText", layer, text, fontSize, color, new Vector2(600f, 80f));
        label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
        var rt = label.rectTransform;
        Vector2 start = UIMotionFx.LayerPos(layer, anchor) + new Vector2(0f, 10f);
        rt.anchoredPosition = start;
        label.alpha = 0f;
        rt.localScale = Vector3.one * 0.8f;

        var seq = DOTween.Sequence();
        seq.Insert(0f, label.DOFade(1f, 0.12f));
        seq.Insert(0f, rt.DOScale(1f, 0.2f).SetEase(Ease.OutBack));
        seq.Insert(0f, rt.DOAnchorPosY(start.y + rise * 0.4f, 0.35f).SetEase(Ease.OutCubic));
        seq.Insert(0.45f, rt.DOAnchorPosY(start.y + rise, 0.45f).SetEase(Ease.InQuad));
        seq.Insert(0.5f, label.DOFade(0f, 0.4f));
        seq.OnComplete(() => UIMotion.DestroySafe(label.gameObject));
        UIMotion.Run(seq, label.gameObject);
    }
}

/// <summary>#2 のコイン飛び。演出用レイヤー（最前面・入力なし）に描くので、どの画面からでも使える。</summary>
public static class CoinBurst
{
    /// <summary>
    /// from から to へコインを count 枚、0.08 秒ずつ放物線で飛ばす（1 枚 0.45 秒）。
    /// onEachArrive は 1 枚着くたび、onAllArrived は最後の 1 枚が着いたとき。省略モードでは飛ばさずに両方を 1 回ずつ呼ぶ。
    /// </summary>
    public static void Fly(RectTransform from, RectTransform to, int count, Action onEachArrive = null, Action onAllArrived = null,
        float interval = 0.08f, float flight = 0.45f)
    {
        if (UIMotion.IsSkip || from == null || to == null || count <= 0)
        {
            onEachArrive?.Invoke();
            onAllArrived?.Invoke();
            return;
        }

        var layer = UIMotionFx.Layer(UIMotionFx.OverlayOrder);
        Vector2 start = UIMotionFx.LayerPos(layer, from);
        Vector2 end = UIMotionFx.LayerPos(layer, to);
        var sprite = UIMotionFx.CoinSprite;
        int arrived = 0;
        var rng = new System.Random(from.GetInstanceID() ^ count);

        for (int i = 0; i < count; i++)
        {
            var img = UIMotionFx.NewImage("MotionCoin", layer, sprite, Color.white, new Vector2(37f, 52f));
            img.preserveAspect = true;
            var rt = img.rectTransform;
            Vector2 jitter = new((float)(rng.NextDouble() * 60 - 30), (float)(rng.NextDouble() * 30 - 10));
            Vector2 p0 = start + jitter;
            // 弧の頂点: 始点と終点の中間から上（画面の上側）へ持ち上げる
            Vector2 ctrl = (p0 + end) * 0.5f + new Vector2((float)(rng.NextDouble() * 120 - 60), 160f + (float)rng.NextDouble() * 80f);
            rt.anchoredPosition = p0;
            rt.localScale = Vector3.zero;

            var seq = DOTween.Sequence();
            float at = i * interval;
            seq.Insert(at, rt.DOScale(1f, 0.12f).SetEase(Ease.OutBack));
            seq.Insert(at, DOVirtual.Float(0f, 1f, flight, t =>
            {
                float u = 1f - t;
                rt.anchoredPosition = u * u * p0 + 2f * u * t * ctrl + t * t * end;
            }).SetEase(Ease.InQuad));
            seq.Insert(at, rt.DOLocalRotate(new Vector3(0f, 0f, 360f), flight, RotateMode.FastBeyond360));
            seq.Insert(at + flight - 0.08f, rt.DOScale(0.6f, 0.08f));
            seq.InsertCallback(at + flight, () =>
            {
                img.enabled = false;
                arrived++;
                onEachArrive?.Invoke();
                if (arrived == count) onAllArrived?.Invoke();
            });
            seq.OnComplete(() => UIMotion.DestroySafe(img.gameObject));
            UIMotion.Run(seq, img.gameObject);
        }
    }
}
