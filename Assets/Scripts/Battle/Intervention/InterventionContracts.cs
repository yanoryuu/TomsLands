using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

// 配信中の介入（Docs/Streaming_Redesign.md §4・§5）でロジック側と UI 側が共有する型。
// ロジック（InterventionPresenter 等）は IInterventionCardView / ISpecialMoveCutIn だけを見て、
// MonoBehaviour の実装（カード・カットイン）には依存しない。

/// <summary>勇者側（カード表）の介入。プレイヤー払いスパチャの3色に対応。</summary>
public enum HeroInterventionType
{
    Heal,     // 緑スパ: 回復
    Skill,    // 青スパ: スキル攻撃（敵タップで対象指定可）
    Special,  // 赤スパ: 必殺技
}

/// <summary>ダンジョン側（カード裏）の介入。</summary>
public enum DungeonInterventionType
{
    Trap,      // 罠を仕掛ける
    Curse,     // 呪い
    Reinforce, // 増援を呼ぶ
    BossBuff,  // ボス強化（1配信1回）
}

public enum InterventionFace
{
    Hero,    // 表: チャット欄＋勇者側
    Dungeon, // 裏: ダンジョン側
}

/// <summary>スパチャの色（YouTube 準拠）。プレイヤー払いは Green / Blue / Red のみ使う。</summary>
public enum SuperChatColor
{
    Blue,
    Cyan,
    Green,
    Yellow,
    Orange,
    Magenta,
    Red,
}

/// <summary>介入ボタン1つの表示状態。</summary>
public readonly struct InterventionButtonState
{
    public readonly int Price;
    /// <summary>利用可能残高が足りるか。</summary>
    public readonly bool Affordable;
    /// <summary>残り回数。-1 は無制限。0 なら押せない。</summary>
    public readonly int UsesLeft;

    public InterventionButtonState(int price, bool affordable, int usesLeft)
    {
        Price = price;
        Affordable = affordable;
        UsesLeft = usesLeft;
    }

    public bool Interactable => Affordable && UsesLeft != 0;
}

/// <summary>チャット欄に積むスパチャカード1枚分。</summary>
public readonly struct SuperChatInfo
{
    public readonly SuperChatColor Color;
    /// <summary>表示上の金額（視聴者スパチャは精算に入らない）。</summary>
    public readonly int Amount;
    /// <summary>添えるコメント。空なら金額だけ。</summary>
    public readonly string Message;
    /// <summary>true=プレイヤー自身の介入スパチャ。</summary>
    public readonly bool IsPlayer;

    public SuperChatInfo(SuperChatColor color, int amount, string message, bool isPlayer)
    {
        Color = color;
        Amount = amount;
        Message = message;
        IsPlayer = isPlayer;
    }
}

/// <summary>カード裏に並べる前列の魔物1体分。</summary>
public readonly struct EnemyBadgeInfo
{
    public readonly Sprite Icon;
    public readonly float Hp01;
    public readonly bool IsBoss;

    public EnemyBadgeInfo(Sprite icon, float hp01, bool isBoss)
    {
        Icon = icon;
        Hp01 = hp01;
        IsBoss = isBoss;
    }
}

/// <summary>裏表フリップの介入カード（View）。</summary>
public interface IInterventionCardView
{
    Observable<HeroInterventionType> OnHeroRequested { get; }
    Observable<DungeonInterventionType> OnDungeonRequested { get; }

    void SetHeroButton(HeroInterventionType type, InterventionButtonState state);
    void SetDungeonButton(DungeonInterventionType type, InterventionButtonState state);

    /// <summary>指示が実行待ちの間は全ボタンを押せなくする（同時に有効な指示は1件）。</summary>
    void SetPending(bool pending);

    /// <summary>クールダウン残り（1=開始直後, 0=明け）。両面共通。</summary>
    void SetCooldown(float remaining01);

    /// <summary>チャット欄（表）にスパチャカードを積む。裏を見ていればタブを跳ねさせて知らせる。</summary>
    void PushSuperChat(SuperChatInfo info);

    /// <summary>裏の前列魔物表示を更新する。</summary>
    void SetEnemies(IReadOnlyList<EnemyBadgeInfo> enemies);

    /// <summary>配信終了時: 入力を止める。</summary>
    void SetInteractable(bool interactable);

    InterventionFace CurrentFace { get; }

    /// <summary>アニメなしで面を切り替える（配信開始時に表へ戻す）。</summary>
    void ShowFaceImmediate(InterventionFace face);

    /// <summary>プレイヤーが押していない介入が発動したことを知らせる（視聴者の赤スパで必殺技が自動で出た等）。</summary>
    void PlayAutoTrigger(HeroInterventionType type);
}

/// <summary>赤スパ必殺技のカットイン（勇者立ち絵＋赤帯）。</summary>
public interface ISpecialMoveCutIn
{
    UniTask PlayAsync(CancellationToken ct);
}
