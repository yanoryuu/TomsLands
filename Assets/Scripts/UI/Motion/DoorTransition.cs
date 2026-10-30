using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #17 両開きの扉。店のホームから施設（鍛冶屋・情報屋・勇者など）に入るとき、画面の上に木の両開きの扉を一瞬かぶせ、
/// 一度押されて揺れてから奥へ開き、光が差して消える（全体 0.6 秒）。中央の上に施設名の札。
///
/// ・見た目だけ: 入力を受けない最前面寄りの層（GraphicRaycaster なし・全部 raycastTarget=false）に出す。
///   画面の切り替え（SetActive）は呼ぶ側が今までどおり即座に行う（onClosed も Play の中で同期で呼ぶ）ので、
///   ゲームの状態・AutoPlay から見える画面は今と同じ瞬間に替わり、扉の下のボタンも押せたまま。
/// ・省略モード（AutoPlay・人格マラソン）では何も出さずに onClosed だけ呼ぶ。「演出を速くする」で速く。unscaled（一時停止中も動く）。
/// ・扉の途中でもう一度呼ぶと、前の扉を消して閉じた扉から出し直す（扉は 1 組だけ）。
/// </summary>
public static class DoorTransition
{
    /// <summary>全体の長さ（通常の速さ）。</summary>
    public const float Duration = 0.6f;
    /// <summary>層の並び順。コイン（30000）より上、祝い（31000）・トースト・ローディングより下。</summary>
    public const int SortingOrder = 30500;

    private const string TweenId = "UIMotion.DoorTransition";

    /// <summary>false にすると扉を出さない（onClosed だけ呼ぶ）。</summary>
    public static bool Enabled { get; set; } = true;

    private static GameObject current;
    private static Texture2D doorTexture;

    /// <summary>扉が出ているか（開きかけを含む）。</summary>
    public static bool IsPlaying => current != null;

    /// <summary>扉を出す。label は中央上の札の文字（null・空なら札なし）。</summary>
    public static void Play(string label = null) => Play(null, label);

