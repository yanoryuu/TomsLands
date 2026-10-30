using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// #13 つかんで置く。
/// ・<see cref="Lift"/>: 持ち上げる（0.1 秒で 1.12 倍・少し傾く・影）。ドラッグ開始時に掴んだもの（ゴースト）へ
/// ・<see cref="SetSlotGlow"/>: 置ける枠を金に光らせる（脈打つ）／消す
/// ・<see cref="Drop"/>: 置く（つぶれて戻る＋ほこり）。置けなかったときは placed=false で小さく揺れて戻る
/// 拡縮・回転・影は Drop で必ず元に戻す。ドラッグのイベント（AutoPlay が ExecuteEvents で送るものも）には触らない。
/// 省略モードでは見た目だけ即座に最終状態（持ち上げ中は大きさを変えない）。
/// </summary>
public static class DragFeel
{
    private const float LiftScale = 1.12f;

    /// <summary>持ち上げる。item はドラッグ中に動かすもの（ゴーストのアイコンなど）。</summary>
    public static void Lift(RectTransform item)
    {
        if (item == null || UIMotion.IsSkip) return;
        int id = item.GetInstanceID();
        if (!LiftedBase.TryGetValue(id, out var baseScale))
        {
            baseScale = UIMotion.BaseScaleOf(item);
            LiftedBase[id] = baseScale;
        }
        DOTween.Kill(item.gameObject);
        AddShadow(item);
        var seq = DOTween.Sequence().SetId(item.gameObject);
        seq.Insert(0f, item.DOScale(baseScale * LiftScale, 0.1f).SetEase(Ease.OutBack, 2f));
        seq.Insert(0f, item.DOLocalRotate(new Vector3(0f, 0f, -4f), 0.1f).SetEase(Ease.OutQuad));
        UIMotion.Run(seq, item.gameObject, unscaled: true);
    }

    /// <summary>置く。placed=true ならつぶれて戻る＋ほこり、false なら小さく揺れて戻る。onDone は戻り終わったとき。</summary>
    public static void Drop(RectTransform item, bool placed = true, Action onDone = null)
    {
        if (item == null)
        {
            onDone?.Invoke();
            return;
        }
        Vector3 baseScale = BaseOf(item);
        DOTween.Kill(item.gameObject);
        RemoveShadow(item);
        if (UIMotion.IsSkip || !item.gameObject.activeInHierarchy)
        {
            item.localScale = baseScale;
            item.localRotation = Quaternion.identity;
            onDone?.Invoke();
            return;
        }

        var seq = DOTween.Sequence().SetId(item.gameObject);
        if (placed)
        {
            seq.Insert(0f, item.DOScale(new Vector3(baseScale.x * 1.15f, baseScale.y * 0.85f, baseScale.z), 0.07f).SetEase(Ease.OutQuad));
            seq.Insert(0f, item.DOLocalRotate(Vector3.zero, 0.07f));
            seq.Insert(0.07f, item.DOScale(new Vector3(baseScale.x * 0.95f, baseScale.y * 1.05f, baseScale.z), 0.07f).SetEase(Ease.InOutSine));
            seq.Insert(0.14f, item.DOScale(baseScale, 0.08f).SetEase(Ease.OutSine));
            Puff(item);
        }
        else
        {
            seq.Insert(0f, item.DOScale(baseScale, 0.12f).SetEase(Ease.OutQuad));
            seq.Insert(0f, item.DOLocalRotate(new Vector3(0f, 0f, 6f), 0.06f));
            seq.Insert(0.06f, item.DOLocalRotate(new Vector3(0f, 0f, -4f), 0.06f));
            seq.Insert(0.12f, item.DOLocalRotate(Vector3.zero, 0.06f));
        }
        seq.OnComplete(() => onDone?.Invoke());
        seq.OnKill(() =>
        {
            if (item == null) return;
            item.localScale = baseScale;
            item.localRotation = Quaternion.identity;
        });
        UIMotion.Run(seq, item.gameObject, unscaled: true);
    }

