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
    public Subject<DungeonName> OnMapIcon { get; private set;} = new();
    public Subject<Unit> OnBackRequested { get; private set;} = new();

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
                FindSlot(d.Key)?.label?.Set(d);
                if (d.IsNext) next = d;
            }
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
        var outline = icon.GetComponent<MapIconOutline>();
        if (outline) outline.EffectColor = cleared ? new Color(color.r, color.g, color.b, 1f) : Color.white;
    }
}
