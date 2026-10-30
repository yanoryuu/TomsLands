using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// #5 ここ一番の祝い（ランク S・新記録・大儲け・格上げなど）。全体 1.5 秒:
/// 白フラッシュ → 紋章のハンコ（2.4 倍から押し付け）→ 放射光が回る → 星が順にはじける → 紙吹雪 → 消える。
/// 演出用レイヤー（入力なし・最前面寄り）に生成するので、シーンの配線は要らない。unscaled（一時停止中も最後まで動く）。
/// 省略モードでは何も出さずに onDone を即呼ぶ。
/// </summary>
public static class Celebration
{
    private const float Total = 1.5f;
    private static readonly Color[] ConfettiColors =
    {
        UIMotionFx.Gold2, UIMotionFx.Red, UIMotionFx.Green, UIMotionFx.Cream, new Color32(0x6c, 0xb4, 0xe8, 0xff),
    };

    /// <summary>
    /// 祝いを出す。title は紋章の下の札に出す文字（null なら札なし）。emblem を渡すと紋章の代わりにその絵を押す。
    /// at を渡すとその UI の位置を中心にする（省くと画面中央）。
    /// </summary>
    public static void Play(string title = null, Sprite emblem = null, Action onDone = null, RectTransform at = null)
    {
        if (UIMotion.IsSkip)
        {
            onDone?.Invoke();
            return;
        }

        var layer = UIMotionFx.Layer(UIMotionFx.CelebrationOrder);
        var root = new GameObject("MotionCelebration", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(layer, false);
        UIMotionFx.Stretch(root);
        Vector2 center = at != null ? UIMotionFx.LayerPos(layer, at) : Vector2.zero;
        var seq = DOTween.Sequence();

        // 白フラッシュ（0.1〜0.3 秒）
        var flash = UIMotionFx.NewImage("Flash", root, UIMotionFx.WhiteSprite, new Color(1f, 1f, 1f, 0f), Vector2.zero);
        UIMotionFx.Stretch(flash.rectTransform);
        seq.Insert(0.02f, flash.DOFade(0.8f, 0.04f));
        seq.Insert(0.06f, flash.DOFade(0f, 0.22f).SetEase(Ease.OutQuad));

        // 放射光（ゆっくり回りながら出て、最後に消える）
        var rays = UIMotionFx.NewImage("Rays", root, UIMotionFx.RaysSprite, new Color(1f, 0.92f, 0.6f, 0f), new Vector2(760f, 760f));
        rays.rectTransform.anchoredPosition = center;
        seq.Insert(0.1f, rays.DOFade(1f, 0.18f));
        seq.Insert(0f, rays.rectTransform.DOLocalRotate(new Vector3(0f, 0f, -80f), Total, RotateMode.FastBeyond360).SetEase(Ease.Linear));
        seq.Insert(Total - 0.3f, rays.DOFade(0f, 0.3f));

        // 紋章のハンコ（45 度傾けたまま 2.4 倍 → 0.9 → 1.08 → 1）
        var seal = UIMotionFx.NewImage("Seal", root, emblem != null ? emblem : UIMotionFx.SealSprite, new Color(1f, 1f, 1f, 0f), new Vector2(260f, 260f));
        seal.preserveAspect = true;
        var sealRt = seal.rectTransform;
        sealRt.anchoredPosition = center;
        sealRt.localRotation = Quaternion.Euler(0f, 0f, emblem != null ? 0f : 18f);
        sealRt.localScale = Vector3.one * 2.4f;
        seq.Insert(0.12f, seal.DOFade(1f, 0.06f));
        seq.Insert(0.12f, sealRt.DOScale(0.9f, 0.1f).SetEase(Ease.InQuad));
        seq.Insert(0.22f, sealRt.DOScale(1.08f, 0.06f).SetEase(Ease.OutQuad));
        seq.Insert(0.28f, sealRt.DOScale(1f, 0.08f).SetEase(Ease.InOutSine));
        seq.Insert(Total - 0.22f, sealRt.DOScale(0.6f, 0.22f).SetEase(Ease.InBack));
        seq.Insert(Total - 0.22f, seal.DOFade(0f, 0.22f));

        // 札（文字）
        if (!string.IsNullOrEmpty(title))
        {
            var plate = UIMotionFx.NewImage("TitlePlate", root, UIMotionFx.GoldPlateSprite, Color.white, new Vector2(460f, 96f));
            var plateRt = plate.rectTransform;
            plateRt.anchoredPosition = center + new Vector2(0f, -190f);
            var label = UIMotionFx.NewText("Title", plateRt, title, 46f, UIMotionFx.Cream, new Vector2(440f, 90f));
            label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
            var plateGroup = UIMotion.EnsureGroup(plate.gameObject);
            plateGroup.alpha = 0f;
            plateRt.localScale = new Vector3(0.4f, 1f, 1f);
            seq.Insert(0.3f, plateGroup.DOFade(1f, 0.1f));
            seq.Insert(0.3f, plateRt.DOScaleX(1f, 0.22f).SetEase(Ease.OutBack));
            seq.Insert(Total - 0.25f, plateGroup.DOFade(0f, 0.25f));
        }

        // 星が順にはじける（紋章のまわり 5 つ）
        for (int i = 0; i < 5; i++)
        {
            float ang = (90f + 72f * i) * Mathf.Deg2Rad;
            var star = UIMotionFx.NewImage("Star", root, UIMotionFx.Star5Sprite, Color.white, new Vector2(70f, 70f));
            var srt = star.rectTransform;
            srt.anchoredPosition = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 175f;
            srt.localScale = Vector3.zero;
            srt.localRotation = Quaternion.Euler(0f, 0f, -90f);
            float t = 0.38f + i * 0.06f;
            seq.Insert(t, srt.DOScale(1.5f, 0.09f).SetEase(Ease.OutQuad));
            seq.Insert(t, srt.DOLocalRotate(new Vector3(0f, 0f, 10f), 0.09f));
            seq.Insert(t + 0.09f, srt.DOScale(1f, 0.08f));
            seq.Insert(t + 0.09f, srt.DOLocalRotate(Vector3.zero, 0.08f));
            seq.Insert(Total - 0.2f, srt.DOScale(0f, 0.2f).SetEase(Ease.InBack));
        }

        // 紙吹雪（紋章から上へはじけて、くるくる落ちる）
        var rng = new System.Random(Environment.TickCount);
        for (int i = 0; i < 40; i++)
        {
            var c = ConfettiColors[i % ConfettiColors.Length];
            var piece = UIMotionFx.NewImage("Confetti", root, UIMotionFx.WhiteSprite, c, new Vector2(14f, 22f));
            var prt = piece.rectTransform;
            prt.anchoredPosition = center;
            piece.color = new Color(c.r, c.g, c.b, 0f);
            float vx = (float)(rng.NextDouble() * 2 - 1) * 520f;
            float vy = 380f + (float)rng.NextDouble() * 420f;
            float spin = (float)(rng.NextDouble() * 2 - 1) * 720f;
            float start = 0.18f + (float)rng.NextDouble() * 0.08f;
            float life = Total - start - 0.05f;
            const float gravity = 1300f;
            seq.Insert(start, piece.DOFade(1f, 0.05f));
            seq.Insert(start, DOVirtual.Float(0f, life, life, t =>
            {
                prt.anchoredPosition = center + new Vector2(vx * t, vy * t - 0.5f * gravity * t * t);
                prt.localRotation = Quaternion.Euler(0f, 0f, spin * t);
                prt.localScale = new Vector3(Mathf.Cos(t * 9f + vx), 1f, 1f); // ひらひら（裏返る）
            }).SetEase(Ease.Linear));
            seq.Insert(Total - 0.3f, piece.DOFade(0f, 0.25f));
        }

        seq.OnComplete(() =>
        {
            UIMotion.DestroySafe(root.gameObject);
            onDone?.Invoke();
        });
        UIMotion.Run(seq, root.gameObject, unscaled: true);
    }
}
