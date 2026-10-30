using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class MapView : MonoBehaviour
{
    [Header("ダンジョン")]
    [SerializeField] private Button mausoleumOblivion;
    [SerializeField] private Button scorchingVolcanoPrison;
    [SerializeField] private Button iceMistCave;
    [SerializeField] private Button deepGreenBeastForest;
    [SerializeField] private Button ancientMechanicalCastle;
    [SerializeField] private Button demonKingsCastle;

    /// <summary>ダンジョンごとの名札と勇者キャラの立ち位置（アイコン中心からのずれ）。</summary>
    [Serializable]
    private class DungeonLabelSlot
    {
        public DungeonName key;
        public MapDungeonLabel label;
        public Vector2 heroOffset = new(-150f, -20f);
    }

    [Header("名札・次の行き先")]
    [SerializeField] private List<DungeonLabelSlot> labelSlots = new();
    [SerializeField] private MapHeroMarker heroMarker;
    [SerializeField] private Color clearedIconColor = new(0.55f, 0.55f, 0.55f, 1f);

    [Header("UI")]
    [SerializeField] private Button backButton;
    public Subject<DungeonName> OnMapIcon { get; } = new();
    public Subject<Unit> OnBackRequested { get; } = new();

    // DungeonName → アイコンのボタン
    private readonly Dictionary<DungeonName, Button> icons = new();

    private void Awake()
    {
        BuildIconTable();
        Bind();
    }

    private void BuildIconTable()
    {
        icons.Clear();
        AddIcon(DungeonName.MausoleumOblivion, mausoleumOblivion);
        AddIcon(DungeonName.ScorchingVolcanoPrison, scorchingVolcanoPrison);
        AddIcon(DungeonName.IceMistCave, iceMistCave);
        AddIcon(DungeonName.DeepGreenBeastForest, deepGreenBeastForest);
        AddIcon(DungeonName.AncientMechanicalCastle, ancientMechanicalCastle);
        AddIcon(DungeonName.DemonKingCastle, demonKingsCastle);
    }

    private void AddIcon(DungeonName key, Button button)
    {
        if (button != null) icons[key] = button;
    }

    private void Bind()
    {
        foreach (var pair in icons)
        {
            var key = pair.Key;
            pair.Value.onClick.AddListener(() => OnMapIcon.OnNext(key));
        }
        if (backButton) backButton.onClick.AddListener(() => OnBackRequested.OnNext(Unit.Default));
    }

    /// <summary>
    /// 全ダンジョンの名札・状態・次の行き先を描き直す（地図に入るたびに呼ぶ）。
    /// </summary>
    public void SetDungeons(IReadOnlyList<MapDungeonDisplay> displays)
    {
        MapDungeonDisplay next = null;

        if (displays != null)
        {
            foreach (var d in displays)
            {
                if (d == null) continue;
                if (icons.TryGetValue(d.Key, out var button)) SetDungeonIcon(button, d.Status);
                var labelSlot = FindSlot(d.Key);
                if (labelSlot != null && labelSlot.label != null) labelSlot.label.Set(d);
                if (d.IsNext) next = d;
            }
        }

        // パッド/キー: 開いたときは次に挑むダンジョンを選ばせる
        if (next != null && icons.TryGetValue(next.Key, out var nextButton))
        {
            var scope = GetComponentInParent<UIFocusScope>();
            if (scope != null) scope.FirstSelected = nextButton;
        }

        // 勇者キャラは次の1ダンジョンにだけ立たせる
        if (heroMarker == null) return;
        if (next != null && icons.TryGetValue(next.Key, out var target))
        {
            var slot = FindSlot(next.Key);
            heroMarker.Show(target.transform as RectTransform, slot != null ? slot.heroOffset : new Vector2(-150f, -20f));
        }
        else
        {
            heroMarker.Hide();
        }
    }

    private DungeonLabelSlot FindSlot(DungeonName key)
    {
        foreach (var s in labelSlots)
        {
            if (s != null && s.key == key) return s;
        }
        return null;
    }

    private void SetDungeonIcon(Button icon, DungeonStatus status)
    {
        // アイコン自体は動かさない（揺れ演出は廃止。動きは勇者キャラだけ）
        bool cleared = status == DungeonStatus.Clear;
        var color = cleared ? clearedIconColor : Color.white;

        if (icon.targetGraphic) icon.targetGraphic.color = color;
        if (icon.TryGetComponent<MapIconOutline>(out var outline)) outline.EffectColor = cleared ? new Color(color.r, color.g, color.b, 1f) : Color.white;
    }

    // ───────── 配信の予定表（領主館 Lv5。Docs/Map_Overlay_Spec.md R6） ─────────

    [Header("配信の予定表（領主館 Lv5）")]
    [Tooltip("予定の名札を横一列に並べる帯（地図の上の余白）。未配線なら予定表は出さない")]
    [SerializeField] private RectTransform scheduleStrip;
    [Tooltip("予定1件ぶんの名札（地図の名札 MapDungeonLabel を縮めて使う）")]
    [SerializeField] private MapDungeonLabel scheduleRowPrefab;
    [SerializeField, Range(0.3f, 1f)] private float scheduleRowMaxScale = 0.62f;
    [SerializeField] private float scheduleRowSpacing = 12f;

    private readonly List<GameObject> scheduleRows = new();

    /// <summary>配信の予定を、配信する順に左から並べる。null か空なら帯ごと隠す（領主館が Lv5 でないとき）。</summary>
    public void SetStreamSchedule(IReadOnlyList<MapScheduleRow> rows)
    {
        if (scheduleStrip == null) return;

        foreach (var go in scheduleRows)
            if (go != null) Destroy(go);
        scheduleRows.Clear();

        bool show = rows != null && rows.Count > 0 && scheduleRowPrefab != null;
        scheduleStrip.gameObject.SetActive(show);
        if (!show) return;

        // 名札の元の大きさを、帯の幅に全件が収まるまで縮める（配信6回なら 0.62、8回なら約 0.52）
        var plateSize = ((RectTransform)scheduleRowPrefab.transform).sizeDelta;
        int n = rows.Count;
        float width = scheduleStrip.rect.width;
        float fit = width > 0f ? (width - scheduleRowSpacing * (n - 1)) / (n * Mathf.Max(1f, plateSize.x)) : scheduleRowMaxScale;
        float scale = Mathf.Clamp(fit, 0.3f, scheduleRowMaxScale);
        float step = plateSize.x * scale + scheduleRowSpacing;

        for (int i = 0; i < n; i++)
        {
            var row = rows[i];
            if (row == null) continue;
            var label = Instantiate(scheduleRowPrefab, scheduleStrip);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((i - (n - 1) * 0.5f) * step, 0f);
            rt.localScale = Vector3.one * scale;
            label.SetScheduleRow(row.Name, row.DayText, row.StatusText, row.IsPast);
            scheduleRows.Add(label.gameObject);
        }

        // #3 配信の予定の札を左から順に出す
        var plates = new List<RectTransform>();
        foreach (var go in scheduleRows) if (go != null && go.transform is RectTransform rt) plates.Add(rt);
        if (scheduleStrip.gameObject.activeInHierarchy) UIMotion.Stagger(plates, 0.06f, 0f, 0.25f, 0.15f);
    }
}

/// <summary>配信の予定表の1件ぶんの表示データ（MapPresenter が文字列まで組み立てる）。</summary>
public class MapScheduleRow
{
    public string Name;       // ダンジョン名
    public string DayText;    // "5日目"
    public string StatusText; // "あと3日" / "本日配信" / "済"（次の配信には「次」を付ける）
    public bool IsPast;
}
