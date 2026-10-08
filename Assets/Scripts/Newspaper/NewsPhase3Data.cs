using System;
using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// フェーズ3のマスター（Docs/News_Phase3_Spec.md §2）
//   事象パターン（効果・真偽はここだけが持つ）／社別テンプレート／埋め草
// =====================================================================

/// <summary>事象パターン 1行（NewsEvents.csv）。周ごとに具体化されて <see cref="NewsEventInstance"/> になる。</summary>
public class NewsEventData
{
    public string eventId;
    /// <summary>系統。cooldown はこの単位で効く（次ダンジョン別に分割した事象を同じ系統にまとめる）。空 = eventId。</summary>
    public string family;
    /// <summary>難易度 easy / normal / hard（空 = easy）。1周の構成比を NewsTuning の目標に寄せる。</summary>
    public string difficulty = "easy";
    /// <summary>common / rare（空 = common）。レアは別枠で低確率、全社が報じる。</summary>
    public string rarity = "common";

    // --- 連載（ストーリーアーク。§17） ---
    public string arcId;          // 単発は空
    public int arcStep;           // 1,2,3…（単発は 0）
    /// <summary>次の話の候補（条件, 事象ID）。最終話は空。条件: rand / always / heroWin / heroLose / hasStock / noStock</summary>
    public List<(string cond, string eventId)> arcNext = new();

    public bool IsArc => !string.IsNullOrEmpty(arcId);
    public bool IsArcStart => IsArc && arcStep <= 1;
    public string category;      // dungeon / hero / rival / supply / culture / economy / village
    public string scale;         // small / medium / large
    public int sign = 1;         // +1 売れる / -1 売れなくなる
    public string targetRule;    // L1:{dungeon} / L2:{dungeon} / L3 / attr:Fire,Weapon / item:AR1 / 空=効果なし
    public Dictionary<string, string> binds = new();   // dungeon=next / region=pool ...
    public float wTrue = 1f, wExaggerated, wFalse;
    public bool hasResult = true;
    public string condition;
    public int weight = 1;
    public int cooldown = 10;
    public string summaryEn;

    public string Family => string.IsNullOrEmpty(family) ? eventId : family;

    public bool IsRare => rarity == "rare";

    public bool HasEffectRule => !string.IsNullOrEmpty(targetRule);

    public int ScaleRank => scale switch { "huge" => 4, "large" => 3, "medium" => 2, _ => 1 };

    /// <summary>目盛り（NewsTuning）から引いた trendDelta（符号込み）。</summary>
    public float TrendDelta => (scale switch
    {
        "huge" => NewsTuning.TrendHuge,
        "large" => NewsTuning.TrendLarge,
        "medium" => NewsTuning.TrendMedium,
        _ => NewsTuning.TrendSmall,
    }) * (sign < 0 ? -1f : 1f);

    public override string ToString() => $"[{eventId}] {category}/{scale}/{targetRule}";
}

/// <summary>社別テンプレート 1行（NewsTemplates.csv）。</summary>
public class NewsTemplateData
{
    public const string AnyCompany = "*";

    public string templateId;
    public string eventId;
    public string companyId;     // royal / guild / commerce / tabloid / craft / *（結果・訂正の社共通）
    public string kind;          // report / result / correction
    public string tone;          // normal / loud
    public string page;
    public string headline, lead, body;
    public string clarity;
    public string status;
    public string summaryEn;

    public bool IsAnyCompany => string.IsNullOrEmpty(companyId) || companyId == AnyCompany;
    public bool IsLoud => tone == "loud";
}

/// <summary>埋め草 1行（NewsFillers.csv）。効果を持たない。</summary>
public class NewsFillerData
{
    public string fillerId;
    public string topic;
    public List<string> companies = new();   // 空 = 全社
    public string page;
    public string headline, lead, body;
    public int cooldown = 8;
    public int weight = 1;
    public string status;
    public string summaryEn;

    public bool AllowedFor(string companyId) => companies.Count == 0 || companies.Contains(companyId);
}

/// <summary>
/// 周の中で具体化された事象1件。<b>効果はここだけが持つ</b>（同じ事象を何社が報じても効果は1回）。
/// 跳ね（1段目）は報じられた日ごとに、同じ事象につき1日1回だけ起きる（<see cref="hypeByDay"/>）。
/// </summary>
public class NewsEventInstance
{
    public string key;            // 周の中で一意（eventId@発効ターン）
    public string eventId;
    public string category;
    public int scaleRank;
    public int effectTurn;
    public int durationTurns = 1;
    public string truth = "true"; // true / exaggerated / false
    public float effectScale = 1f;
    public bool hasResult;
    public string difficulty = "easy";
    public bool isRare;
    public string arcId;      // 連載の話なら連載ID
    public int arcStep;

    // 効果
    public float trendDelta;
    public float demandKick;
    public string targetAttribute;
    public string targetType;
    public string targetItemId;
    public int minRequiredLevel;  // L3（高額帯）

