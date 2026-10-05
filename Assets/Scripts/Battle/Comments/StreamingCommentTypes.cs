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
}

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

    public StreamingCommentRequest(string text, Color color, NicoCommentSize size, NicoCommentPosition position, int priority)
    {
        Text = text;
        Color = color;
        Size = size;
        Position = position;
        Priority = priority;
    }
}