    /// <summary>置ける枠を光らせる（on=false で消す）。slot の上に金の枠線を足して脈打たせる。</summary>
    public static void SetSlotGlow(RectTransform slot, bool on)
    {
        if (slot == null) return;
        var existing = slot.Find("DropGlow") as RectTransform;
        if (!on)
        {
            if (existing != null)
            {
                DOTween.Kill(existing.gameObject);
                UIMotion.DestroySafe(existing.gameObject);
            }
            return;
        }
        if (existing != null) return;

        var img = UIMotionFx.NewImage("DropGlow", slot, UIMotionFx.FrameSprite, UIMotionFx.Gold2, Vector2.zero);
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 0.8f; // 内側に収めた分、縁を太くして目立たせる
        UIMotionFx.IgnoreLayout(img.gameObject);
        var rt = img.rectTransform;
        UIMotionFx.Stretch(rt);
        // 枠の内側に収める（外へはみ出すと、間を詰めて並べた隣の枠に重なる）。
        // 隣の枠と矩形が重なる並べ方（絵の余白で間を空けている売り場など）は、隣との中間より内側まで縮める
        Vector2 inset = InsetForNeighbors(slot, GlowInset);
        rt.offsetMin = inset;
        rt.offsetMax = -inset;
        if (UIMotion.IsSkip) return; // 光る枠は出す（最終状態）。脈打ちだけ省く

        // 脈打ちは内側へ縮むだけ（膨らむと隣へはみ出す）
        var seq = DOTween.Sequence().SetId(img.gameObject);
        seq.Append(img.DOFade(0.45f, 0.45f).SetEase(Ease.InOutSine));
        seq.Join(rt.DOScale(0.97f, 0.45f).SetEase(Ease.InOutSine));
        seq.Append(img.DOFade(1f, 0.45f).SetEase(Ease.InOutSine));
        seq.Join(rt.DOScale(1f, 0.45f).SetEase(Ease.InOutSine));
        seq.SetLoops(-1);
        UIMotion.RunLoop(seq, img.gameObject, unscaled: true);
    }

    /// <summary>光る枠を枠の縁からどれだけ内側に置くか（枠の座標）。</summary>
    private const float GlowInset = 2f;

    /// <summary>
    /// 光る枠の内側への寄せ（x: 左右、y: 上下。slot の座標）。同じ親の兄弟（同じくらいの大きさの枠）との中心の間隔の半分より
    /// 外へ出ないようにする。隣が無ければ minInset だけ。
    /// </summary>
    private static Vector2 InsetForNeighbors(RectTransform slot, float minInset)
    {
        Vector2 inset = new(minInset, minInset);
        var parent = slot.parent;
        if (parent == null) return inset;
        Vector2 size = slot.rect.size;
        Vector3 scale = slot.localScale;
        if (size.x <= 0f || size.y <= 0f || Mathf.Abs(scale.x) < 1e-4f || Mathf.Abs(scale.y) < 1e-4f) return inset;
        Vector2 sizeInParent = new(size.x * Mathf.Abs(scale.x), size.y * Mathf.Abs(scale.y));
        Vector2 center = (Vector2)slot.localPosition + Vector2.Scale(slot.rect.center, scale);
        float halfX = float.MaxValue, halfY = float.MaxValue; // 親の座標で、隣までの距離の半分
        foreach (Transform t in parent)
        {
            if (t == slot || !t.gameObject.activeInHierarchy || !(t is RectTransform other)) continue;
            Vector2 osize = Vector2.Scale(other.rect.size, new Vector2(Mathf.Abs(other.localScale.x), Mathf.Abs(other.localScale.y)));
            // 大きさの違う物（背景・見出しなど）は隣の枠として数えない
            if (Mathf.Abs(osize.x - sizeInParent.x) > sizeInParent.x * 0.25f || Mathf.Abs(osize.y - sizeInParent.y) > sizeInParent.y * 0.25f) continue;
            Vector2 d = (Vector2)other.localPosition + Vector2.Scale(other.rect.center, other.localScale) - center;
            if (Mathf.Abs(d.y) < sizeInParent.y * 0.5f) halfX = Mathf.Min(halfX, Mathf.Abs(d.x) * 0.5f);      // 同じ行
            else if (Mathf.Abs(d.x) < sizeInParent.x * 0.5f) halfY = Mathf.Min(halfY, Mathf.Abs(d.y) * 0.5f); // 同じ列
        }
        if (halfX < float.MaxValue) inset.x = Mathf.Max(inset.x, size.x * 0.5f - halfX / Mathf.Abs(scale.x) + minInset);
        if (halfY < float.MaxValue) inset.y = Mathf.Max(inset.y, size.y * 0.5f - halfY / Mathf.Abs(scale.y) + minInset);
        inset.x = Mathf.Min(inset.x, size.x * 0.4f);
        inset.y = Mathf.Min(inset.y, size.y * 0.4f);
        return inset;
    }

