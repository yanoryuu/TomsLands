using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// クリア確率（クリア見込み）の計算。
///
/// 実際の戦闘（BattleFlowManager/BattleActionExecutor）は
/// ダメージ = max(1, 攻撃力 - 防御力) の決定論シミュレーションのため、
/// ここでも同じルールで戦闘をドライランし、その結果から表示用の％を作る。
/// ・勝てる想定 → 55〜95%（残りHPが多いほど高い）
/// ・負ける想定 → 5〜45%（倒せた敵が多いほど高い）
/// つまり 50% 以上が表示されていれば現在のステータスでは勝てる見込み、という整合が保証される
/// （以前はレベル差ヒューリスティックだったため「95%表示で敗北」が起こり得た）。
///
/// 画面には％を出さず、<see cref="ToOutlook"/> の5段階（楽勝／有利／互角／不利／絶望）で出す
/// （戦闘は決定論なので「60%」は実際には「ほぼ勝つ」で、確率として読むと誤解されるため）。
/// 段階の境目は上の変換を逆算して、ドライランの勝敗と残りHP・撃破数に対応させている。
/// ・楽勝: 勝つ想定で、勇者の残りHPが 50% 以上（75% 以上）
/// ・有利: 勝つ想定で、残りHP 20〜50%（63〜75%）
/// ・互角: 勝つ想定でも残りHP 20% 未満（55〜63%）／負ける想定でも敵の 75% 以上を倒す（35〜45%）
///         … 回復（最大HPの30%）・罠・増援など介入1回でひっくり返り得る幅
/// ・不利: 負ける想定で、倒せる敵が 25〜75%（15〜35%）
/// ・絶望: 負ける想定で、倒せる敵が 25% 未満（15% 未満）
/// 属性は戦闘に効かせる予定だが、この計算には入れない（2026-10 の決定）。
/// </summary>
public static class ClearProbabilityCalculator
{
    /// <summary>BattleSequencer.maxConcurrentEnemies と同値（同時出現の最大数）。</summary>
    private const int MaxConcurrentEnemies = 3;

    /// <summary>ドライランの最大ターン数（お互い決定打がない場合の無限ループ保険）。</summary>
    private const int MaxSimTurns = 500;

    public static float Calculate(
        RuntimeHeroData hero,
        ItemModel itemModel,
        DungeonData dungeon,
        int dungeonLevel,
        RelicEffectResolver relicResolver = null)
    {
        if (hero == null || dungeon == null) return 50f;

        // レリックの勇者弱体化（HeroPowerMul）は実戦闘（CharacterModel）で掛かるので表示にも掛ける
        // （以前は掛けておらず「表示 60% なのに負ける」が起きていた。Jev AutoPlay の clearprob_mismatch で検出）
        float powerMul = relicResolver != null ? Mathf.Max(0.1f, relicResolver.Modify(RelicStatId.HeroPowerMul, 1f)) : 1f;

        var phases = BuildPhases(dungeon, dungeonLevel);
        if (phases.Count == 0)
        {
            // フェーズ構成が取れない場合は旧ヒューリスティックにフォールバック
            return CalculateLegacy(hero, dungeon);
        }

        return Simulate(hero, phases, HeroEquipmentBonus.Get(hero, itemModel), powerMul);
    }

    /// <summary>実戦闘（BattleContext.InitializePhases）と同じ優先順でフェーズ構成を作る。</summary>
    private static List<DungeonPhaseData> BuildPhases(DungeonData dungeon, int dungeonLevel)
    {
        var level = dungeon.GetLevelData(dungeonLevel);
        if (level == null) return new List<DungeonPhaseData>();

        List<DungeonPhaseData> phases;
        if (level.phases != null && level.phases.Any(p => p?.enemies != null && p.enemies.Any(e => e != null)))
        {
            phases = level.phases;
        }
        else
        {
            phases = DungeonPhaseBuilder.BuildFromLegacy(level);
        }

        // 実戦闘と同じく通常ウェーブの周回（normalWaveRepeat）を展開する
        return BattleWavePlan.Expand(phases
            .Where(p => p?.enemies != null && p.enemies.Any(e => e != null))
            .ToList());
    }

