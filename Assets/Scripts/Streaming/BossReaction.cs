using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

// =====================================================================
// ボスの反応（2026-10-30 12:30 / 13:00 ユーザーの決定、審議 panel_1030e）
// - 盛り上がり（配信熱）と欲しさ（武器・防具の売れ行きの気分）を分ける。
// - ボス・中ボスが出た瞬間の「状況」= 勝敗メーター（互角は 2 つ）+ HP + 弱点（属性相性）+ 流れ の足し算 → 5 つ。
// - 反応の表（balance.json の bossReaction / シート bal_bossReaction）が空なら何も動かさない（影のログだけ）。
// - 欲しさは v1 では「売れる数」だけを動かす（値札は動かさない）。配信の中だけで、翌日の店には持ち越さない。
// =====================================================================

/// <summary>ボス・中ボスが出た瞬間の状況（勇者から見て 0 = 絶望 … 4 = 勝ちムード）。</summary>
public enum BossSituation
{
    None = -1,
    Desperate = 0, // 絶望
    Behind = 1,    // 劣勢
    Close = 2,     // 拮抗
    Favored = 3,   // 優勢
    Winning = 4,   // 勝ちムード
}

public static class BossSituations
{
    public static string Key(BossSituation s) => s switch
    {
        BossSituation.Winning => "winning",
        BossSituation.Favored => "favored",
        BossSituation.Close => "close",
        BossSituation.Behind => "behind",
        BossSituation.Desperate => "desperate",
        _ => "none",
    };

    public static string Label(BossSituation s) => s switch
    {
        BossSituation.Winning => "勝ちムード",
        BossSituation.Favored => "優勢",
        BossSituation.Close => "拮抗",
        BossSituation.Behind => "劣勢",
        BossSituation.Desperate => "絶望",
        _ => "なし",
    };

