using UnityEngine;

/// <summary>
/// 介入ボタンのホバー説明（見出し＋本文 1〜2 行）。数値は StreamingInteractionSettings から組み立てる。
/// 効果の中身は InterventionCommands / InterventionCommandQueue.ResolveAttack / InterventionPresenter.OnExecuted と対応。
/// </summary>
public static class InterventionTooltipTexts
{
    public readonly struct Entry
    {
        public readonly string Title;
        public readonly string Body;
        public readonly Color TitleColor;

        public Entry(string title, string body, Color titleColor)
        {
            Title = title;
            Body = body;
            TitleColor = titleColor;
        }
    }

    private static readonly Color HealTitle = new(0.56f, 0.9f, 0.5f, 1f);
    private static readonly Color SkillTitle = new(0.52f, 0.78f, 1f, 1f);
    private static readonly Color SpecialTitle = new(1f, 0.52f, 0.42f, 1f);
    private static readonly Color DungeonTitle = new(0.93f, 0.8f, 1f, 1f);
    private static readonly Color GoldTitle = new(1f, 0.82f, 0.36f, 1f);

    public static Entry Hero(HeroInterventionType type, StreamingInteractionSettings s)
    {
        switch (type)
        {
            case HeroInterventionType.Heal:
            {
                string body = $"勇者のHPを最大HPの{Pct(s.healRatio)}回復する";
                if (s.healHeat > 0f) body += "\n配信が少し盛り上がる";
                return new Entry("回復（緑スパ）", body, HealTitle);
            }
            case HeroInterventionType.Skill:
                return new Entry("スキル（青スパ）",
                    $"勇者の次の攻撃が{Mul(s.skillMultiplier)}倍。敵をクリック（パッドは Y）で狙える\n配信が盛り上がり、武器が売れやすくなる",
                    SkillTitle);
            default:
                return new Entry("必殺技（赤スパ）",
                    $"勇者の次の攻撃が{Mul(s.specialMultiplier)}倍（1配信{s.specialMaxPerStream}回まで）\n配信が大きく盛り上がり、武器が高く売れる",
                    SpecialTitle);
        }
    }

    public static Entry Dungeon(DungeonInterventionType type, StreamingInteractionSettings s)
    {
        switch (type)
        {
            case DungeonInterventionType.Trap:
                return new Entry("罠",
                    $"勇者が次に受ける攻撃に\n最大HPの{Pct(s.trapBonusDamageRatio)}の追加ダメージ（防御無視）",
                    DungeonTitle);
            case DungeonInterventionType.Curse:
                return new Entry("呪い",
                    $"勇者の防御力を{Pct(1f - s.curseDefenseMul)}下げる\n勇者が{s.curseHeroHits}回攻撃を受けるまで続く",
                    DungeonTitle);
            case DungeonInterventionType.Reinforce:
                return new Entry("増援",
                    $"いまのフェーズに魔物を{s.reinforceCount}体追加する",
                    DungeonTitle);
            default:
            {
                string body = $"ボスが強くなる（1配信{s.bossBuffMaxPerStream}回）\n攻撃{Mul(s.bossBuffAttackMul)}倍・被ダメ{Pct(1f - s.bossBuffDamageTakenMul)}減";
                if (s.bossBuffDefensePierce > 0f) body += $"・防御{Pct(s.bossBuffDefensePierce)}無視";
                return new Entry("ボス強化", body, DungeonTitle);
            }
        }
    }

    /// <summary>めくりタブ: いま見ている面から、めくった先の面を説明する。</summary>
    public static Entry Tab(InterventionFace current) => current == InterventionFace.Hero
        ? new Entry("→ ダンジョンへの投資", "めくると魔物側に投資して勇者を邪魔できる", DungeonTitle)
        : new Entry("→ 勇者へのスパチャ", "めくると勇者にスパチャして助けられる", GoldTitle);

    private static string Pct(float ratio) => $"{Mathf.RoundToInt(ratio * 100f)}%";

    private static string Mul(float m) => m.ToString(Mathf.Approximately(m, Mathf.Round(m)) ? "0" : "0.##");
}