    /// <summary>
    /// BattleActionExecutor と同じルールで戦闘をドライランし、表示用の％へ変換する。
    /// ・ターン順: 勇者 → 出現順の敵（そのターン中に倒された敵は行動しない）
    /// ・勇者は生存している先頭の敵を攻撃、敵は全員勇者を攻撃
    /// ・勇者死亡でそのターンの残りの敵は行動中断（実装の IsBattleEnded() break と同じ）
    /// ・ターン終了時: 死亡除去 → フェーズ全滅なら次フェーズ → 同時3体まで補充
    /// </summary>
    private static float Simulate(RuntimeHeroData hero, List<DungeonPhaseData> phases, HeroEquipmentBonus.Multipliers equip, float powerMul = 1f)
    {
        var stats = ComputeStats(hero, equip, powerMul);
        int heroMaxHp = stats.Hp;
        int heroAtk   = stats.Attack;
        int heroDef   = stats.Defense;
        int heroHp    = heroMaxHp;

        int totalEnemies = phases.Sum(p => p.enemies.Count(e => e != null));
        int defeated = 0;

        int phaseIndex = 0;
        var spawnQueue = new Queue<EnemyData>(phases[0].enemies.Where(e => e != null));
        // (残りHP, 攻撃力, 防御力)
        var field = new List<(int hp, int atk, int def)>();

        void Refill()
        {
            while (field.Count < MaxConcurrentEnemies && spawnQueue.Count > 0)
            {
                var e = spawnQueue.Dequeue();
                field.Add((Mathf.Max(1, e.hp), Mathf.Max(0, e.attackPower), Mathf.Max(0, e.defensePower)));
            }
        }

        Refill(); // 開幕スポーン（BattleFlowManager の初回 SpawnFromPhaseQueueAsync 相当）

        for (int turn = 0; turn < MaxSimTurns; turn++)
        {
            // --- 勇者の攻撃（先頭の生存敵） ---
            if (field.Count > 0)
            {
                var target = field[0];
                target.hp -= Mathf.Max(1, heroAtk - target.def);
                field[0] = target;
            }

            // --- 敵の攻撃（このターン中に倒された敵は行動しない） ---
            foreach (var enemy in field)
            {
                if (enemy.hp <= 0) continue;
                heroHp -= Mathf.Max(1, enemy.atk - heroDef);
                if (heroHp <= 0) break; // 勇者死亡で残りは行動中断
            }

            if (heroHp <= 0) break;

            // --- ターン終了評価 ---
            defeated += field.Count(e => e.hp <= 0);
            field.RemoveAll(e => e.hp <= 0);

            if (field.Count == 0 && spawnQueue.Count == 0)
            {
                phaseIndex++;
                if (phaseIndex >= phases.Count) break; // 全フェーズクリア＝勝利

                foreach (var e in phases[phaseIndex].enemies.Where(e => e != null))
                    spawnQueue.Enqueue(e);
            }

            Refill();
        }

        if (heroHp > 0 && phaseIndex >= phases.Count)
        {
            // 勝利見込み: 残りHP割合が高いほど確率を高く見せる（55〜95%）
            float hpRatio = Mathf.Clamp01(heroHp / (float)heroMaxHp);
            return Mathf.Clamp(55f + hpRatio * 40f, 55f, 95f);
        }

        // 敗北見込み（ターン上限による膠着含む）: 進行度が高いほど「惜しい」表示（5〜45%）
        float progress = totalEnemies > 0 ? Mathf.Clamp01(defeated / (float)totalEnemies) : 0f;
        return Mathf.Clamp(5f + progress * 40f, 5f, 45f);
    }

    /// <summary>戦闘に入る勇者の能力値（装備・レリック込み）。</summary>
    public readonly struct HeroStats
    {
        public readonly int Hp;
        public readonly int Attack;
        public readonly int Defense;

        public HeroStats(int hp, int attack, int defense)
        {
            Hp = hp;
            Attack = attack;
            Defense = defense;
        }
    }

    /// <summary>
    /// 勇者画面に出す「戦うときの攻撃・防御・HP」。クリア見込みのドライランと同じ値を返す。
    /// </summary>
    public static HeroStats GetEffectiveStats(RuntimeHeroData hero, ItemModel itemModel, RelicEffectResolver relicResolver = null)
    {
        if (hero == null) return new HeroStats(0, 0, 0);
        float powerMul = relicResolver != null ? Mathf.Max(0.1f, relicResolver.Modify(RelicStatId.HeroPowerMul, 1f)) : 1f;
        return ComputeStats(hero, HeroEquipmentBonus.Get(hero, itemModel), powerMul);
    }

    /// <summary>
    /// 武器・防具を weaponId / armorId に付け替えたときの「戦うときの攻撃・防御・HP」（勇者画面のドラッグ中の見込み）。
    /// 勇者の装備そのものは変えない。
    /// </summary>
    public static HeroStats GetEffectiveStatsWith(RuntimeHeroData hero, ItemModel itemModel, RelicEffectResolver relicResolver, string weaponId, string armorId)
    {
        if (hero == null) return new HeroStats(0, 0, 0);
        float powerMul = relicResolver != null ? Mathf.Max(0.1f, relicResolver.Modify(RelicStatId.HeroPowerMul, 1f)) : 1f;
        return ComputeStats(hero, HeroEquipmentBonus.Get(weaponId, armorId, itemModel), powerMul);
    }

