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
/// 段階の境目（表示％）は gameconst の clearOutlook.easyMin / favorableMin / evenMin / unfavorableMin
/// （ClearOutlookSettingsData）。店の勇者パネル・マップ・配信の勝敗メーターの段・魔王軍支援のオッズの段（SupportOdds）は
/// みなこの 1 か所の境目を使う。境目は上の変換を逆算して、ドライランの勝敗と残りHP・撃破数に対応させている。
/// 既定（配信に無いとき。2026-10-30 までの動き）: 75 / 63 / 35 / 15
/// ・楽勝: 勝つ想定で、勇者の残りHPが 50% 以上（75% 以上）
/// ・有利: 勝つ想定で、残りHP 20〜50%（63〜75%）
/// ・互角: 勝つ想定でも残りHP 20% 未満（55〜63%）／負ける想定でも敵の 75% 以上を倒す（35〜45%）
///         … 回復（最大HPの30%）・罠・増援など介入1回でひっくり返り得る幅
/// ・不利: 負ける想定で、倒せる敵が 25〜75%（15〜35%）
/// ・絶望: 負ける想定で、倒せる敵が 25% 未満（15% 未満）
/// 予定（2026-10-30 ユーザー決定。リセット時にシートで 75 / 55 / 45 / 15 を入れる）:
/// ・楽勝 75% 以上（勝つ・残りHP 50% 以上）／有利 55〜75%（勝つ想定のそれ以外すべて）
/// ・互角 45〜55%（勝ち負けの境目。上の変換では勝つ想定は 55% 以上・負ける想定は 45% 以下なので、
///   ここに入るのは「負ける想定で進み具合 100%」の 45% ちょうどと、配信中のメーターが間を通るときだけ）
/// ・不利 15〜45%（負ける・敵の 25% 以上を倒す）／絶望 15% 未満
/// 属性は戦闘に効かせる予定だが、この計算には入れない（2026-10 の決定）。
/// GameConst.clearOutlookProgressByEnemyHp = true のときは、負ける側の「倒せる敵の割合」を「削れる敵 HP の割合」に置き換える
/// （魔王の残り HP が見込みに入る。2026-10-30 ユーザー決定。既定 false＝今までどおり）。
/// 敵の構成は DungeonWaveSource（実戦闘と同じ入り口）から取る。
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

    /// <summary>
    /// ドライランに使うウェーブ列。実戦闘（BattleContext.InitializePhases）と同じ入り口 DungeonWaveSource を使う
    /// （配信の dungeonPhases・敵の値・ダンジョンの倍率・normalWaveRepeat まで同じ）。
    /// </summary>
    public static List<DungeonPhaseData> BuildPhases(DungeonData dungeon, int dungeonLevel)
        => DungeonWaveSource.Build(dungeon, dungeonLevel);

    /// <summary>GameConst の clearOutlookProgressByEnemyHp（負ける見込みの進み具合を削った HP で測るか）。</summary>
    public static bool ProgressByEnemyHp => GameConst.Data != null && GameConst.Data.clearOutlookProgressByEnemyHp;

    /// <summary>
    /// 負ける見込みのときの進み具合（0〜1）。
    /// byHp=false: 倒した敵の数 ÷ 敵の数（今まで）。byHp=true: 削った HP ÷ 敵の HP の合計（ボスの残り HP がそのまま効く）。
    /// </summary>
    public static float LossProgress(bool byHp, int defeated, int totalEnemies, long remainingEnemyHp, long totalEnemyHp)
    {
        if (byHp)
            return totalEnemyHp > 0 ? Mathf.Clamp01(1f - remainingEnemyHp / (float)totalEnemyHp) : 0f;
        return totalEnemies > 0 ? Mathf.Clamp01(defeated / (float)totalEnemies) : 0f;
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
        long totalEnemyHp = phases.Sum(p => p.enemies.Where(e => e != null).Sum(e => (long)Mathf.Max(1, e.hp)));
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
        // clearOutlookProgressByEnemyHp なら倒した数ではなく削った HP で測る（場・出現待ち・この先のウェーブの残り HP。魔王の残り HP を含む）
        long remainingHp = field.Sum(e => (long)Mathf.Max(0, e.hp)) + spawnQueue.Sum(e => (long)Mathf.Max(1, e.hp));
        for (int i = phaseIndex + 1; i < phases.Count; i++)
            remainingHp += phases[i].enemies.Where(e => e != null).Sum(e => (long)Mathf.Max(1, e.hp));
        float progress = LossProgress(ProgressByEnemyHp, defeated, totalEnemies, remainingHp, totalEnemyHp);
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

    /// <summary>段階の境目（gameconst の clearOutlook.*）。根拠はクラスの summary を参照。</summary>
    public static ClearOutlookSettingsData Thresholds => GameConst.Data?.clearOutlook ?? DefaultThresholds;

    private static readonly ClearOutlookSettingsData DefaultThresholds = new();

    /// <summary>表示％ → 5段階（境目は Thresholds）。</summary>
    public static ClearOutlook ToOutlook(float probability) => ToOutlook(probability, Thresholds);

    /// <summary>表示％ → 5段階。境目が壊れている（数でない・上の段ほど高くなっていない）ときは既定の境目を使う。</summary>
    public static ClearOutlook ToOutlook(float probability, ClearOutlookSettingsData t)
    {
        if (t == null || !t.IsValid) t = DefaultThresholds;
        if (probability >= t.easyMin) return ClearOutlook.Easy;
        if (probability >= t.favorableMin) return ClearOutlook.Favorable;
        if (probability >= t.evenMin) return ClearOutlook.Even;
        if (probability >= t.unfavorableMin) return ClearOutlook.Unfavorable;
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

/// <summary>
/// クリア見込みの段階の境目（表示％。gameconst の data.clearOutlook.*、シート data に「clearOutlook.キー名」の行で書く）。
/// 既定値は 2026-10-30 時点の境目そのまま（配信に無ければ今と同じ）。リセット時にシートで favorableMin 55・evenMin 45 を入れる予定。
/// 各キーは「その段になる最低の％」（以上でその段）。
/// </summary>
[System.Serializable]
public class ClearOutlookSettingsData
{
    /// <summary>楽勝の下限（既定 75 = 勝つ想定・残りHP 50% 以上）。</summary>
    public float easyMin = 75f;
    /// <summary>有利の下限（既定 63 = 勝つ想定・残りHP 20% 以上。予定 55 = 勝つ想定のすべて）。</summary>
    public float favorableMin = 63f;
    /// <summary>互角の下限（既定 35 = 負けでも敵の 75% 以上を倒す。予定 45）。</summary>
    public float evenMin = 35f;
    /// <summary>不利の下限（既定 15 = 負け・敵の 25% 以上を倒す）。これ未満は絶望。</summary>
    public float unfavorableMin = 15f;

    /// <summary>どれも数で、楽勝 ≥ 有利 ≥ 互角 ≥ 不利 の順になっているか。</summary>
    public bool IsValid =>
        !float.IsNaN(easyMin) && !float.IsNaN(favorableMin) && !float.IsNaN(evenMin) && !float.IsNaN(unfavorableMin)
        && easyMin >= favorableMin && favorableMin >= evenMin && evenMin >= unfavorableMin;

    public ClearOutlookSettingsData Clone() => (ClearOutlookSettingsData)MemberwiseClone();
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
