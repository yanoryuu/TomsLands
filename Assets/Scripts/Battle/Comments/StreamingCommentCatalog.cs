using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 配信コメント（ニコニコ式）の文言と出し方のデータ。
/// 文言は CSV（<see cref="csv"/>）で差し替える。CSV が無いときは <see cref="entries"/>、それも空なら組み込みの最小セット。
///
/// CSV 列: trigger,text,weight,size,position,color,minHeat,buzz
///   trigger  … StreamingCommentTrigger の名前（Idle / EnemyDefeated など）
///   text     … {hero} {enemy} {item} {price} {viewers} を置換
///   weight   … 抽選の重み（省略 1）
///   size     … Small / Medium / Big（省略 Medium）
///   position … Flow / Top / Bottom（省略 Flow）
///   color    … white / red / pink / orange / yellow / green / cyan / blue / purple / #RRGGBB（省略 white）
///   minHeat  … この配信熱以上でのみ出る（0〜100、省略 0）
///   buzz     … Any / Buzz / SuperBuzz / Flame（省略 Any。Buzz は通常バズ・超バズのどちらでも可）
/// </summary>
[CreateAssetMenu(menuName = "ScriptableObjects/Battle/StreamingCommentCatalog", fileName = "StreamingCommentCatalog")]
public class StreamingCommentCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public StreamingCommentTrigger trigger;
        public string text;
        public float weight = 1f;
        public NicoCommentSize size = NicoCommentSize.Medium;
        public NicoCommentPosition position = NicoCommentPosition.Flow;
        public Color color = Color.white;
        [Range(0, 100)] public float minHeat = 0f;
        public StreamingCommentBuzzCondition buzz = StreamingCommentBuzzCondition.Any;
    }

    [Serializable]
    public class Burst
    {
        public StreamingCommentTrigger trigger;
        [Tooltip("1回のイベントで出す件数（同接の倍率がさらに掛かる）")]
        public int minCount = 1;
        public int maxCount = 1;
        [Tooltip("何秒に散らして出すか")]
        public float spreadSeconds = 0.6f;
        [Tooltip("発生確率（0〜1）。販売など頻度の高いイベントで間引く")]
        [Range(0, 1)] public float chance = 1f;
        [Tooltip("同じトリガーを次に受け付けるまでの秒数")]
        public float cooldownSeconds = 0f;
        [Tooltip("優先度（同時表示上限を超えたとき低い方を捨てる）")]
        public int priority = 1;
    }

    [Header("文言（CSV が設定されていれば CSV を優先）")]
    public TextAsset csv;
    public List<Entry> entries = new();

    [Header("イベントごとの弾幕量")]
    public List<Burst> bursts = new();

    [Header("雑談の間隔（秒）: 配信熱 0 → 100 を Lerp。さらに同接倍率で割る")]
    public float idleIntervalAtHeat0 = 2.6f;
    public float idleIntervalAtHeat100 = 0.45f;

    [Header("同接 → コメント量の倍率")]
    [Tooltip("この同接数で倍率 1.0")]
    public float referenceViewers = 150f;
    public float minDensityScale = 0.5f;
    public float maxDensityScale = 2.5f;

    [Header("表示")]
    [Tooltip("同時に画面に出せる上限。超えたら優先度の低いものから捨てる")]
    public int maxConcurrent = 40;

    // ───────── 実行時キャッシュ ─────────
    [NonSerialized] private List<Entry> _resolved;
    [NonSerialized] private Dictionary<StreamingCommentTrigger, Burst> _burstMap;

    /// <summary>CSV / entries / 組み込みの順で解決した文言一覧。</summary>
    public IReadOnlyList<Entry> GetEntries()
    {
        if (_resolved != null) return _resolved;
        _resolved = csv != null ? ParseCsv(csv.text) : new List<Entry>();
        if (_resolved.Count == 0 && entries != null) _resolved.AddRange(entries);
        if (_resolved.Count == 0) _resolved.AddRange(BuiltInEntries());
        return _resolved;
    }

    public Burst GetBurst(StreamingCommentTrigger trigger)
    {
        if (_burstMap == null)
        {
            _burstMap = new Dictionary<StreamingCommentTrigger, Burst>();
            foreach (var b in DefaultBursts()) _burstMap[b.trigger] = b;
            if (bursts != null)
                foreach (var b in bursts) if (b != null) _burstMap[b.trigger] = b;
        }
        return _burstMap.TryGetValue(trigger, out var burst) ? burst : null;
    }

    /// <summary>CSV を差し替えた後などに呼ぶ。</summary>
    public void ClearCache()
    {
        _resolved = null;
        _burstMap = null;
    }

    private void OnValidate() => ClearCache();

    /// <summary>アセットが無いときの代替（組み込み文言）。</summary>
    public static StreamingCommentCatalog CreateFallback()
    {
        var c = CreateInstance<StreamingCommentCatalog>();
        c.name = "StreamingCommentCatalog(Fallback)";
        return c;
    }

    // ═════════════════════════════════════════
    //  CSV
    // ═════════════════════════════════════════

    public static List<Entry> ParseCsv(string text)
    {
        var list = new List<Entry>();
        if (string.IsNullOrEmpty(text)) return list;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            var cols = SplitCsvLine(line);
            if (cols.Count < 2) continue;
            if (i == 0 && cols[0].Trim().Equals("trigger", StringComparison.OrdinalIgnoreCase)) continue; // ヘッダー

            if (!Enum.TryParse(cols[0].Trim(), true, out StreamingCommentTrigger trigger)) continue;
            var body = cols[1].Trim();
            if (string.IsNullOrEmpty(body)) continue;

            var e = new Entry { trigger = trigger, text = body };
            if (cols.Count > 2 && float.TryParse(cols[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) e.weight = Mathf.Max(0f, w);
            if (cols.Count > 3 && Enum.TryParse(cols[3].Trim(), true, out NicoCommentSize size)) e.size = size;
            if (cols.Count > 4 && Enum.TryParse(cols[4].Trim(), true, out NicoCommentPosition pos)) e.position = pos;
            if (cols.Count > 5) e.color = ParseColor(cols[5].Trim());
            if (cols.Count > 6 && float.TryParse(cols[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mh)) e.minHeat = mh;
            if (cols.Count > 7 && Enum.TryParse(cols[7].Trim(), true, out StreamingCommentBuzzCondition bz)) e.buzz = bz;
            list.Add(e);
        }
        return list;
    }

    /// <summary>ニコニコのコマンド色に寄せた名前付き色。</summary>
    public static Color ParseColor(string s)
    {
        if (string.IsNullOrEmpty(s)) return Color.white;
        switch (s.ToLowerInvariant())
        {
            case "white": return Color.white;
            case "red": return new Color32(0xFF, 0x3B, 0x3B, 0xFF);
            case "pink": return new Color32(0xFF, 0x8C, 0xC6, 0xFF);
            case "orange": return new Color32(0xFF, 0xA5, 0x2E, 0xFF);
            case "yellow": return new Color32(0xFF, 0xE8, 0x3D, 0xFF);
            case "green": return new Color32(0x5C, 0xF0, 0x6A, 0xFF);
            case "cyan": return new Color32(0x4F, 0xF0, 0xFF, 0xFF);
            case "blue": return new Color32(0x6B, 0x8C, 0xFF, 0xFF);
            case "purple": return new Color32(0xC6, 0x7B, 0xFF, 0xFF);
            case "gold": return new Color32(0xE8, 0xA3, 0x3D, 0xFF);
        }
        var hex = s.StartsWith("#") ? s : "#" + s;
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    // ═════════════════════════════════════════
    //  既定値
    // ═════════════════════════════════════════

    private static IEnumerable<Burst> DefaultBursts()
    {
        Burst B(StreamingCommentTrigger t, int min, int max, float spread, float chance = 1f, float cd = 0f, int prio = 1)
            => new Burst { trigger = t, minCount = min, maxCount = max, spreadSeconds = spread, chance = chance, cooldownSeconds = cd, priority = prio };

        yield return B(StreamingCommentTrigger.BattleStart, 4, 7, 1.6f, prio: 2);
        yield return B(StreamingCommentTrigger.Idle, 1, 1, 0f, prio: 0);
        yield return B(StreamingCommentTrigger.HeroAttack, 1, 1, 0.2f, chance: 0.35f, cd: 0.8f);
        yield return B(StreamingCommentTrigger.HeroCritical, 2, 3, 0.5f, cd: 0.6f, prio: 2);
        yield return B(StreamingCommentTrigger.HeroDamaged, 1, 2, 0.4f, chance: 0.5f, cd: 0.8f);
        yield return B(StreamingCommentTrigger.HeroPinch, 3, 5, 1.0f, cd: 4f, prio: 3);
        yield return B(StreamingCommentTrigger.EnemyDefeated, 2, 3, 0.6f, prio: 2);
        yield return B(StreamingCommentTrigger.BossAppeared, 7, 11, 1.4f, prio: 4);
        yield return B(StreamingCommentTrigger.BossDefeated, 8, 12, 1.4f, prio: 4);
        yield return B(StreamingCommentTrigger.WaveCleared, 2, 4, 0.8f, prio: 2);
        yield return B(StreamingCommentTrigger.ItemSold, 1, 1, 0.3f, chance: 0.55f, cd: 0.7f, prio: 1);
        yield return B(StreamingCommentTrigger.StockDepleted, 3, 5, 0.8f, prio: 3);
        yield return B(StreamingCommentTrigger.HeatUp, 3, 5, 1.0f, cd: 3f, prio: 2);
        yield return B(StreamingCommentTrigger.HeatDown, 1, 2, 0.8f, cd: 3f, prio: 1);
        yield return B(StreamingCommentTrigger.Victory, 10, 14, 1.6f, prio: 5);
        yield return B(StreamingCommentTrigger.Defeat, 8, 12, 1.6f, prio: 5);
        yield return B(StreamingCommentTrigger.SuperChat, 1, 2, 0.5f, cd: 0.5f, prio: 2);
        yield return B(StreamingCommentTrigger.RedSuperChat, 6, 9, 1.2f, prio: 4);
        yield return B(StreamingCommentTrigger.InterventionHeal, 2, 3, 0.6f, prio: 2);
        yield return B(StreamingCommentTrigger.InterventionSkill, 3, 4, 0.6f, prio: 3);
        yield return B(StreamingCommentTrigger.SpecialMove, 9, 13, 1.2f, prio: 5);
        yield return B(StreamingCommentTrigger.DungeonTrap, 2, 3, 0.6f, prio: 2);
        yield return B(StreamingCommentTrigger.DungeonCurse, 2, 3, 0.6f, prio: 2);
        yield return B(StreamingCommentTrigger.DungeonReinforce, 3, 4, 0.8f, prio: 3);
        yield return B(StreamingCommentTrigger.DungeonBossBuff, 5, 7, 1.0f, prio: 4);
    }

    private static IEnumerable<Entry> BuiltInEntries()
    {
        Entry E(StreamingCommentTrigger t, string s) => new Entry { trigger = t, text = s };
        yield return E(StreamingCommentTrigger.BattleStart, "わこつ");
        yield return E(StreamingCommentTrigger.Idle, "wktk");
        yield return E(StreamingCommentTrigger.Idle, "ｷﾀ━━━(ﾟ∀ﾟ)━━━!!");
        yield return E(StreamingCommentTrigger.HeroAttack, "いけー");
        yield return E(StreamingCommentTrigger.HeroDamaged, "いてぇ");
        yield return E(StreamingCommentTrigger.EnemyDefeated, "888888");
        yield return E(StreamingCommentTrigger.BossAppeared, "ボスきた");
        yield return E(StreamingCommentTrigger.ItemSold, "{item}ポチった");
        yield return E(StreamingCommentTrigger.StockDepleted, "売り切れ草");
        yield return E(StreamingCommentTrigger.Victory, "うぽつ");
        yield return E(StreamingCommentTrigger.Defeat, "ああああ");
        yield return E(StreamingCommentTrigger.SuperChat, "ナイスパ");
        yield return E(StreamingCommentTrigger.RedSuperChat, "赤スパきたああ");
        yield return E(StreamingCommentTrigger.InterventionHeal, "回復助かる");
        yield return E(StreamingCommentTrigger.InterventionSkill, "スキルきた");
        yield return E(StreamingCommentTrigger.SpecialMove, "うおおおお");
        yield return E(StreamingCommentTrigger.DungeonTrap, "罠www");
        yield return E(StreamingCommentTrigger.DungeonCurse, "呪われてて草");
        yield return E(StreamingCommentTrigger.DungeonReinforce, "なんか湧いた");
        yield return E(StreamingCommentTrigger.DungeonBossBuff, "ボス強くなってない？");
    }
}