    private static HeroStats ComputeStats(RuntimeHeroData hero, HeroEquipmentBonus.Multipliers equip, float powerMul)
    {
        int hp  = Mathf.Max(1, Mathf.RoundToInt(hero.hp.Value * equip.Hp));
        int atk = Mathf.Max(1, Mathf.RoundToInt(hero.attackPower.Value * equip.Attack));
        int def = Mathf.Max(0, Mathf.RoundToInt(hero.defensePower.Value * equip.Defense));
        if (!Mathf.Approximately(powerMul, 1f))
        {
            // CharacterModel のコンストラクタと同じ丸め（max(1, round(v × powerMul))）
            hp  = Mathf.Max(1, Mathf.RoundToInt(Mathf.RoundToInt(hero.hp.Value * equip.Hp) * powerMul));
            atk = Mathf.Max(1, Mathf.RoundToInt(Mathf.RoundToInt(hero.attackPower.Value * equip.Attack) * powerMul));
            def = Mathf.Max(1, Mathf.RoundToInt(Mathf.RoundToInt(hero.defensePower.Value * equip.Defense) * powerMul));
        }
        return new HeroStats(hp, atk, def);
    }

    // ─── 5段階の表示（数字は出さない） ───

    /// <summary>段階の境目（表示％）。根拠はクラスの summary を参照。</summary>
    private const float EasyMin = 75f;      // 勝ち・残りHP 50% 以上
    private const float FavorableMin = 63f; // 勝ち・残りHP 20% 以上
    private const float EvenMin = 35f;      // 負けでも敵の 75% 以上を倒す
    private const float UnfavorableMin = 15f; // 負け・敵の 25% 以上を倒す

    /// <summary>表示％ → 5段階。</summary>
    public static ClearOutlook ToOutlook(float probability)
    {
        if (probability >= EasyMin) return ClearOutlook.Easy;
        if (probability >= FavorableMin) return ClearOutlook.Favorable;
        if (probability >= EvenMin) return ClearOutlook.Even;
        if (probability >= UnfavorableMin) return ClearOutlook.Unfavorable;
        return ClearOutlook.Hopeless;
    }

    public static string OutlookLabel(ClearOutlook outlook) => outlook switch
    {
        ClearOutlook.Easy        => "楽勝",
        ClearOutlook.Favorable   => "有利",
        ClearOutlook.Even        => "互角",
        ClearOutlook.Unfavorable => "不利",
        _                        => "絶望",
    };

    /// <summary>段階の色（勇者側から見て 緑→金→赤）。木と金縁のトーンに合わせた落ち着いた色。</summary>
    public static string OutlookColorHex(ClearOutlook outlook) => outlook switch
    {
        ClearOutlook.Easy        => "#3E8E41",
        ClearOutlook.Favorable   => "#6E9E2E",
        ClearOutlook.Even        => "#C98A1E",
        ClearOutlook.Unfavorable => "#C9622B",
        _                        => "#A8332B",
    };

    /// <summary>色付きの段階名（TMP リッチテキスト）。例: 「&lt;color=#6E9E2E&gt;有利&lt;/color&gt;」</summary>
    public static string FormatOutlook(float probability)
    {
        var outlook = ToOutlook(probability);
        return $"<color={OutlookColorHex(outlook)}>{OutlookLabel(outlook)}</color>";
    }

    /// <summary>
    /// フェーズ構成が取得できない場合のみ使う旧ヒューリスティック
    /// （推奨レベル差 ±10%/Lv・難易度補正）。
    /// </summary>
    private static float CalculateLegacy(RuntimeHeroData hero, DungeonData dungeon)
    {
        float recLv      = Mathf.Max(1, dungeon.recommendedLevel);
        float levelDiff  = hero.level.Value - recLv;
        float baseProbability = Mathf.Clamp(60f + levelDiff * 10f, 20f, 90f);

        float diffFactor = 10f / Mathf.Max(1f, dungeon.difficulty + 5f);
        baseProbability *= diffFactor;

        return Mathf.Clamp(baseProbability, 5f, 95f);
    }
}

/// <summary>クリア見込みの5段階（ClearProbabilityCalculator.ToOutlook）。</summary>
public enum ClearOutlook
{
    Hopeless = 0,
    Unfavorable = 1,
    Even = 2,
    Favorable = 3,
    Easy = 4,
}
