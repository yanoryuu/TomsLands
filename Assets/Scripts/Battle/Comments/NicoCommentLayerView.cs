using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ニコニコ式コメントの表示層（View）。戦闘ビューポートを覆う RectTransform の上で、
/// 流れコメントを右→左へ等時間で流し、上下固定コメントを中央に出す。
/// コメントは入力を受けない（敵タップ・売り場ドラッグを邪魔しない）。
/// 移動は Update で一括（ポーズ・同時数十件でも軽い）。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class NicoCommentLayerView : MonoBehaviour
{
    [Header("表示")]
    [Tooltip("白文字＋黒縁の SDF フォント（MPLUSRounded1c-Bold SDFPlusPadding など）")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("黒縁つきのマテリアルプリセット。未設定ならフォント既定マテリアル")]
    [SerializeField] private Material fontMaterial;
    [SerializeField] private float flowDuration = 4f;
    [SerializeField] private float fixedDuration = 3f;
    [SerializeField] private float lineHeight = 50f;
    [SerializeField] private float topPadding = 56f;
    [SerializeField] private float bottomPadding = 8f;
    [SerializeField] private float fontSizeSmall = 30f;
    [SerializeField] private float fontSizeMedium = 38f;
    [SerializeField] private float fontSizeBig = 54f;
    [Tooltip("文字の不透明度（ニコニコは不透明。戦闘を見やすくしたいなら少し下げる）")]
    [Range(0.3f, 1f)] [SerializeField] private float textAlpha = 0.95f;

    [Header("上限")]
    [SerializeField] private int maxConcurrent = 40;
    [SerializeField] private int prewarm = 16;

    private const string DensityPrefsKey = "StreamingComment.Density";

    private class Item
    {
        public RectTransform Rect;
        public TextMeshProUGUI Label;
        public bool Active;
        public bool Fixed;
        public float Speed;
        public float Width;
        public float EndTime;
        public int Priority;
        public float SpawnTime;
    }

    private readonly List<Item> _items = new();
    private RectTransform _area;
    private NicoLaneAllocator _lanes;
    private NicoFixedLaneAllocator _topLanes;
    private NicoFixedLaneAllocator _bottomLanes;
    private float _clock;
    private bool _paused;
    private int _fewCounter;
    private Vector2 _lastSize;

    public NicoDensity Density { get; private set; } = NicoDensity.On;
    public int ActiveCount { get; private set; }

    private void Awake()
    {
        _area = (RectTransform)transform;
        if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        try { Density = (NicoDensity)Mathf.Clamp(PlayerPrefs.GetInt(DensityPrefsKey, 0), 0, 2); }
        catch { Density = NicoDensity.On; }

        RebuildLanes();
        for (int i = 0; i < prewarm; i++) CreateItem();
    }

    private void RebuildLanes()
    {
        var size = _area.rect.size;
        _lastSize = size;
        float usable = Mathf.Max(lineHeight, size.y - topPadding - bottomPadding);
        int laneCount = Mathf.Max(1, Mathf.FloorToInt(usable / lineHeight));
        _lanes = new NicoLaneAllocator(laneCount, Mathf.Max(1f, size.x), flowDuration);
        _topLanes = new NicoFixedLaneAllocator(Mathf.Max(1, laneCount / 3), fixedDuration);
        _bottomLanes = new NicoFixedLaneAllocator(Mathf.Max(1, laneCount / 3), fixedDuration);
    }

    // ─────────────────────────────────────────
    //  公開 API
    // ─────────────────────────────────────────

    public void Show(in StreamingCommentRequest req)
    {
        if (!isActiveAndEnabled || string.IsNullOrEmpty(req.Text)) return;
        if (Density == NicoDensity.Off) return;
        if (Density == NicoDensity.Few && req.Priority < 3 && (_fewCounter++ % 3) != 0) return;

        if (_area.rect.size != _lastSize) RebuildLanes();

        // 同じ固定コメントが表示中なら重ねない
        if (req.Position != NicoCommentPosition.Flow)
            foreach (var it in _items)
                if (it.Active && it.Fixed && it.Label.text == req.Text) return;

        var item = Acquire(req.Priority);
        if (item == null) return;

        var label = item.Label;
        label.text = req.Text;
        label.fontSize = req.Size switch
        {
            NicoCommentSize.Small => fontSizeSmall,
            NicoCommentSize.Big => fontSizeBig,
            _ => fontSizeMedium,
        };
        var c = req.Color;
        c.a = textAlpha;
        label.color = c;

        float width = Mathf.Ceil(label.GetPreferredValues(req.Text, 4000f, lineHeight * 2f).x) + 8f;
        float height = Mathf.Max(lineHeight, label.fontSize * 1.3f);
        item.Rect.sizeDelta = new Vector2(width, height);
        item.Width = width;
        item.Priority = req.Priority;
        item.SpawnTime = _clock;
        item.Active = true;
        item.Rect.gameObject.SetActive(true);
        item.Rect.localScale = Vector3.one;

        var areaSize = _area.rect.size;
        if (req.Position == NicoCommentPosition.Flow)
        {
            int lane = _lanes.Allocate(_clock, width);
            item.Fixed = false;
            item.Speed = _lanes.SpeedFor(width);
            item.EndTime = _clock + flowDuration;
            float y = -topPadding - lane * lineHeight - lineHeight * 0.5f;
            if (req.Size == NicoCommentSize.Big) y -= (height - lineHeight) * 0.5f;
            item.Rect.anchoredPosition = new Vector2(areaSize.x, y);
        }
        else
        {
            item.Fixed = true;
            item.Speed = 0f;
            item.EndTime = _clock + fixedDuration;
            float y;
            if (req.Position == NicoCommentPosition.Top)
                y = -topPadding - _topLanes.Allocate(_clock) * lineHeight - lineHeight * 0.5f;
            else
                y = -areaSize.y + bottomPadding + _bottomLanes.Allocate(_clock) * lineHeight + lineHeight * 0.5f;
            item.Rect.anchoredPosition = new Vector2((areaSize.x - width) * 0.5f, y);
            UIFx.Pop(item.Rect, 0.6f, 0.18f);
        }
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (paused) DOTween.Pause(this); else DOTween.Play(this);
    }

    public void SetDensity(NicoDensity density)
    {
        Density = density;
        try { PlayerPrefs.SetInt(DensityPrefsKey, (int)density); } catch { /* PlayerPrefs 不可でも表示は続ける */ }
        if (density == NicoDensity.Off) ClearAll();
    }

    /// <summary>ON → 少なめ → OFF の順に切り替える（ボタン用）。</summary>
    public NicoDensity CycleDensity()
    {
        SetDensity((NicoDensity)(((int)Density + 1) % 3));
        return Density;
    }

    public void ClearAll()
    {
        foreach (var it in _items) Release(it);
    }

    // ─────────────────────────────────────────
    //  内部
    // ─────────────────────────────────────────

    private void Update()
    {
        if (_paused) return;
        float dt = Time.deltaTime;
        _clock += dt;

        int active = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            if (!it.Active) continue;
            if (_clock >= it.EndTime) { Release(it); continue; }
            active++;
            if (it.Fixed) continue;
            var p = it.Rect.anchoredPosition;
            p.x -= it.Speed * dt;
            it.Rect.anchoredPosition = p;
        }
        ActiveCount = active;
    }

    private Item Acquire(int priority)
    {
        Item free = null;
        Item victim = null;
        int active = 0;
        foreach (var it in _items)
        {
            if (!it.Active) { free ??= it; continue; }
            active++;
            // 捨てる候補: 優先度が最も低く、最も古いもの
            if (victim == null || it.Priority < victim.Priority || (it.Priority == victim.Priority && it.SpawnTime < victim.SpawnTime))
                victim = it;
        }

        if (active >= maxConcurrent)
        {
            if (victim == null || victim.Priority > priority) return null;
            Release(victim);
            return victim;
        }
        return free ?? CreateItem();
    }

    private void Release(Item it)
    {
        if (!it.Active && !it.Rect.gameObject.activeSelf) return;
        it.Active = false;
        it.Rect.DOKill();
        it.Rect.gameObject.SetActive(false);
    }

    private Item CreateItem()
    {
        var go = new GameObject("NicoComment", typeof(RectTransform));
        go.layer = gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(_area, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);

        var label = go.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        if (fontMaterial != null) label.fontSharedMaterial = fontMaterial;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.richText = false;
        label.fontSize = fontSizeMedium;

        go.SetActive(false);
        var item = new Item { Rect = rect, Label = label };
        _items.Add(item);
        return item;
    }
}