    /// <summary>
    /// 閉じた扉を出し、そのすぐ後（同じフレーム・同期）に onClosed で画面を切り替えさせ、扉が開いて新しい画面が見える。
    /// 省略モード・無効なら扉は出さず onClosed だけ呼ぶ。
    /// </summary>
    public static void Play(Action onClosed, string label = null)
    {
        Stop();
        if (!Enabled || UIMotion.IsSkip)
        {
            onClosed?.Invoke();
            return;
        }

        var layer = UIMotionFx.Layer(SortingOrder);
        var root = new GameObject("MotionDoor", typeof(RectTransform)).GetComponent<RectTransform>();
        root.gameObject.layer = layer.gameObject.layer;
        root.SetParent(layer, false);
        UIMotionFx.Stretch(root);
        current = root.gameObject;

        // 光（扉の奥。扉より先に描く）
        var slit = UIMotionFx.NewImage("Slit", root, UIMotionFx.GlowSprite, new Color(1f, 0.96f, 0.8f, 0f), new Vector2(220f, 1300f));
        var glow = UIMotionFx.NewImage("Glow", root, UIMotionFx.GlowSprite, new Color(1f, 0.93f, 0.72f, 0f), new Vector2(1700f, 1250f));
        var rays = UIMotionFx.NewImage("Rays", root, UIMotionFx.RaysSprite, new Color(1f, 0.92f, 0.62f, 0f), new Vector2(1500f, 1500f));
        rays.preserveAspect = true;

        // 扉（左右）
        var left = CreateLeaf("DoorLeft", root, true);
        var right = CreateLeaf("DoorRight", root, false);

        // 施設名の札
        RectTransform plateRt = null;
        CanvasGroup plateGroup = null;
        if (!string.IsNullOrEmpty(label))
        {
            var plate = UIMotionFx.NewImage("NamePlate", root, UIMotionFx.GoldPlateSprite, Color.white, new Vector2(Mathf.Max(360f, 90f + label.Length * 64f), 112f));
            plateRt = plate.rectTransform;
            plateRt.anchorMin = plateRt.anchorMax = new Vector2(0.5f, 1f);
            plateRt.anchoredPosition = new Vector2(0f, -150f);
            var text = UIMotionFx.NewText("Label", plateRt, label, 56f, UIMotionFx.Cream, plateRt.sizeDelta - new Vector2(30f, 10f));
            text.fontSharedMaterial = UIMotionFx.OutlinedMaterial(text.font);
            text.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            plateGroup = UIMotion.EnsureGroup(plate.gameObject);
        }

        // 扉で覆ってから画面を切り替える（同じフレームなので、古い画面と新しい画面が扉なしで見えることはない）
        if (onClosed != null)
        {
            try { onClosed(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        var seq = DOTween.Sequence().SetId(TweenId);
        // 押されて揺れる（0→9°→3°）→ 奥へ開く（→100°）→ 消える
        foreach (var leaf in new[] { left, right })
        {
            var l = leaf;
            seq.Insert(0.03f, DOTween.To(() => l.Angle, v => l.Angle = v, 9f, 0.07f).SetEase(Ease.OutQuad));
            seq.Insert(0.10f, DOTween.To(() => l.Angle, v => l.Angle = v, 3f, 0.04f).SetEase(Ease.InOutSine));
            seq.Insert(0.14f, DOTween.To(() => l.Angle, v => l.Angle = v, 100f, 0.34f).SetEase(Ease.InOutQuad));
            seq.Insert(0.40f, l.DOFade(0f, 0.14f).SetEase(Ease.InQuad));
        }
        // 押した瞬間に継ぎ目から光が漏れ、開くと奥から光が差して消える
        seq.Insert(0.03f, slit.DOFade(0.95f, 0.07f));
        seq.Insert(0.22f, slit.DOFade(0f, 0.12f));
        seq.Insert(0.16f, glow.DOFade(0.9f, 0.12f).SetEase(Ease.OutQuad));
        seq.Insert(0.32f, glow.DOFade(0f, 0.28f).SetEase(Ease.InQuad));
        seq.Insert(0.18f, rays.DOFade(0.5f, 0.12f));
        seq.Insert(0.36f, rays.DOFade(0f, 0.24f));
        seq.Insert(0f, rays.rectTransform.DOLocalRotate(new Vector3(0f, 0f, -14f), Duration).SetEase(Ease.Linear));
        seq.Insert(0.16f, rays.rectTransform.DOScale(1.15f, 0.44f).SetEase(Ease.OutQuad));
        if (plateRt != null)
        {
            seq.Insert(0.16f, plateGroup.DOFade(0f, 0.14f).SetEase(Ease.InQuad));
            seq.Insert(0.16f, plateRt.DOScale(1.08f, 0.14f).SetEase(Ease.OutQuad));
            seq.Insert(0.16f, plateRt.DOAnchorPosY(-130f, 0.14f).SetEase(Ease.OutQuad));
        }
        var go = root.gameObject;
        seq.OnKill(() =>
        {
            if (current == go) current = null;
            UIMotion.DestroySafe(go);
        });
        UIMotion.Run(seq, go, unscaled: true);
    }

    /// <summary>出ている扉をすぐ消す（施設に入ってすぐ店へ戻ったときなど）。</summary>
    public static void Stop()
    {
        DOTween.Kill(TweenId);
        if (current != null)
        {
            UIMotion.DestroySafe(current);
            current = null;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        Enabled = true;
    }

    private static DoorLeafGraphic CreateLeaf(string name, RectTransform parent, bool leftSide)
    {
        // CanvasRenderer は先に付けておく（Graphic の RequireComponent に任せると付かないことがある）
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        UIMotionFx.Stretch(rt);
        var leaf = go.AddComponent<DoorLeafGraphic>();
        leaf.raycastTarget = false;
        leaf.Texture = DoorTexture;
        leaf.LeftSide = leftSide;
        leaf.Angle = 0f;
        return leaf;
    }

    // =====================================================
    // 扉の絵（コードで描く。#6 の木の幕と同じ板の色、金の帯と取っ手）
    // =====================================================

    /// <summary>扉 1 枚の絵（u=0 が蝶番側、u=1 が取っ手側）。</summary>
    public static Texture2D DoorTexture => doorTexture != null ? doorTexture : doorTexture = MakeDoorTexture(320, 360);

    private static Texture2D MakeDoorTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
            name = "MotionDoorLeaf",
        };
        var px = new Color[w * h];
        Color dark = new Color32(0x3b, 0x22, 0x14, 0xff);
        Color light = new Color32(0x8a, 0x5a, 0x30, 0xff);
        Color[] tones = { new Color32(0x6b, 0x3e, 0x22, 0xff), UIMotionFx.Wood, new Color32(0x64, 0x3a, 0x22, 0xff), UIMotionFx.Wood2 };
        Color gold = UIMotionFx.Gold;
        Color goldDark = new Color32(0x8a, 0x5a, 0x14, 0xff);
        const int border = 12;
        const int line = 6;
        const int planks = 4;
        float plankW = (w - border * 2f) / planks;
        float[] bands = { 0.22f, 0.78f };
        const int bandH = 16;
        var knob = new Vector2(w * 0.86f, h * 0.48f);
        const float knobR = 15f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c;
                int edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                if (edge < border) c = dark;
                else if (edge < border + line) c = light;
                else
                {
                    float lx = x - border;
                    int k = Mathf.Clamp((int)(lx / plankW), 0, planks - 1);
                    float px0 = lx - k * plankW;
                    if (px0 < 3f) c = dark;
                    else
                    {
                        float grain = Mathf.Sin(px0 * 0.33f + Mathf.Sin(y * 0.025f + k * 1.9f) * 3.5f + k * 11f) * 0.5f + 0.5f;
                        c = Color.Lerp(tones[k], tones[k] * 0.82f, grain * 0.35f);
                    }
                    // 金の帯（上下 2 本）と鋲
                    foreach (float b in bands)
                    {
                        float by = h * b;
                        float dy = Mathf.Abs(y - by);
                        if (dy < bandH * 0.5f)
                        {
                            c = dy > bandH * 0.5f - 2.5f ? goldDark : Color.Lerp(UIMotionFx.Gold2, gold, dy / (bandH * 0.5f));
                            float cx = border + (k + 0.5f) * plankW;
                            float rd = Vector2.Distance(new Vector2(x, y), new Vector2(cx, by));
                            if (rd < 4.5f) c = rd < 2.5f ? UIMotionFx.Cream : goldDark;
                        }
                    }
                }
                // 両端の陰（蝶番側と取っ手側）
                float u = (x + 0.5f) / w;
                float shade = 1f - 0.18f * Mathf.Clamp01(1f - u / 0.1f) - 0.22f * Mathf.Clamp01((u - 0.9f) / 0.1f);
                c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);

                // 取っ手（台座の暗い丸 + 金の玉）
                float kd = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), knob);
                if (kd < knobR + 6f)
                {
                    float a = Mathf.Clamp01(knobR + 6f - kd);
                    c = Color.Lerp(c, dark, a * 0.8f);
                }
                if (kd < knobR)
                {
                    var hl = knob + new Vector2(-knobR * 0.3f, knobR * 0.3f);
                    float t = Mathf.Clamp01(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), hl) / (knobR * 1.3f));
                    var kc = t < 0.35f ? Color.Lerp(new Color32(0xff, 0xf3, 0xb0, 0xff), gold, t / 0.35f) : Color.Lerp(gold, goldDark, (t - 0.35f) / 0.65f);
                    c = Color.Lerp(c, kc, Mathf.Clamp01(knobR - kd));
                }
                px[y * w + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply(false, false);
        return tex;
    }
}