    /// <summary>英語のキー・日本語の名前のどちらでも読む。</summary>
    public static bool TryParse(string text, out BossSituation s)
    {
        s = BossSituation.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string t = text.Trim();
        foreach (BossSituation v in Enum.GetValues(typeof(BossSituation)))
        {
            if (v == BossSituation.None) continue;
            if (string.Equals(t, Key(v), StringComparison.OrdinalIgnoreCase) || t == Label(v) || string.Equals(t, v.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                s = v;
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// ウェーブの種類（dungeonPhases の phaseType: normal / midboss / boss）を読む。
/// DungeonPhaseData に phaseType（文字列か enum）が無い版では null を返す（中ボスは無し。ボスは isBoss の敵で分かる）。
/// 別の作業で足されるフィールドなので、コンパイルが依存しないようリフレクションで読む。
/// </summary>
public static class DungeonPhaseTypes
{
    public const string Normal = "normal";
    public const string Midboss = "midboss";
    public const string Boss = "boss";

    private static bool _resolved;
    private static FieldInfo _field;
    private static PropertyInfo _property;

    public static string Of(DungeonPhaseData phase)
    {
        if (phase == null) return null;
        if (!_resolved)
        {
            _resolved = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var type = typeof(DungeonPhaseData);
            _field = type.GetField("phaseType", flags);
            if (_field == null) _property = type.GetProperty("phaseType", flags) ?? type.GetProperty("PhaseType", flags);
        }
        object v = null;
        try
        {
            if (_field != null) v = _field.GetValue(phase);
            else if (_property != null) v = _property.GetValue(phase);
        }
        catch (Exception)
        {
            return null;
        }
        string s = v?.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.ToLowerInvariant();
    }
}

/// <summary>反応の表の 1 行（bal_bossReaction）。値が空の項目は null（その項目は動かさない）。</summary>
public sealed class BossReactionRow
{
    public string Id;
    /// <summary>"boss" / "midboss"。空なら boss。</summary>
    public string PhaseType;
    public BossSituation Situation;
    /// <summary>出現の瞬間の熱。boss の行は bossAppearHeat の代わり、midboss の行はそのまま足す。</summary>
    public float? Heat;
    /// <summary>売れ行きの気分（売れる数への倍率。1 = 変えない）。</summary>
    public float? WeaponMood;
    public float? ArmorMood;
    /// <summary>気分が続くターン数（0 以下・空 = 配信の終わりまで）。</summary>
    public int? MoodTurns;
    /// <summary>【v1 では使わない】値札の倍率。補充の払い戻しを下げた後の区間で使う予定（読むだけ）。</summary>
    public float? WeaponPriceMul;
    public float? ArmorPriceMul;
}

/// <summary>
/// 反応の表（balance.json の "bossReaction" 配列。シート bal_bossReaction、GAS の BALANCE_LISTS で行シートとして出す）。
/// 列: id, phaseType(boss/midboss), situation(winning/favored/close/behind/desperate または 勝ちムード…), heat:float,
///     weaponMood:float, armorMood:float, moodTurns:int, weaponPriceMul:float, armorPriceMul:float（値札の 2 列は v1 では無視）。
/// situation の列が無ければ id を「boss_close」「midboss:絶望」のように読む。表が無い・空なら今と同じ動き。
/// </summary>
public static class BossReactionTable
{
    public const string RemoteKey = "bossReaction";

    private static List<BossReactionRow> _rows;

    public static bool IsEmpty => _rows == null || _rows.Count == 0;
    public static int Count => _rows?.Count ?? 0;

    public static void Clear() => _rows = null;

    /// <summary>配信の配列を読む（RemoteBalance.OverrideFromBundle から呼ぶ）。不正な行は読み飛ばす。</summary>
    public static void Load(JToken token)
    {
        _rows = null;
        if (token is not JArray arr || arr.Count == 0) return;
        var list = new List<BossReactionRow>();
        foreach (var el in arr)
        {
            if (el is not JObject o) continue;
            string id = o.Value<string>("id")?.Trim() ?? "";
            string phaseType = (o.Value<string>("phaseType") ?? "").Trim().ToLowerInvariant();
            string situationText = o.Value<string>("situation");
            if (string.IsNullOrWhiteSpace(situationText) || string.IsNullOrEmpty(phaseType))
            {
                // id から読む（boss_close / midboss:絶望 / close）
                var parts = id.Split(new[] { '_', ':', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    if (string.IsNullOrEmpty(phaseType)) phaseType = parts[0].Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(situationText)) situationText = parts[parts.Length - 1];
                }
                else if (string.IsNullOrWhiteSpace(situationText)) situationText = id;
            }
            if (string.IsNullOrEmpty(phaseType)) phaseType = DungeonPhaseTypes.Boss;
            if (!BossSituations.TryParse(situationText, out var situation))
            {
                Debug.LogWarning($"[BossReaction] 状況が読めない行を読み飛ばします: {o.ToString(Newtonsoft.Json.Formatting.None)}");
                continue;
            }
            var row = new BossReactionRow
            {
                Id = id,
                PhaseType = phaseType,
                Situation = situation,
                Heat = ReadFloat(o["heat"]),
                WeaponMood = ReadFloat(o["weaponMood"]),
                ArmorMood = ReadFloat(o["armorMood"]),
                MoodTurns = ReadInt(o["moodTurns"]),
                WeaponPriceMul = ReadFloat(o["weaponPriceMul"]),
                ArmorPriceMul = ReadFloat(o["armorPriceMul"]),
            };
            if ((row.WeaponPriceMul.HasValue && !Mathf.Approximately(row.WeaponPriceMul.Value, 1f))
                || (row.ArmorPriceMul.HasValue && !Mathf.Approximately(row.ArmorPriceMul.Value, 1f)))
                Debug.LogWarning($"[BossReaction] 行 '{id}' の値札の倍率は v1 では使いません（売れる数だけを動かす。2026-10-30 の決定）。");
            list.Add(row);
        }
        if (list.Count > 0) _rows = list;
    }

    /// <summary>種類と状況の行（同じ組が 2 つあれば下の行）。無ければ null。</summary>
    public static BossReactionRow Find(string phaseType, BossSituation situation)
    {
        if (_rows == null || situation == BossSituation.None) return null;
        string pt = string.IsNullOrEmpty(phaseType) ? DungeonPhaseTypes.Boss : phaseType;
        BossReactionRow found = null;
        foreach (var r in _rows)
            if (r.Situation == situation && r.PhaseType == pt) found = r;
        return found;
    }

    private static float? ReadFloat(JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
        {
            double d = t.Value<double>();
            return double.IsNaN(d) || double.IsInfinity(d) ? null : (float)d;
        }
        if (t.Type == JTokenType.String
            && double.TryParse(((string)t).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
            return (float)s;
        return null;
    }

    private static int? ReadInt(JToken t)
    {
        var f = ReadFloat(t);
        return f.HasValue ? Mathf.RoundToInt(f.Value) : (int?)null;
    }
}

/// <summary>
/// ボスの状況の点数に使う勝敗メーターの 6 段（0 = 絶望 … 5 = 楽勝）。見込みの 5 段（ClearProbabilityCalculator.ToOutlook）の
/// 互角だけを 50% で勇者寄り・魔王寄りに分ける。魔王軍支援のオッズ（SupportOddsTier。互角は 1 段）とは別に、ここだけで持つ。
/// </summary>
public enum BossMeterStep
{
    Unknown = -1,
    Hopeless = 0,
    Unfavorable = 1,
    EvenDemon = 2,
    EvenHero = 3,
    Favorable = 4,
    Easy = 5,
}

public static class BossMeterSteps
{
    /// <summary>互角を分ける境目（表示％）。勝つ想定は 55% 以上、負ける想定は 45% 以下なので、この間で分かれる。</summary>
    public const float EvenSplitPct = 50f;

    /// <summary>勝敗メーター（0〜1。BattleOutlookEstimator）→ 6 段。</summary>
    public static BossMeterStep FromMeter(float meter)
    {
        if (float.IsNaN(meter)) return BossMeterStep.Unknown;
        float pct = Mathf.Clamp01(meter) * 100f;
        return ClearProbabilityCalculator.ToOutlook(pct) switch
        {
            ClearOutlook.Easy => BossMeterStep.Easy,
            ClearOutlook.Favorable => BossMeterStep.Favorable,
            ClearOutlook.Even => pct >= EvenSplitPct ? BossMeterStep.EvenHero : BossMeterStep.EvenDemon,
            ClearOutlook.Unfavorable => BossMeterStep.Unfavorable,
            _ => BossMeterStep.Hopeless,
        };
    }

    /// <summary>ログのキー（2026-10-30 の影のログと同じ文字列）。</summary>
    public static string Key(BossMeterStep step) => step switch
    {
        BossMeterStep.Easy => "easy",
        BossMeterStep.Favorable => "favorable",
        BossMeterStep.EvenHero => "evenHero",
        BossMeterStep.EvenDemon => "evenDemon",
        BossMeterStep.Unfavorable => "unfavorable",
        BossMeterStep.Hopeless => "hopeless",
        _ => "unknown",
    };
}

/// <summary>状況の点数（足し算）。画面に出ているものだけで決める。</summary>
public static class BossSituationScorer
{
    public readonly struct Result
    {
        public readonly float Score;
        public readonly BossSituation Situation;
        public readonly BossMeterStep MeterTier;
        public readonly float MeterPoints;
        public readonly float HpPoints;
        public readonly float WeakPoints;
        public readonly float FlowPoints;

        public Result(float score, BossSituation situation, BossMeterStep meterTier, float meterPoints, float hpPoints, float weakPoints, float flowPoints)
        {
            Score = score;
            Situation = situation;
            MeterTier = meterTier;
            MeterPoints = meterPoints;
            HpPoints = hpPoints;
            WeakPoints = weakPoints;
            FlowPoints = flowPoints;
        }
    }

    /// <summary>
    /// 点数 = メーター点（勝敗メーターの 6 段: 絶望 0 / 不利 1 / 互角・魔王寄り 2 / 互角・勇者寄り 3 / 有利 4 / 楽勝 5）
    ///      + HP 点（大ピンチ未満 −1 / ピンチ未満 −0.5 / それ以外 0。境目は配信熱と同じ pinchHpRatio・criticalPinchHpRatio）
    ///      + 弱点点（勇者の武器の属性が相手の弱点なら +1。不利な属性は 0。戦闘で不利属性は何も起こさないため）
    ///      + 流れ点（flowTurns ターン前よりメーターが flowMeterDelta 以上上がっていれば +0.5、下がっていれば −0.5）。
    /// 5 つの状況へは BossReactionSettingsData の境目で振り分ける。
    /// </summary>
    public static Result Score(float meter, float heroHpRatio, bool weak, float? meterBefore,
        BossReactionSettingsData s, float pinchHpRatio, float criticalPinchHpRatio)
    {
        s ??= new BossReactionSettingsData();
        var tier = BossMeterSteps.FromMeter(meter);
        float meterPts = tier == BossMeterStep.Unknown ? 2.5f : (int)tier;
        float hpPts = heroHpRatio < criticalPinchHpRatio ? -1f : heroHpRatio < pinchHpRatio ? -0.5f : 0f;
        float weakPts = weak ? 1f : 0f;
        float flowPts = 0f;
        if (meterBefore.HasValue)
        {
            float d = meter - meterBefore.Value;
            float th = Mathf.Max(0f, s.flowMeterDelta);
            if (d >= th && d > 0f) flowPts = 0.5f;
            else if (d <= -th && d < 0f) flowPts = -0.5f;
        }
        float score = meterPts + hpPts + weakPts + flowPts;
        return new Result(score, Classify(score, s), tier, meterPts, hpPts, weakPts, flowPts);
    }

    public static BossSituation Classify(float score, BossReactionSettingsData s)
    {
        if (score >= s.winningMinScore) return BossSituation.Winning;
        if (score >= s.favoredMinScore) return BossSituation.Favored;
        if (score >= s.closeMinScore) return BossSituation.Close;
        if (score >= s.behindMinScore) return BossSituation.Behind;
        return BossSituation.Desperate;
    }
}

/// <summary>
/// 1 配信ぶんのボスの反応と配信熱の記録（StreamingSalesController が配信ごとに作る。翌日へは何も持ち越さない）。
/// Unity のシーンに依存しないので EditMode テストで動かせる。
/// </summary>
public sealed class BossReactionTracker
{
    /// <summary>1 ターンの終わり（または出現の瞬間）の戦場の様子。</summary>
    public readonly struct Snapshot
    {
        public readonly float Heat;
        public readonly float Meter;
        public readonly float HeroHpRatio;
        public readonly int PhaseIndex;
        public readonly int PhaseCount;

        public Snapshot(float heat, float meter, float heroHpRatio, int phaseIndex, int phaseCount)
        {
            Heat = heat;
            Meter = meter;
            HeroHpRatio = heroHpRatio;
            PhaseIndex = phaseIndex;
            PhaseCount = phaseCount;
        }
    }

    /// <summary>反応の結果（足す熱・状況・使った行）。</summary>
    public readonly struct Effect
    {
        public readonly string Kind;            // boss / midboss / comeback / collapse
        public readonly float Heat;             // 足す熱（0 なら足さない。boss は表が空なら bossAppearHeat）
        public readonly BossSituation Situation;
        public readonly BossSituationScorer.Result Score;
        public readonly BossReactionRow Row;     // 使った行（無ければ null）
        public readonly Snapshot At;

        public Effect(string kind, float heat, BossSituation situation, BossSituationScorer.Result score, BossReactionRow row, Snapshot at)
        {
            Kind = kind;
            Heat = heat;
            Situation = situation;
            Score = score;
            Row = row;
            At = at;
        }
    }

    public const string KindBoss = "boss";
    public const string KindMidboss = "midboss";
    public const string KindComeback = "comeback";
    public const string KindCollapse = "collapse";

    private const float ComebackMeter = 0.55f; // 勝つ見込み（ClearProbabilityCalculator と同じ目盛り）
    private const float CollapseMeter = 0.45f; // 負ける見込み

    private readonly BossReactionSettingsData _s;
    private readonly float _pinch;
    private readonly float _critical;
    private readonly List<float> _meters = new();

    // 熱の道筋
    private readonly float _startHeat;
    private float _peakHeat;
    private float _lastHeat;
    private int _lastTier = -1;
    private float _lastPriceMul = 1f;
    private readonly int[] _turnsPerTier = new int[4];
    private readonly JArray _heatPath = new();

    // 状況
    private Effect? _boss;
    private Effect? _midboss;
    private int _lastPhaseIndex = -1;
    private bool _comebackFired;
    private bool _collapseFired;

    // 売れ行きの気分（売れる数への倍率）。turnsLeft: -1 = 配信の終わりまで、0 = 効いていない
    private float _weaponMood = 1f;
    private float _armorMood = 1f;
    private int _moodTurnsLeft;

    /// <summary>状況が決まった（uiEnabled のときだけ流す。表示はまだ無い）。</summary>
    public event Action<Effect> SituationDecided;

    public int Turn { get; private set; }
    public BossSituation BossSituation => _boss?.Situation ?? BossSituation.None;
    public BossSituation MidbossSituation => _midboss?.Situation ?? BossSituation.None;
    public float WeaponMood => _moodTurnsLeft != 0 ? _weaponMood : 1f;
    public float ArmorMood => _moodTurnsLeft != 0 ? _armorMood : 1f;

    public BossReactionTracker(BossReactionSettingsData settings, float pinchHpRatio, float criticalPinchHpRatio, float startHeat)
    {
        _s = settings ?? new BossReactionSettingsData();
        _pinch = pinchHpRatio;
        _critical = criticalPinchHpRatio;
        _startHeat = startHeat;
        _peakHeat = startHeat;
        _lastHeat = startHeat;
    }

    /// <summary>売れる数への倍率（武器・防具だけ。気分が効いていなければ 1）。</summary>
    public float GetVolumeMultiplier(ItemTypeData.ItemType type) => type switch
    {
        ItemTypeData.ItemType.Weapon => WeaponMood,
        ItemTypeData.ItemType.Armor => ArmorMood,
        _ => 1f,
    };

    /// <summary>
    /// ボス（kind = boss）・中ボス（midboss）が出た瞬間。状況を決め、表の行があれば気分を入れて、足す熱を返す。
    /// boss: 行の heat が無ければ defaultHeat（= bossAppearHeat。表が空なら今と同じ）。midboss: 行が無ければ 0。
    /// </summary>
    public Effect OnAppear(string kind, Snapshot at, bool weak, float defaultHeat)
    {
        float? before = MeterTurnsAgo(_s.flowTurns);
        var score = BossSituationScorer.Score(at.Meter, at.HeroHpRatio, weak, before, _s, _pinch, _critical);
        var row = BossReactionTable.Find(kind, score.Situation);
        float heat = kind == KindBoss ? (row?.Heat ?? defaultHeat) : (row?.Heat ?? 0f);
        if (row != null && (row.WeaponMood.HasValue || row.ArmorMood.HasValue))
        {
            _weaponMood = Mathf.Max(0f, row.WeaponMood ?? 1f);
            _armorMood = Mathf.Max(0f, row.ArmorMood ?? 1f);
            int turns = row.MoodTurns ?? 0;
            _moodTurnsLeft = turns > 0 ? turns : -1;
        }
        var effect = new Effect(kind, heat, score.Situation, score, row, at);
        if (kind == KindBoss)
        {
            _boss = effect;
            _comebackFired = false;
            _collapseFired = false;
        }
        else _midboss = effect;
        if (_s.uiEnabled) SituationDecided?.Invoke(effect);
        return effect;
    }

    /// <summary>
    /// ターンの終わり（配信熱の段の掛け直しの後）。熱の道筋と影のログを書き、中ボスの出現（ウェーブの種類が midboss に変わった）と
    /// ボスの後の逆転・崩れを見る。足す熱がある反応を返す（どれも既定では熱 0）。
    /// 気分の残りターンは最初に減らす（出現したターンの販売から数えて moodTurns ターン分の販売に効く）。
    /// </summary>
    public List<Effect> RecordTurn(Snapshot at, int heatTier, float heatPriceMul, string phaseType, bool weakAgainstStrongest)
    {
        Turn++;
        var effects = new List<Effect>();

        // 気分の残りターン（このターンの販売は済んでいる）。先に減らしてから、このターンの終わりに起きた反応で入れ直す
        if (_moodTurnsLeft > 0)
        {
            _moodTurnsLeft--;
            if (_moodTurnsLeft == 0)
            {
                _weaponMood = 1f;
                _armorMood = 1f;
            }
        }

        // 熱の道筋（段が変わったターンだけ記録）
        _peakHeat = Mathf.Max(_peakHeat, at.Heat);
        _lastHeat = at.Heat;
        if (heatTier >= 0 && heatTier < _turnsPerTier.Length) _turnsPerTier[heatTier]++;
        if (heatTier != _lastTier)
        {
            _heatPath.Add(new JArray(Turn, _lastTier, heatTier, Round(heatPriceMul, 3), Round(at.Heat, 1)));
            _lastTier = heatTier;
        }
        _lastPriceMul = heatPriceMul;

        // 中ボス: ウェーブの種類が midboss のウェーブに入った（phaseType の無い版では起きない）
        if (at.PhaseIndex != _lastPhaseIndex)
        {
            _lastPhaseIndex = at.PhaseIndex;
            if (phaseType == DungeonPhaseTypes.Midboss)
                effects.Add(OnAppear(KindMidboss, at, weakAgainstStrongest, 0f));
        }

        _meters.Add(at.Meter);

        // ボスの後: 逆転・崩れ（各 1 回）
        if (_boss.HasValue)
        {
            var bs = _boss.Value.Situation;
            if (!_comebackFired && (bs == BossSituation.Behind || bs == BossSituation.Desperate) && at.Meter >= ComebackMeter)
            {
                _comebackFired = true;
                effects.Add(AfterEvent(KindComeback, at, _s.comebackHeat, _s.comebackWeaponMood, _s.comebackArmorMood));
            }
            if (!_collapseFired && (bs == BossSituation.Winning || bs == BossSituation.Favored) && at.Meter <= CollapseMeter)
            {
                _collapseFired = true;
                effects.Add(AfterEvent(KindCollapse, at, _s.collapseHeat, _s.collapseWeaponMood, _s.collapseArmorMood));
            }
        }

        if (_s.shadowTurnLog)
        {
            BalanceLog.Write("heat_turn", new JObject
            {
                ["turn"] = Turn,
                ["heat"] = Round(at.Heat, 1),
                ["tier"] = heatTier,
                ["priceMul"] = Round(heatPriceMul, 3),
                ["meter"] = Round(at.Meter, 3),
                ["meterTier"] = BossMeterSteps.Key(BossMeterSteps.FromMeter(at.Meter)),
                ["heroHp"] = Round(at.HeroHpRatio, 3),
                ["phase"] = at.PhaseIndex,
                ["phaseType"] = phaseType,
                ["weaponMood"] = Round(WeaponMood, 3),
                ["armorMood"] = Round(ArmorMood, 3),
            }, echo: false);
        }

        return effects;
    }

    private Effect AfterEvent(string kind, Snapshot at, float heat, float weaponDelta, float armorDelta)
    {
        if (weaponDelta != 0f || armorDelta != 0f)
        {
            _weaponMood = Mathf.Max(0f, WeaponMood + weaponDelta);
            _armorMood = Mathf.Max(0f, ArmorMood + armorDelta);
            _moodTurnsLeft = _s.afterMoodTurns > 0 ? _s.afterMoodTurns : -1;
        }
        var basis = _boss.Value;
        return new Effect(kind, heat, basis.Situation, basis.Score, null, at);
    }

    private float? MeterTurnsAgo(int turns)
    {
        if (_meters.Count == 0) return null;
        int n = Mathf.Max(1, turns);
        int i = _meters.Count - n;
        return _meters[Mathf.Max(0, i)];
    }

    /// <summary>出現・逆転・崩れの影のログ（boss_situation）。熱は反応を足す前と後。</summary>
    public void LogEffect(Effect e, float heatBefore, float heatAfter)
    {
        var sc = e.Score;
        BalanceLog.Write("boss_situation", new JObject
        {
            ["event"] = e.Kind,
            ["turn"] = Turn,
            ["situation"] = BossSituations.Key(e.Situation),
            ["situationLabel"] = BossSituations.Label(e.Situation),
            ["score"] = Round(sc.Score, 2),
            ["meter"] = Round(e.At.Meter, 3),
            ["meterTier"] = BossMeterSteps.Key(sc.MeterTier),
            ["meterPts"] = sc.MeterPoints,
            ["heroHp"] = Round(e.At.HeroHpRatio, 3),
            ["hpPts"] = sc.HpPoints,
            ["weakPts"] = sc.WeakPoints,
            ["flowPts"] = sc.FlowPoints,
            ["row"] = e.Row?.Id,
            ["heatAdded"] = Round(e.Heat, 1),
            ["heatBefore"] = Round(heatBefore, 1),
            ["heatAfter"] = Round(heatAfter, 1),
            ["weaponMood"] = Round(WeaponMood, 3),
            ["armorMood"] = Round(ArmorMood, 3),
            ["phase"] = e.At.PhaseIndex,
        });
    }

    /// <summary>stream_result に入れる要約（熱の道筋・状況・逆転/崩れ）。</summary>
    public JObject BuildSummary()
    {
        var o = new JObject
        {
            ["turns"] = Turn,
            ["heatStart"] = Round(_startHeat, 1),
            ["heatPeak"] = Round(_peakHeat, 1),
            ["heatEnd"] = Round(_lastHeat, 1),
            ["heatPriceMulEnd"] = Round(_lastPriceMul, 3),
            ["turnsPerHeatTier"] = new JArray(_turnsPerTier[0], _turnsPerTier[1], _turnsPerTier[2], _turnsPerTier[3]),
            // [ターン, 前の段, 新しい段, 値段の倍率, 熱]（段: 0 冷め / 1 普通 / 2 盛り上がり / 3 超人気。-1 = 開始前）
            ["heatPath"] = _heatPath.DeepClone(),
            ["bossSituation"] = BossSituations.Key(BossSituation),
            ["midbossSituation"] = BossSituations.Key(MidbossSituation),
            ["comeback"] = _comebackFired,
            ["collapse"] = _collapseFired,
            ["reactionTableRows"] = BossReactionTable.Count,
        };
        if (_boss.HasValue)
        {
            o["bossScore"] = Round(_boss.Value.Score.Score, 2);
            o["bossPhase"] = _boss.Value.At.PhaseIndex;
            o["bossMeter"] = Round(_boss.Value.At.Meter, 3);
            o["bossRow"] = _boss.Value.Row?.Id;
        }
        if (_midboss.HasValue)
        {
            o["midbossScore"] = Round(_midboss.Value.Score.Score, 2);
            o["midbossRow"] = _midboss.Value.Row?.Id;
        }
        return o;
    }

    private static double Round(float v, int digits) => Math.Round(v, digits);
}
