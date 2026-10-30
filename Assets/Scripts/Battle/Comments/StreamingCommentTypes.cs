using UnityEngine;

/// <summary>配信コメント（ニコニコ式）を出すきっかけ。CSV の trigger 列はこの名前で書く。</summary>
public enum StreamingCommentTrigger
{
    BattleStart,
    Idle,           // 平常時の雑談
    HeroAttack,     // 勇者の攻撃（通常）
    HeroCritical,   // 勇者の攻撃で敵HPを大きく削った
    HeroDamaged,    // 勇者が被弾
    HeroPinch,      // 勇者のHPが3割を切った
    EnemyDefeated,
    BossAppeared,
    BossDefeated,
    WaveCleared,
    ItemSold,
    StockDepleted,
    HeatUp,
    HeatDown,
    Victory,
    Defeat,
    // ── 介入・スパチャ（Docs/Streaming_Redesign.md §4・§5）──
    SuperChat,          // 視聴者スパチャ（青〜桃）への反応
    RedSuperChat,       // 赤スパへの反応（大弾幕）
    InterventionHeal,   // 緑スパ: 回復
    InterventionSkill,  // 青スパ: スキル攻撃
    SpecialMove,        // 赤スパ: 必殺技（「うおおおお」弾幕）
    DungeonTrap,
    DungeonCurse,
    DungeonReinforce,
    DungeonBossBuff,
    // ── 賭けの立場・相場（SUMMARY E-8）。CSV の trigger 列はこの名前。末尾に足す ──
    PriceSpike,         // 配信中に相場が急に上がった（{item} {price} {pct}）
    BetOnHero,          // 配信開始: 店長が勇者の装備を抱えて勇者に張っている
    BetOnDemon,         // 配信開始: 店長が魔王軍に出資して魔王側に張っている
    VictoryBetDemon,    // 決着: 魔王側に張ったのに勇者が勝った
    DefeatBetDemon,     // 決着: 魔王側に張って当たった（配当確定）
    DefeatBetHero,      // 決着: 勇者に張って外れた
    DemonSuperChat,     // 視聴者の魔王軍スパ（紫）への反応
}

/// <summary>この配信でプレイヤー（店長）がどちらに張っているか。配信コメントの出し分けに使う。</summary>
public enum StreamingBetStance { None, Hero, Demon }

public enum NicoCommentSize { Small, Medium, Big }

/// <summary>Flow = 右→左に流れる / Top・Bottom = 上下中央に固定（ニコニコの ue / shita）。</summary>
public enum NicoCommentPosition { Flow, Top, Bottom }

/// <summary>コメント表示量のプレイヤー設定。</summary>
public enum NicoDensity { On, Few, Off }

/// <summary>バズ状態による出し分け条件（CSV の buzz 列）。</summary>
public enum StreamingCommentBuzzCondition { Any, Buzz, SuperBuzz, Flame }

/// <summary>コメント1件の表示要求。</summary>
public readonly struct StreamingCommentRequest
{
    public readonly string Text;
    public readonly Color Color;
    public readonly NicoCommentSize Size;
    public readonly NicoCommentPosition Position;
    /// <summary>大きいほど優先（同時表示上限を超えたとき低い方を捨てる）。</summary>
    public readonly int Priority;
    /// <summary>スパチャ（投げ銭）のコメントか。表示側で枠付きに強調する（#15。見た目だけ）。</summary>
    public readonly bool IsSuperChat;

    public StreamingCommentRequest(string text, Color color, NicoCommentSize size, NicoCommentPosition position, int priority,
        bool isSuperChat = false)
    {
        Text = text;
        Color = color;
        Size = size;
        Position = position;
        Priority = priority;
        IsSuperChat = isSuperChat;
    }
}