    /// <summary>at の足元にほこりを散らす（0.35 秒）。</summary>
    public static void Puff(RectTransform at)
    {
        if (at == null || UIMotion.IsSkip) return;
        var canvas = at.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var root = (RectTransform)canvas.rootCanvas.transform;
        var center = (Vector2)root.InverseTransformPoint(at.TransformPoint(new Vector3(at.rect.center.x, at.rect.yMin, 0f)));
        var holder = new GameObject("DropPuff", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.gameObject.layer = at.gameObject.layer;
        holder.SetParent(root, false);
        holder.anchoredPosition = center - Vector2.Scale(root.rect.size, new Vector2(0.5f, 0.5f) - root.pivot);
        var seq = DOTween.Sequence();
        const int count = 7;
        for (int i = 0; i < count; i++)
        {
            float ang = Mathf.Lerp(200f, 340f, i / (float)(count - 1)) * Mathf.Deg2Rad;
            var dot = UIMotionFx.NewImage("Dust", holder, UIMotionFx.SoftDotSprite, new Color(0.95f, 0.9f, 0.78f, 0.9f), new Vector2(26f, 26f));
            var drt = dot.rectTransform;
            drt.localScale = Vector3.one * 0.6f;
            Vector2 dir = new(Mathf.Cos(ang), -Mathf.Sin(ang) * 0.4f + 0.25f);
            seq.Insert(0f, drt.DOAnchorPos(dir * 46f, 0.35f).SetEase(Ease.OutCubic));
            seq.Insert(0f, drt.DOScale(1.5f, 0.35f).SetEase(Ease.OutQuad));
            seq.Insert(0.1f, dot.DOFade(0f, 0.25f));
        }
        seq.OnComplete(() => UIMotion.DestroySafe(holder.gameObject));
        UIMotion.Run(seq, holder.gameObject, unscaled: true);
    }

    private static readonly System.Collections.Generic.Dictionary<int, Vector3> LiftedBase = new();

    private static Vector3 BaseOf(RectTransform item)
    {
        // 持ち上げ中なら持ち上げる前の大きさ
        int id = item.GetInstanceID();
        if (LiftedBase.TryGetValue(id, out var s))
        {
            LiftedBase.Remove(id);
            return s;
        }
        return UIMotion.BaseScaleOf(item);
    }

    /// <summary>自分で足した影（型がちょうど Shadow で DontSave のもの。Outline や元からの Shadow は除く）。</summary>
    private static Shadow OwnShadow(RectTransform item)
    {
        foreach (var s in item.GetComponents<Shadow>())
            if (s.GetType() == typeof(Shadow) && s.hideFlags == HideFlags.DontSave) return s;
        return null;
    }

    private static void AddShadow(RectTransform item)
    {
        if (!item.TryGetComponent<Graphic>(out _)) return;
        var shadow = OwnShadow(item);
        if (shadow == null)
        {
            shadow = item.gameObject.AddComponent<Shadow>();
            shadow.hideFlags = HideFlags.DontSave;
        }
        shadow.effectColor = new Color(0.1f, 0.05f, 0f, 0.4f);
        shadow.effectDistance = new Vector2(6f, -12f);
        shadow.enabled = true;
    }

    private static void RemoveShadow(RectTransform item)
    {
        var shadow = OwnShadow(item);
        if (shadow != null) UIMotion.DestroySafe(shadow);
    }
}