    /// <summary>報道のあった日 → その日の跳ね倍率（その日の報道のうち一番大きい面の値）。</summary>
    public readonly Dictionary<int, float> hypeByDay = new();

    /// <summary>差し込み枠の具体値（{dungeon} → 灼熱の火山牢 など）。</summary>
    public readonly Dictionary<string, string> bindings = new();

    public bool IsFalse => truth == "false";
    public int SettleTurn => effectTurn + Math.Max(1, durationTurns);

    public bool HasTarget =>
        !string.IsNullOrEmpty(targetAttribute) || !string.IsNullOrEmpty(targetType)
        || !string.IsNullOrEmpty(targetItemId) || minRequiredLevel > 0;

    /// <summary>2段目（実需）が来るか。誤報・効果なしは来ない。</summary>
    public bool HasRealEffect => !IsFalse && effectScale > 0f && HasTarget && (trendDelta != 0f || demandKick != 0f);

    public bool Matches(RuntimeItemData item)
    {
        if (item == null || !HasTarget) return false;
        if (!string.IsNullOrEmpty(targetItemId)) return item.ItemId == targetItemId;
        if (!string.IsNullOrEmpty(targetAttribute) && item.ItemAttribute.ToString() != targetAttribute) return false;
        if (!string.IsNullOrEmpty(targetType) && item.ItemType.ToString() != targetType) return false;
        if (minRequiredLevel > 0 && (item.RequiredLevel == null || item.RequiredLevel.Value < minRequiredLevel)) return false;
        return true;
    }

    /// <summary>報道1件ぶんの跳ねを記録する。同じ日は大きい方だけ残す。</summary>
    public void AddHype(int day, float rate)
    {
        if (rate <= 0f || Mathf.Approximately(rate, 1f)) return;
        if (hypeByDay.TryGetValue(day, out var cur) && Mathf.Abs(Mathf.Log(cur)) >= Mathf.Abs(Mathf.Log(rate))) return;
        hypeByDay[day] = rate;
    }

    /// <summary>跳ねの累計（剥がすときに使う）。</summary>
    public float TotalHype
    {
        get
        {
            float r = 1f;
            foreach (var v in hypeByDay.Values) r *= v;
            return r;
        }
    }

    /// <summary>跳ねが剥がれ始めるターン。発効ターンと、最後の報道の翌日の遅い方。</summary>
    public int UnwindStart
    {
        get
        {
            int last = 0;
            foreach (var d in hypeByDay.Keys) last = Math.Max(last, d);
            return Math.Max(effectTurn, last + 1);
        }
    }
}

/// <summary>
/// 発行カレンダーを組むための「世界」の情報（ダンジョンの名前と弱点、戦闘の予定）。
/// GameFlowManager がフローを組んだ後に渡す。無ければダンジョンを差し込む事象は置かれない。
/// </summary>
public class NewsWorld
{
    public struct Dungeon
    {
        public string key;        // DungeonName の文字列
        public string name;       // 表示名
        public string weakness;   // 弱点属性（ItemAttribute の文字列）
    }

    public readonly List<Dungeon> dungeons = new();
    /// <summary>戦闘の予定（ターン, ダンジョンの key）。ターン昇順。</summary>
    public readonly List<(int turn, string key)> battles = new();

    public bool TryGet(string key, out Dungeon d)
    {
        foreach (var x in dungeons)
            if (x.key == key || x.name == key) { d = x; return true; }
        d = default;
        return false;
    }

    /// <summary>そのターンから見た次の戦闘のダンジョン。無ければ false。</summary>
    public bool TryNextBattle(int turn, out Dungeon d)
    {
        foreach (var b in battles)
            if (b.turn >= turn && TryGet(b.key, out d)) return true;
        d = default;
        return false;
    }

    /// <summary>周の再生成を判定するための指紋。</summary>
    public string Fingerprint()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var d in dungeons) sb.Append(d.key).Append(d.weakness).Append(';');
        foreach (var b in battles) sb.Append(b.turn).Append(b.key).Append(';');
        return sb.ToString();
    }
}

/// <summary>
/// 連載の「次の話」の予約枠（§17）。分岐は決定ターンの朝に、その時点の状態で決める。
/// 決まった分岐（resolvedEventId）はセーブに残し、ロード後も同じ紙面を再現する。
/// </summary>
public class NewsArcSlot
{
    public string key;            // 周の中で一意（arcId#step@決定ターン）
    public string arcId;
    public int step;
    public string prevEventKey;   // 前の話の事象（hasStock の判定と差し込み枠の引き継ぎに使う）
    public int decisionTurn;      // この朝に分岐を決める（= 最初の社が報じる日）
    public int effectTurn;        // 次の話の発効ターン
    public List<(string cond, string eventId)> options = new();
    public string resolvedEventId; // 決まった事象。空 = 未決定 / "-" = 連載はここで終わり

    public bool IsResolved => !string.IsNullOrEmpty(resolvedEventId);
}
