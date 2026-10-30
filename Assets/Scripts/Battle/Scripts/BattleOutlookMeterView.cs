using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 配信中の勝敗メーター（SUMMARY G-5）。「勇者 ━━●━━ 魔王」の綱引きバーで、金（勇者）の塗りが魔王側へ押し込むほど勇者の勝ち見込み。
/// 数字は出さない（店の勇者パネルの見込みと同じ目盛りで、介入・被弾・撃破のたびに針が動く）。
/// 見た目の部品は Awake で自分の子に作る（このオブジェクトの矩形＝バー全体）。値は BattleSceneStarter が入れる。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BattleOutlookMeterView : MonoBehaviour
{
    [Header("見た目")]
    [Tooltip("両端の「勇者」「魔王」の文字（MPLUSRounded1c-Bold SDF）")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("バーの地（未設定なら単色）")]
    [SerializeField] private Sprite trackSprite;
    [SerializeField] private Color trackColor = new(0.23f, 0.14f, 0.08f, 0.95f);
    [SerializeField] private Color heroColor = new(0.91f, 0.64f, 0.24f, 1f);   // 金 #E8A33D
    [SerializeField] private Color demonColor = new(0.72f, 0.45f, 1f, 1f);    // 魔王軍の紫
    [SerializeField] private Color needleColor = new(1f, 0.97f, 0.88f, 1f);
    [SerializeField] private float labelWidth = 64f;
    [SerializeField] private float labelFontSize = 24f;
    [SerializeField] private float barHeight = 16f;
    [SerializeField] private float needleSize = 30f;
    [Tooltip("針の移動にかける秒")]
    [SerializeField] private float moveSeconds = 0.45f;

    private RectTransform _root;
    private RectTransform _bar;
    private Image _heroFill;
    private Image _demonFill;
    private RectTransform _needle;
    private Image _trail;
    private bool _built;
    private float _value = -1f;
    private Tween _tween;

    private void Awake()
    {
        Build();
        SetImmediate(0.5f);
    }

    private void OnDestroy() => _tween?.Kill();

    /// <summary>勇者の勝ち見込み（0〜1）を入れる。前回と同じなら何もしない。</summary>
    public void SetValue(float heroWin01)
    {
        if (!Build()) return;
        heroWin01 = Mathf.Clamp01(heroWin01);
        if (_value >= 0f && Mathf.Abs(heroWin01 - _value) < 0.005f) return;
        bool firstTime = _value < 0f;
        float from = firstTime ? heroWin01 : _value;
        _value = heroWin01;
        _tween?.Kill();
        if (firstTime || !isActiveAndEnabled) { Apply(heroWin01); return; }
        _tween = DOVirtual.Float(from, heroWin01, moveSeconds * GameSettings.EffectDurationScale, Apply)
            .SetEase(Ease.OutCubic)
            .SetLink(gameObject);
        UIFx.Pop(_needle, 1.35f, 0.25f);
        PlayTrail(from, heroWin01);
    }

    /// <summary>
    /// #10 ゲージの残像。針が動いた区間（前の位置〜新しい位置）に白い残像を残し、0.4 秒待ってから 0.5 秒で新しい位置へ縮める。
    /// 綱引きバーは勇者・魔王の 2 枚の塗りで TrailGauge（1 枚の塗りの後ろに描く）では隠れてしまうため、塗りの上・針の下に描く。
    /// 省略モードでは出さない。
    /// </summary>
    private void PlayTrail(float from, float to)
    {
        if (_trail == null) return;
        var go = _trail.gameObject;
        DOTween.Kill(go);
        if (UIMotion.IsSkip || !isActiveAndEnabled || Mathf.Abs(to - from) < 0.01f)
        {
            go.SetActive(false);
            return;
        }
        var rt = _trail.rectTransform;
        float near = to;
        float far = from;
        SetTrailSpan(rt, near, far);
        var c = _trail.color;
        c.a = 0.55f;
        _trail.color = c;
        go.SetActive(true);

        var seq = DOTween.Sequence().SetId(go);
        seq.Insert(0.4f, DOVirtual.Float(far, near, 0.5f, x => SetTrailSpan(rt, near, x)).SetEase(Ease.InOutQuad));
        seq.Insert(0.6f, _trail.DOFade(0f, 0.3f));
        seq.OnComplete(() => { if (go != null) go.SetActive(false); });
        seq.OnKill(() => { if (go != null) go.SetActive(false); });
        UIMotion.Run(seq, go);
    }

    private static void SetTrailSpan(RectTransform rt, float a, float b)
    {
        rt.anchorMin = new Vector2(Mathf.Min(a, b), 0f);
        rt.anchorMax = new Vector2(Mathf.Max(a, b), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public void SetImmediate(float heroWin01)
    {
        if (!Build()) return;
        _tween?.Kill();
        _value = Mathf.Clamp01(heroWin01);
        Apply(_value);
    }

    /// <summary>針の位置とバーの塗り分け。針より左（勇者側）が金、右（魔王側）が紫。</summary>
    private void Apply(float heroWin01)
    {
        // 勇者の見込みが高いほど針は魔王側（右）へ押し込む＝勇者の色が広がる
        float x = Mathf.Clamp01(heroWin01);
        _heroFill.rectTransform.anchorMax = new Vector2(x, 1f);
        _demonFill.rectTransform.anchorMin = new Vector2(x, 0f);
        _needle.anchorMin = _needle.anchorMax = new Vector2(x, 0.5f);
        _needle.anchoredPosition = Vector2.zero;
    }

    private bool Build()
    {
        if (_built) return true;
        _root = transform as RectTransform;
        if (_root == null) return false;

        MakeLabel("Hero", "勇者", 0f, heroColor);
        MakeLabel("Demon", "魔王", 1f, demonColor);

        var barGo = new GameObject("Bar", typeof(RectTransform));
        _bar = barGo.GetComponent<RectTransform>();
        _bar.SetParent(_root, false);
        _bar.anchorMin = new Vector2(0f, 0.5f);
        _bar.anchorMax = new Vector2(1f, 0.5f);
        _bar.offsetMin = new Vector2(labelWidth, -barHeight * 0.5f);
        _bar.offsetMax = new Vector2(-labelWidth, barHeight * 0.5f);

        var track = NewImage("Track", _bar, trackSprite, trackColor);
        Stretch(track.rectTransform, -3f);

        _heroFill = NewImage("HeroFill", _bar, trackSprite, heroColor);
        Stretch(_heroFill.rectTransform, 0f);
        _demonFill = NewImage("DemonFill", _bar, trackSprite, demonColor);
        Stretch(_demonFill.rectTransform, 0f);
        // #10 残像（塗りの上・針の下。普段は隠す）
        _trail = NewImage("OutlookTrail", _bar, null, Color.white);
        _trail.gameObject.SetActive(false);

        var needle = NewImage("Needle", _bar, MakeCircleSprite(), needleColor);
        _needle = needle.rectTransform;
        _needle.sizeDelta = new Vector2(needleSize, needleSize);
        _needle.pivot = new Vector2(0.5f, 0.5f);
        var ring = NewImage("Ring", _needle, MakeCircleSprite(), trackColor);
        Stretch(ring.rectTransform, 3f);
        ring.transform.SetAsFirstSibling();
        var inner = NewImage("Inner", _needle, MakeCircleSprite(), needleColor);
        Stretch(inner.rectTransform, -4f);

        foreach (var g in GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        _built = true;
        return true;
    }

    private void MakeLabel(string name, string text, float anchorX, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_root, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = labelFontSize;
        t.alignment = anchorX < 0.5f ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = color;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(anchorX, 0f);
        rt.anchorMax = new Vector2(anchorX, 1f);
        rt.pivot = new Vector2(anchorX, 0.5f);
        rt.sizeDelta = new Vector2(labelWidth, 0f);
        rt.anchoredPosition = Vector2.zero;
    }

    private static Image NewImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private static Sprite _circle;

    private static Sprite MakeCircleSprite()
    {
        if (_circle != null) return _circle;
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        float r = s * 0.5f - 1f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
        }
        tex.Apply();
        _circle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        return _circle;
    }
}
