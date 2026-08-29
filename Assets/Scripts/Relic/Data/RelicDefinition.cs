using System.Collections.Generic;
using UnityEngine;

public enum RelicRarity
{
    Common,
    Rare,
    Epic,
}

/// <summary>
/// レリックのカテゴリ（表示・将来のフェーズ別枠制用）。v1 では制約に使わない。
/// </summary>
public enum RelicCategory
{
    Any,
    Event,
    Procurement,
    Display,
    Sales,
}

/// <summary>
/// レリック（装備アイテム）のマスターデータ。
/// 数値補正（modifiers）はデータだけで完結し、特殊効果は behaviours の behaviourKey で
/// C#実装（RelicBehaviourRegistry）を参照するハイブリッド方式。
/// 本体はラン中限定（ランが終わると消滅）。メタ層（解禁プール/スターター）は別レイヤー。
/// </summary>
[CreateAssetMenu(fileName = "RelicDefinition", menuName = "ScriptableObjects/Relic/RelicDefinition")]
public class RelicDefinition : ScriptableObject
{
    public string relicId;
    public string relicName;
    [TextArea] public string description;
    public Sprite icon;
    public RelicRarity rarity = RelicRarity.Common;
    public RelicCategory category = RelicCategory.Any;
    [Tooltip("呪い（デメリット持ち）。報酬抽選プールから除外され、イベント等でのみ付与される")]
    public bool isCurse;

    [Header("数値補正（常時パッシブ）")]
    public List<RelicModifier> modifiers = new();

    [Header("特殊効果（C#実装への参照）")]
    public List<RelicBehaviourRef> behaviours = new();
}
