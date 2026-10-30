using DG.Tweening;
using UnityEngine;

/// <summary>#14 ダメージ数字の種類。</summary>
public enum DamageKind
{
    /// <summary>ふつう（白）。</summary>
    Normal = 0,
    /// <summary>クリティカル（大きく黄色・「!」付き）。</summary>
    Critical = 1,
    /// <summary>回復（緑・「+」付き、まっすぐ上へ）。</summary>
    Heal = 2,
}

/// <summary>
/// #14 ダメージ数字。数字が弾んで出て（0.3→1.4→1 倍）、放物線で横へ飛びながら落ちて消える（0.8 秒）。
/// クリティカルは 1.5 倍の大きさで黄色、回復は緑でまっすぐ上へ。
/// 演出用レイヤー（並び順 <see cref="UIMotionFx.DamageOrder"/>。<see cref="SortingOrder"/> で変えられる）に生成する。
/// timeScale に従う（配信の一時停止・倍速と一緒に止まる／速くなる）。省略モードでは出さない。
/// </summary>
public static class DamageNumber
{
    /// <summary>描く層の並び順（配信画面の UI との重なりを変えたいとき）。</summary>
    public static int SortingOrder { get; set; } = UIMotionFx.DamageOrder;

    private static int serial;

    /// <summary>anchor（敵の絵・キャラの枠など UI）の上に出す。</summary>
    public static void Spawn(RectTransform anchor, int amount, DamageKind kind = DamageKind.Normal)
    {
        if (anchor == null || UIMotion.IsSkip) return;
        var layer = UIMotionFx.Layer(SortingOrder);
        SpawnAt(layer, UIMotionFx.LayerPos(layer, anchor, new Vector2(0f, anchor.rect.height * 0.25f)), amount, kind);
    }

    /// <summary>スクリーン座標に出す。</summary>
    public static void SpawnAtScreen(Vector2 screenPos, int amount, DamageKind kind = DamageKind.Normal)
    {
        if (UIMotion.IsSkip) return;
        var layer = UIMotionFx.Layer(SortingOrder);
        SpawnAt(layer, UIMotionFx.ScreenToLayer(layer, screenPos), amount, kind);
    }

    /// <summary>ワールド座標（2D の敵の位置など）に出す。cam を省くと Camera.main。</summary>
    public static void SpawnAtWorld(Vector3 worldPos, int amount, DamageKind kind = DamageKind.Normal, Camera cam = null)
    {
        if (UIMotion.IsSkip) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        SpawnAtScreen(cam.WorldToScreenPoint(worldPos), amount, kind);
    }

    private static void SpawnAt(RectTransform layer, Vector2 pos, int amount, DamageKind kind)
    {
        string text = kind switch
        {
            DamageKind.Heal => $"+{amount:N0}",
            DamageKind.Critical => $"{amount:N0}!",
            _ => $"{amount:N0}",
        };
        Color color = kind switch
        {
            DamageKind.Heal => UIMotionFx.Green,
            DamageKind.Critical => UIMotionFx.Gold2,
            _ => Color.white,
        };
        float size = kind == DamageKind.Critical ? 78f : 52f;
        var label = UIMotionFx.NewText("MotionDamage", layer, text, size, color, new Vector2(400f, 120f));
        label.fontSharedMaterial = UIMotionFx.OutlinedMaterial(label.font);
        var rt = label.rectTransform;
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one * 0.3f;

        // 左右交互に飛ばす（同じ所に重なりにくい）
        serial++;
        float side = (serial & 1) == 0 ? 1f : -1f;
        float dx = kind == DamageKind.Heal ? 0f : side * (40f + (serial * 37 % 40));
        float up = kind == DamageKind.Heal ? 70f : 80f;
        float fall = kind == DamageKind.Heal ? 0f : 60f;
        const float life = 0.8f;

        var seq = DOTween.Sequence();
        seq.Insert(0f, rt.DOScale(kind == DamageKind.Critical ? 1.6f : 1.4f, 0.08f).SetEase(Ease.OutQuad));
        seq.Insert(0.08f, rt.DOScale(1f, 0.12f).SetEase(Ease.OutBack));
        if (kind == DamageKind.Critical)
        {
            rt.localRotation = Quaternion.Euler(0f, 0f, -side * 8f);
            seq.Insert(0.08f, rt.DOLocalRotate(Vector3.zero, 0.2f).SetEase(Ease.OutBack));
        }
        seq.Insert(0f, DOVirtual.Float(0f, 1f, life, t =>
        {
            // 放物線: 上がって（up）から落ちる（fall）
            float y = up * 4f * t * (1f - t) * 0.9f + (kind == DamageKind.Heal ? up * t * 0.4f : -fall * t * t);
            rt.anchoredPosition = pos + new Vector2(dx * t, y);
        }).SetEase(Ease.Linear));
        seq.Insert(life - 0.25f, label.DOFade(0f, 0.25f));
        seq.OnComplete(() => UIMotion.DestroySafe(label.gameObject));
        UIMotion.Run(seq, label.gameObject);
    }
}
