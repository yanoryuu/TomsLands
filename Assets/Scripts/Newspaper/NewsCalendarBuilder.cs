using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// フェーズ3の発行カレンダー（Docs/News_Phase3_Spec.md §3）。純粋関数（同じ入力なら必ず同じ結果）。
///
///   1. 周のシードから事象を置く（発効ターンを決める）
///   2. 事象ごとに報じる社を決め、各社は「発効 − 自社の lead」の日に報じる（時間差クロスリファレンス）
///   3. 決着に合わせて結果記事・訂正記事を置く
///   4. 社 × ターンの号を、本数（issueMin〜issueMax）に合わせて詰める。足りない分は埋め草で補う
///   5. 面を割り当て、記事を組み立てる（差し込み枠・署名・sourceClarity）
///
/// <b>効果は事象が持つ</b>。報道は効果を持たず、跳ね（1段目）だけを「事象 × 日」に1回記録する。
/// </summary>
public static class NewsCalendarBuilder
{
    public class Result
    {
        public readonly List<NewsEventInstance> events = new();
        public readonly List<NewsIssueEntry> entries = new();
        /// <summary>連載の予約枠（§17）。分岐を事前に決められないものは未決定のまま残る。</summary>
        public readonly List<NewsArcSlot> arcSlots = new();
    }

    private static readonly Regex Placeholder = new(@"\{([a-zA-Z_]+)\}", RegexOptions.Compiled);
    private static readonly string[] ClarityOrder = { "confirmed", "presumed", "anonymous" };

    /// <summary>号に入れる前の掲載1件。</summary>
    private class Pending
    {
        public NewsIssueEntry entry;
        public NewsTemplateData template;   // 報道・続報
        public NewsFillerData filler;       // 埋め草
        public NewsEventInstance ev;
        public int priority;                // 大きいほど先に面を取る
    }

    public static Result Build(
        int seed, int maxTurn,
        IReadOnlyList<NewspaperCompanyData> companies,
        IReadOnlyList<NewsEventData> events,
        IReadOnlyList<NewsTemplateData> templates,
        IReadOnlyList<NewsFillerData> fillers,
        NewsWorld world)
    {
        var result = new Result();
        if (companies == null || companies.Count == 0) return result;
        events ??= Array.Empty<NewsEventData>();
        templates ??= Array.Empty<NewsTemplateData>();
        fillers ??= Array.Empty<NewsFillerData>();

        var rng = new Random(seed ^ 0x5EED3);
        var idx = new TemplateIndex(templates);

        // ---- 1〜3. 事象・報道・続報 ----
        var pending = new Dictionary<(string company, int day), List<Pending>>();
        PlaceEvents(rng, seed, maxTurn, companies, events, idx, world, result.events, pending, result.arcSlots);

        // ---- 4〜5. 号を詰めて面を割り当てる ----
        var lastFillerUse = new Dictionary<(string company, string filler), int>();
        // 同じ日に別の社が同じ埋め草を載せない（埋め草がクロスリファレンスに見えてしまうため）
        var fillerByDay = new Dictionary<int, HashSet<string>>();
        foreach (var company in companies)
        {
            List<Pending> carry = null;
            for (int day = 1; day <= maxTurn; day++)
            {
                pending.TryGetValue((company.companyId, day), out var list);
                list = list != null ? new List<Pending>(list) : new List<Pending>();
                if (carry != null) { list.AddRange(carry); carry = null; }

                // 訂正は本数に数えない（「お詫びと訂正」枠に入る）
                var corrections = list.FindAll(p => p.entry.kind == NewsEntryKind.Correction);
                var body = list.FindAll(p => p.entry.kind != NewsEntryKind.Correction);
                body.Sort((a, b) => b.priority.CompareTo(a.priority));

                // 多すぎる分は翌日へ送る（発効日を過ぎるなら落とす。効果は事象側なので影響しない）
                int max = Math.Max(1, company.issueMax);
                if (body.Count > max)
                {
                    carry = new List<Pending>();
                    for (int i = body.Count - 1; i >= max; i--)
                    {
                        var p = body[i];
                        body.RemoveAt(i);
                        if (p.entry.kind == NewsEntryKind.Report && day + 1 < p.entry.effectTurn)
                        {
                            p.entry.publishTurn = day + 1;
                            carry.Add(p);
                        }
                    }
                }

                // 足りない分は埋め草で補う（どの社も毎日1本以上）
                int min = Math.Max(1, company.issueMin);
                if (!fillerByDay.TryGetValue(day, out var usedToday)) fillerByDay[day] = usedToday = new HashSet<string>();
                while (body.Count < min)
                {
                    var f = PickFiller(rng, fillers, company.companyId, day, lastFillerUse, usedToday);
                    if (f == null) break;
                    usedToday.Add(f.fillerId);
                    lastFillerUse[(company.companyId, f.fillerId)] = day;
                    body.Add(new Pending
                    {
                        filler = f,
                        priority = -100,
                        entry = new NewsIssueEntry
                        {
                            publishTurn = day,
                            effectTurn = day,
                            companyId = company.companyId,
                            articleId = f.fillerId,
                            kind = NewsEntryKind.Filler,
                            effectScale = 0f,
                        },
                    });
                }

                AssignPagesAndRender(company, day, body, corrections, result.entries);
            }
        }

        return result;
    }

    // =====================================================================
    // 1〜3. 事象を置き、報道と続報を作る
    // =====================================================================

    private static void PlaceEvents(
        Random rng, int seed, int maxTurn,
        IReadOnlyList<NewspaperCompanyData> companies,
        IReadOnlyList<NewsEventData> events,
        TemplateIndex idx, NewsWorld world,
        List<NewsEventInstance> outEvents,
        Dictionary<(string, int), List<Pending>> pending,
        List<NewsArcSlot> arcSlots)
    {
        var lastPlaced = new Dictionary<string, int>();
        var lastBinding = new Dictionary<string, int>();
        int largeSince = 0;
        var rareCredit = new RareCredit { value = (float)rng.NextDouble() };

        // 連載（§17）: 第1話だけをここで置き、続きは予約枠にする
        var arcLength = new Dictionary<string, int>();
        foreach (var e in events)
            if (e.IsArc) arcLength[e.arcId] = Math.Max(arcLength.TryGetValue(e.arcId, out var l) ? l : 1, Math.Max(1, e.arcStep));
        var arcBusyUntil = new Dictionary<string, int>();
        int arcEvents = 0, singleEvents = 0;
        int maxLead = MaxLead(companies);

        for (int turn = 2; turn <= maxTurn; turn++)
        {
            double r = rng.NextDouble();
            int n = r < NewsTuning.EventsPerTurnZero ? 0 : r < 1.0 - NewsTuning.EventsPerTurnTwo ? 1 : 2;

            // 大事件の保証: 12ターン以上「大」が無ければ、このターンは大を1件置く
            bool forceLarge = turn - largeSince >= 12;
            if (forceLarge && n == 0) n = 1;

            // 連載を始めるか: 同時進行2本まで、1周の事象に占める連載の割合を目標（15〜20%）へ寄せる
            int active = 0;
            foreach (var kv in arcBusyUntil) if (kv.Value >= turn) active++;
            double share = (double)arcEvents / Math.Max(1, arcEvents + singleEvents);
            if (arcLength.Count > 0 && active < NewsTuning.ArcMaxConcurrent && share < NewsTuning.ArcShareTarget
                && rng.NextDouble() < NewsTuning.ArcStartChance)
            {
                var start = PickArcStart(rng, turn, events, idx, world, lastPlaced, arcBusyUntil);
                if (start != null)
                {
                    lastPlaced[start.data.Family] = turn;
                    int len = arcLength.TryGetValue(start.data.arcId, out var al) ? al : 1;
                    arcEvents += len;
                    arcBusyUntil[start.data.arcId] = turn + (len - 1) * (NewsTuning.ArcGapMax + maxLead);
                    outEvents.Add(start.ev);
                    AddReports(rng, start.ev, companies, idx, pending, maxTurn);
                    QueueNextArcSlot(seed, maxTurn, start.data, start.ev, companies, events, idx, world, outEvents, pending, arcSlots, null);
                    n = Math.Max(0, n - 1);
                }
            }

            for (int k = 0; k < n; k++)
            {
                var ev = PickAndInstantiate(rng, turn, events, idx, world, lastPlaced, lastBinding,
                    requireLarge: forceLarge && k == 0, rare: rareCredit);
                if (ev == null && forceLarge && k == 0)
                    ev = PickAndInstantiate(rng, turn, events, idx, world, lastPlaced, lastBinding, requireLarge: false, rare: rareCredit);
                if (ev == null) continue;

                if (ev.scaleRank >= 3) largeSince = turn;
                singleEvents++;
                outEvents.Add(ev);
                AddReports(rng, ev, companies, idx, pending, maxTurn);
            }
        }
    }

    private static NewsEventInstance PickAndInstantiate(
        Random rng, int turn, IReadOnlyList<NewsEventData> events, TemplateIndex idx, NewsWorld world,
        Dictionary<string, int> lastPlaced, Dictionary<string, int> lastBinding, bool requireLarge, RareCredit rare)
    {
        var candidates = new List<NewsEventData>();
        foreach (var e in events)
        {
            if (e.weight <= 0) continue;
            if (e.IsArc) continue;   // 連載は第1話も含めて通常の抽選に混ぜない（PickArcStart で置く）
            if (requireLarge && e.ScaleRank < 3) continue;
            if (!idx.HasReports(e.eventId)) continue;
            // cooldown は系統（family）単位。次ダンジョン別に分けた事象が続けて出ないように
            if (lastPlaced.TryGetValue(e.Family, out var last) && turn - last < Math.Max(1, e.cooldown)) continue;
            if (!ConditionOk(e.condition, turn, world)) continue;
            candidates.Add(e);
        }

        // 難易度とレア（§16）: まずレアかどうか（別枠）、次に難易度の層を目標の割合で選び、その層の中で weight 抽選
        var all = candidates;
        candidates = ChooseTier(rng, all, requireLarge, rare);
        int total = 0;
        foreach (var c in candidates) total += c.weight;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (candidates.Count == 0)
            {
                // 選んだ層が尽きたら（ダンジョンを差し込めない等）、レア以外の残り全体から選び直す
                if (ReferenceEquals(candidates, all)) break;
                all.RemoveAll(e => e.IsRare);
                candidates = all;
                total = 0;
                foreach (var c in candidates) total += c.weight;
                if (candidates.Count == 0) break;
            }
            int roll = rng.Next(Math.Max(1, total));
            NewsEventData pick = candidates[candidates.Count - 1];
            foreach (var c in candidates)
            {
                if (roll < c.weight) { pick = c; break; }
                roll -= c.weight;
            }

            var ev = Instantiate(rng, pick, turn, idx, world);
            if (ev == null)
            {
                candidates.Remove(pick);
                total -= pick.weight;
                continue;
            }

            // 同じ具体化（パターン＋ダンジョン）は近いうちに繰り返さない
            ev.bindings.TryGetValue("dungeon", out var dn);
            string bindKey = pick.Family + "|" + dn;
            if (lastBinding.TryGetValue(bindKey, out var lb) && turn - lb < Math.Max(1, pick.cooldown) * 3)
            {
                candidates.Remove(pick);
                total -= pick.weight;
                continue;
            }

            lastPlaced[pick.Family] = turn;
            lastBinding[bindKey] = turn;
            rare.Count(pick);
            return ev;
        }
        return null;
    }

    /// <summary>
    /// 候補を「レア」か「難易度の1層」に絞る。目標: レア 5%（別枠）・易しい65% / 普通25% / 難しい10%。
    /// 選んだ層に候補が無ければ、候補のある層だけで割合を配り直す。乱数はシードから引くので再現できる。
    /// </summary>
    private static List<NewsEventData> ChooseTier(Random rng, List<NewsEventData> candidates, bool requireLarge, RareCredit credit)
    {
        var rare = candidates.FindAll(e => e.IsRare);
        var common = candidates.FindAll(e => !e.IsRare);

        // レアは「貯金」方式: 1件選ぶごとに RareEventRate ずつ貯まり、1 に届いたらレアを出す（出せなければ持ち越す）。
        // 貯金の初期値は周のシードで散らすので、どの周の何件目に出るかは周ごとに違う。周全体でほぼ 5% になる
        credit.value += NewsTuning.RareEventRate;
        if (!requireLarge && rare.Count > 0 && (common.Count == 0 || credit.value >= 1f))
        {
            credit.value = Math.Max(0f, credit.value - 1f);
            return rare;
        }
        if (common.Count == 0) return rare;

        var tiers = new[]
        {
            (key: "easy", share: NewsTuning.DifficultyEasyShare),
            (key: "normal", share: NewsTuning.DifficultyNormalShare),
            (key: "hard", share: NewsTuning.DifficultyHardShare),
        };
        // 目標の割合に対して一番足りていない層を選ぶ（cooldown で層が空いた周回の偏りを後で取り返す）。
        // 揺らぎを少し入れて、毎回同じ順で並ばないようにする
        string best = null;
        double bestScore = double.MinValue;
        foreach (var t in tiers)
        {
            if (!common.Exists(e => DifficultyKey(e) == t.key)) continue;
            int have = credit.tierCount.TryGetValue(t.key, out var c) ? c : 0;
            double score = t.share * (credit.tierTotal + 1) - have + rng.NextDouble() * 0.5;
            if (score > bestScore) { bestScore = score; best = t.key; }
        }
        return best != null ? common.FindAll(e => DifficultyKey(e) == best) : common;
    }

    /// <summary>レアの貯金（1周の生成の間だけ持つ）。</summary>
    private class RareCredit
    {
        public float value;
        public readonly Dictionary<string, int> tierCount = new();
        public int tierTotal;

        public void Count(NewsEventData e)
        {
            if (e.IsRare) return;
            var k = DifficultyKey(e);
            tierCount[k] = (tierCount.TryGetValue(k, out var n) ? n : 0) + 1;
            tierTotal++;
        }
    }

    /// <summary>易しい / 普通 / 難しい以外の値は易しい扱い。</summary>
    private static string DifficultyKey(NewsEventData e) =>
        e.difficulty == "normal" || e.difficulty == "hard" ? e.difficulty : "easy";

    private static NewsEventInstance Instantiate(Random rng, NewsEventData e, int effectTurn, TemplateIndex idx, NewsWorld world,
        Dictionary<string, string> inherit = null)
    {
        var ev = new NewsEventInstance
        {
            key = $"{e.eventId}@{effectTurn}",
            eventId = e.eventId,
            category = e.category,
            scaleRank = e.ScaleRank,
            effectTurn = effectTurn,
            hasResult = e.hasResult,
            difficulty = DifficultyKey(e),
            isRare = e.IsRare,
            arcId = e.arcId,
            arcStep = e.arcStep,
        };

        // 連載の続きは、前の話の差し込み枠（ダンジョン・地名など）を引き継ぐ
        if (inherit != null)
            foreach (var kv in inherit) ev.bindings[kv.Key] = kv.Value;

        // --- 差し込み枠 ---
        string dungeonKey = null;
        var binds = new Dictionary<string, string>(e.binds);
        bool needsDungeon = (e.targetRule ?? "").Contains("{dungeon}") || idx.UsesPlaceholder(e.eventId, "dungeon");
        if (needsDungeon && !binds.ContainsKey("dungeon")) binds["dungeon"] = "next";

        foreach (var kv in binds)
        {
            if (ev.bindings.ContainsKey(kv.Key)) continue;   // 引き継いだ値を優先
            if (kv.Key == "dungeon")
            {
                if (world == null || world.dungeons.Count == 0) return null;
                NewsWorld.Dungeon d;
                bool ok = kv.Value switch
                {
                    "next" => world.TryNextBattle(effectTurn, out d) || TryAny(rng, world, out d),
                    "any" or "" => TryAny(rng, world, out d),
                    _ => world.TryGet(kv.Value, out d),
                };
                if (!ok) return null;
                dungeonKey = d.key;
                ev.bindings["dungeon"] = d.name;
                ev.bindings["_weakness"] = d.weakness;
            }
            else
            {
                ev.bindings[kv.Key] = kv.Value is "pool" or "" or "any" ? FromPool(rng, kv.Key) : kv.Value;
            }
        }
        // テンプレートに出てくる残りの枠は、事象の単位でそろえる（同じ事象の社違いで地名が食い違わないように）
        foreach (var name in idx.PlaceholdersOf(e.eventId))
            if (!ev.bindings.ContainsKey(name) && name != "company")
                ev.bindings[name] = name == "season" ? SeasonOf(effectTurn) : FromPool(rng, name);

        // --- 対象（因果の3法則） ---
        if (!ApplyTargetRule(e.targetRule, ev, world, dungeonKey)) return null;

        // --- 効果量（目盛りのみ） ---
        float td = e.TrendDelta;
        ev.trendDelta = e.HasEffectRule ? td : 0f;
        ev.demandKick = e.HasEffectRule ? NewsTuning.DefaultDemandKick(td) : 0f;
        ev.durationTurns = NewsTuning.DefaultDuration(td);
        if (e.scale == "huge")
        {
            ev.demandKick = e.HasEffectRule ? NewsTuning.KickHuge * (td < 0f ? -1f : 1f) : 0f;
            ev.durationTurns = NewsTuning.DurationHuge;
        }

        // --- 真偽 ---
        float sum = e.wTrue + e.wExaggerated + e.wFalse;
        double t = rng.NextDouble() * (sum > 0 ? sum : 1);
        ev.truth = sum <= 0 || t < e.wTrue ? "true" : t < e.wTrue + e.wExaggerated ? "exaggerated" : "false";
        ev.effectScale = ev.truth == "false" ? 0f : ev.truth == "exaggerated" ? NewsTuning.ExaggeratedScale : 1f;
        return ev;
    }

    private static bool ApplyTargetRule(string rule, NewsEventInstance ev, NewsWorld world, string dungeonKey)
    {
        if (string.IsNullOrEmpty(rule)) return true;   // 効果なしの事象

        string weakness = ev.bindings.TryGetValue("_weakness", out var w) ? w : null;
        int colon = rule.IndexOf(':');
        string head = colon >= 0 ? rule.Substring(0, colon).Trim() : rule.Trim();
        string arg = colon >= 0 ? rule.Substring(colon + 1).Trim() : string.Empty;

        // L1 / L2 の引数がダンジョン名なら、その弱点を使う
        if ((head == "L1" || head == "L2") && arg.Length > 0 && arg != "{dungeon}" && world != null && world.TryGet(arg, out var d))
            weakness = d.weakness;

        switch (head)
        {
            case "L1":   // 攻: 弱点属性の武器
                if (string.IsNullOrEmpty(weakness)) return false;
                ev.targetAttribute = weakness;
                ev.targetType = "Weapon";
                return true;
            case "L2":   // 守: その攻撃を防ぐ属性の防具（防ぐ属性 = 弱点属性。items_sheet の説明文より）
                if (string.IsNullOrEmpty(weakness)) return false;
                ev.targetAttribute = weakness;
                ev.targetType = "Armor";
                return true;
            case "L3":   // 客: 高額帯
                ev.minRequiredLevel = NewsTuning.L3MinRequiredLevel;
                return true;
            case "attr":
            {
                var parts = arg.Split(',');
                ev.targetAttribute = parts[0].Trim();
                if (parts.Length > 1) ev.targetType = parts[1].Trim();
                return ev.targetAttribute.Length > 0;
            }
            case "type":
                ev.targetType = arg;
                return arg.Length > 0;
            case "item":
                ev.targetItemId = arg;
                return arg.Length > 0;
        }
        return false;
    }

    private static void AddReports(
        Random rng, NewsEventInstance ev, IReadOnlyList<NewspaperCompanyData> companies, TemplateIndex idx,
        Dictionary<(string, int), List<Pending>> pending, int maxTurn)
    {
        var candidates = new List<NewspaperCompanyData>();
        foreach (var c in companies)
            if (idx.ReportsOf(ev.eventId, c.companyId).Count > 0) candidates.Add(c);
        if (candidates.Count == 0) return;

        var reporters = new List<NewspaperCompanyData>();
        if (ev.IsFalse)
        {
            // 誤報は1社だけ。falseRate を重みに選ぶ（王立は 0 なので選ばれない）
            float sum = 0f;
            foreach (var c in candidates) sum += Math.Max(0f, c.falseRate);
            if (sum <= 0f)
            {
                ev.truth = "true";
                ev.effectScale = 1f;
            }
            else
            {
                double roll = rng.NextDouble() * sum;
                foreach (var c in candidates)
                {
                    roll -= Math.Max(0f, c.falseRate);
                    if (roll < 0) { reporters.Add(c); break; }
                }
                if (reporters.Count == 0) reporters.Add(candidates[candidates.Count - 1]);
            }
        }
        if (!ev.IsFalse)
        {
            // 報じられる日（発効 − lead）が周の中に収まる社だけが候補
            var able = candidates.FindAll(c => ev.effectTurn - Math.Max(0, c.leadTurns) >= 1);
            if (able.Count == 0) able = candidates;

            if (ev.isRare)
            {
                // レアは街中の大騒ぎ。報じうる社はすべて報じる
                reporters.AddRange(able);
            }
            else if (able.Count >= 2 && rng.NextDouble() >= NewsTuning.TrueSingleReportRate)
            {
                // 複数社が報じる（クロスリファレンスが成立する）。最低2社
                foreach (var c in able)
                    if (rng.NextDouble() < c.reportRate) reporters.Add(c);
                while (reporters.Count < 2)
                {
                    var rest = able.FindAll(c => !reporters.Contains(c));
                    if (rest.Count == 0) break;
                    reporters.Add(rest[rng.Next(rest.Count)]);
                }
            }
            else
            {
                // 本物でも1社だけが報じる（仕様 U2: 25%）。「同報なし＝誤報」と機械的に読めないように
                float sum = 0f;
                foreach (var c in able) sum += Math.Max(0.01f, c.reportRate);
                double roll = rng.NextDouble() * sum;
                foreach (var c in able)
                {
                    roll -= Math.Max(0.01f, c.reportRate);
                    if (roll < 0) { reporters.Add(c); break; }
                }
                if (reporters.Count == 0) reporters.Add(able[able.Count - 1]);
            }
        }

        bool loudUsed = false;
        foreach (var c in reporters)
        {
            int day = ev.effectTurn - Math.Max(0, c.leadTurns);
            if (day < 1 || day > maxTurn) continue;

            bool wantLoud = ev.truth == "false" || (ev.truth == "exaggerated" && (!loudUsed || rng.NextDouble() < 0.5));
            var tmpl = idx.PickReport(rng, ev.eventId, c.companyId, wantLoud);
            if (tmpl == null) continue;
            if (tmpl.IsLoud) loudUsed = true;

            Add(pending, c.companyId, day, new Pending
            {
                template = tmpl,
                ev = ev,
                priority = ev.scaleRank * 10 + (ev.HasTarget ? 1 : 0),
                entry = new NewsIssueEntry
                {
                    publishTurn = day,
                    effectTurn = ev.effectTurn,
                    companyId = c.companyId,
                    articleId = tmpl.templateId,
                    kind = NewsEntryKind.Report,
                    eventInstance = ev,
                    isFalseReport = ev.IsFalse,
                    effectScale = ev.effectScale,
                },
            });

            // 続報: 本物・誇張は結果記事、誤報は訂正記事。報じた社自身が出す
            NewsTemplateData follow = null;
            int fday;
            NewsEntryKind kind;
            if (ev.IsFalse)
            {
                follow = idx.FollowUpOf(ev.eventId, c.companyId, "correction");
                fday = ev.SettleTurn + NewsTuning.CorrectionDelay;
                kind = NewsEntryKind.Correction;
            }
            else
            {
                if (ev.hasResult) follow = idx.FollowUpOf(ev.eventId, c.companyId, "result");
                fday = ev.SettleTurn + NewsTuning.ResultDelay;
                kind = NewsEntryKind.Result;
            }
            if (follow == null || fday > maxTurn) continue;

            Add(pending, c.companyId, fday, new Pending
            {
                template = follow,
                ev = ev,
                priority = kind == NewsEntryKind.Result ? 0 : -50,
                entry = new NewsIssueEntry
                {
                    publishTurn = fday,
                    effectTurn = fday,
                    companyId = c.companyId,
                    articleId = follow.templateId,
                    kind = kind,
                    eventInstance = ev,
                    parentArticleId = ev.eventId,
                    isFalseReport = false,
                    effectScale = 0f,
                },
            });
        }
    }

    // =====================================================================
    // 連載（ストーリーアーク。§17）
    // =====================================================================

    private class ArcStart
    {
        public NewsEventData data;
        public NewsEventInstance ev;
    }

    private static int MaxLead(IReadOnlyList<NewspaperCompanyData> companies)
    {
        int m = 0;
        foreach (var c in companies) m = Math.Max(m, c.leadTurns);
        return m;
    }

    private static ArcStart PickArcStart(
        Random rng, int turn, IReadOnlyList<NewsEventData> events, TemplateIndex idx, NewsWorld world,
        Dictionary<string, int> lastPlaced, Dictionary<string, int> arcBusyUntil)
    {
        var cands = new List<NewsEventData>();
        int total = 0;
        foreach (var e in events)
        {
            if (!e.IsArcStart || e.weight <= 0 || !idx.HasReports(e.eventId)) continue;
            if (arcBusyUntil.TryGetValue(e.arcId, out var busy) && busy >= turn) continue;
            if (lastPlaced.TryGetValue(e.Family, out var last) && turn - last < Math.Max(1, e.cooldown)) continue;
            if (!ConditionOk(e.condition, turn, world)) continue;
            cands.Add(e);
            total += e.weight;
        }
        for (int attempt = 0; attempt < 4 && cands.Count > 0; attempt++)
        {
            int roll = rng.Next(Math.Max(1, total));
            var pick = cands[cands.Count - 1];
            foreach (var c in cands) { if (roll < c.weight) { pick = c; break; } roll -= c.weight; }
            var ev = Instantiate(rng, pick, turn, idx, world);
            if (ev != null) return new ArcStart { data = pick, ev = ev };
            cands.Remove(pick);
            total -= pick.weight;
        }
        return null;
    }

    /// <summary>
    /// 次の話の予約枠を作る。分岐が rand / always だけなら、その場で（シードから）決めて続きを置く。
    /// pending が null のとき（ゲーム中の確定）は、組み上がった紙面へ直接差し込む（calendar に追加）。
    /// </summary>
    private static void QueueNextArcSlot(
        int seed, int maxTurn, NewsEventData data, NewsEventInstance ev,
        IReadOnlyList<NewspaperCompanyData> companies, IReadOnlyList<NewsEventData> events,
        TemplateIndex idx, NewsWorld world, List<NewsEventInstance> outEvents,
        Dictionary<(string, int), List<Pending>> pending, List<NewsArcSlot> arcSlots, List<NewsIssueEntry> calendar)
    {
        if (data == null || !data.IsArc || data.arcNext.Count == 0) return;

        var r = new Random(StableHash($"{seed}|{ev.key}|gap"));
        int decision = ev.effectTurn + 1 + r.Next(Math.Max(1, NewsTuning.ArcGapMax));
        int effect = decision + MaxLead(companies);
        if (effect > maxTurn) return;

        int step = Math.Max(1, data.arcStep) + 1;
        var slot = new NewsArcSlot
        {
            key = $"{data.arcId}#{step}@{decision}",
            arcId = data.arcId,
            step = step,
            prevEventKey = ev.key,
            decisionTurn = decision,
            effectTurn = effect,
            options = new List<(string, string)>(data.arcNext),
        };
        arcSlots.Add(slot);

        // 状態に依らない分岐（rand / always）は事前に決めてよい
        bool stateFree = slot.options.TrueForAll(o => o.cond == "rand" || o.cond == "always");
        if (!stateFree) return;

        string chosen = ChooseArcBranch(seed, slot, null, null);
        ApplyArcBranch(seed, maxTurn, slot, chosen, companies, events, idx, world, outEvents, pending, arcSlots, calendar);
    }

    /// <summary>
    /// 分岐を選ぶ。状態の条件（heroWin / heroLose / hasStock / noStock）に合うものを並び順で優先し、
    /// 無ければ rand の中からシードで抽選、それも無ければ always、最後は候補全体からシードで抽選。
    /// </summary>
    public static string ChooseArcBranch(int seed, NewsArcSlot slot, bool? heroWon, Func<bool> hasStock)
    {
        if (slot.options.Count == 0) return "-";
        // 在庫の判定は1回だけ評価する（hasStock / noStock の両方で同じ答えを使う）
        bool? stockCache = null;
        bool HasStock() => stockCache ??= hasStock != null && hasStock();
        foreach (var o in slot.options)
        {
            bool hit = o.cond switch
            {
                "heroWin" => heroWon == true,
                "heroLose" => heroWon == false,
                "hasStock" => hasStock != null && HasStock(),
                "noStock" => hasStock != null && !HasStock(),
                _ => false,
            };
            if (hit) return o.eventId;
        }
        var rands = slot.options.FindAll(o => o.cond == "rand");
        if (rands.Count > 0) return rands[new Random(StableHash($"{seed}|{slot.key}|rand")).Next(rands.Count)].eventId;
        var always = slot.options.Find(o => o.cond == "always");
        if (!string.IsNullOrEmpty(always.eventId)) return always.eventId;
        // どの条件にも当たらない（まだ配信していない等）ときは、先頭に偏らないようシードで選ぶ
        return slot.options[new Random(StableHash($"{seed}|{slot.key}|fallback")).Next(slot.options.Count)].eventId;
    }

    private static void ApplyArcBranch(
        int seed, int maxTurn, NewsArcSlot slot, string eventId,
        IReadOnlyList<NewspaperCompanyData> companies, IReadOnlyList<NewsEventData> events,
        TemplateIndex idx, NewsWorld world, List<NewsEventInstance> outEvents,
        Dictionary<(string, int), List<Pending>> pending, List<NewsArcSlot> arcSlots, List<NewsIssueEntry> calendar)
    {
        slot.resolvedEventId = string.IsNullOrEmpty(eventId) ? "-" : eventId;
        NewsEventData data = null;
        foreach (var e in events) if (e.eventId == eventId) { data = e; break; }
        if (data == null) { slot.resolvedEventId = "-"; return; }

        NewsEventInstance prev = null;
        foreach (var e in outEvents) if (e.key == slot.prevEventKey) { prev = e; break; }

        var r = new Random(StableHash($"{seed}|{slot.key}|ev"));
        var ev = Instantiate(r, data, slot.effectTurn, idx, world, prev?.bindings);
        if (ev == null) { slot.resolvedEventId = "-"; return; }
        ev.arcId = slot.arcId;
        ev.arcStep = slot.step;
        outEvents.Add(ev);

        if (pending != null)
        {
            AddReports(r, ev, companies, idx, pending, maxTurn);
        }
        else if (calendar != null)
        {
            var local = new Dictionary<(string, int), List<Pending>>();
            AddReports(r, ev, companies, idx, local, maxTurn);
            var keys = new List<(string, int)>(local.Keys);
            keys.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : string.CompareOrdinal(a.Item1, b.Item1));
            foreach (var k in keys)
            {
                var company = FindCompany(companies, k.Item1);
                if (company != null) InsertIntoIssue(company, k.Item2, local[k], calendar);
            }
        }

        QueueNextArcSlot(seed, maxTurn, data, ev, companies, events, idx, world, outEvents, pending, arcSlots, calendar);
    }

    /// <summary>
    /// ゲーム中に分岐が決まったとき、組み上がった紙面へ話を差し込む（§17）。
    /// 号の本数が上限を超える分は、その号の埋め草を外して空ける。
    /// </summary>
    public static void ResolveArcSlot(
        int seed, int maxTurn, NewsArcSlot slot, string eventId,
        IReadOnlyList<NewspaperCompanyData> companies, IReadOnlyList<NewsEventData> events,
        IReadOnlyList<NewsTemplateData> templates, NewsWorld world,
        List<NewsEventInstance> outEvents, List<NewsIssueEntry> calendar, List<NewsArcSlot> arcSlots)
    {
        var idx = new TemplateIndex(templates ?? (IReadOnlyList<NewsTemplateData>)Array.Empty<NewsTemplateData>());
        ApplyArcBranch(seed, maxTurn, slot, eventId, companies, events, idx, world, outEvents, null, arcSlots, calendar);
    }

    private static NewspaperCompanyData FindCompany(IReadOnlyList<NewspaperCompanyData> companies, string id)
    {
        foreach (var c in companies) if (c.companyId == id) return c;
        return null;
    }

    private static void InsertIntoIssue(NewspaperCompanyData company, int day, List<Pending> items, List<NewsIssueEntry> calendar)
    {
        items.Sort((a, b) => b.priority.CompareTo(a.priority));
        foreach (var p in items)
        {
            var existing = calendar.FindAll(e => e.publishTurn == day && e.companyId == company.companyId);
            if (p.entry.kind != NewsEntryKind.Correction)
            {
                int count = existing.FindAll(e => e.kind != NewsEntryKind.Correction).Count;
                if (count >= Math.Max(1, company.issueMax))
                {
                    var filler = existing.FindLast(e => e.kind == NewsEntryKind.Filler);
                    if (filler != null) { calendar.Remove(filler); existing.Remove(filler); }
                }
            }

            var used = new Dictionary<string, int>();
            bool reportOnFront = false;
            foreach (var e in existing)
            {
                var pg = e.Article?.page ?? "front";
                used[pg] = (used.TryGetValue(pg, out var n) ? n : 0) + 1;
                if (pg == "front" && e.kind == NewsEntryKind.Report) reportOnFront = true;
            }
            bool Free(string pg) => company.HasPage(pg) && (used.TryGetValue(pg, out var n) ? n : 0) < company.SlotOf(pg);

            string page = null;
            if (p.entry.kind == NewsEntryKind.Correction) page = company.HasPage("rumor") ? "rumor" : "front";
            else if (p.ev != null && p.ev.IsFalse && p.entry.kind == NewsEntryKind.Report)
            {
                if (Free("rumor")) page = "rumor";
                page ??= company.pages.Find(pg => pg != "front" && Free(pg));
            }
            if (page == null && p.entry.kind == NewsEntryKind.Report && !reportOnFront) page = "front";
            if (page == null && !string.IsNullOrEmpty(p.template?.page) && Free(p.template.page)) page = p.template.page;
            if (page == null) { var cp = NewsTuning.PageOfCategory(p.ev?.category); if (Free(cp)) page = cp; }
            page ??= company.pages.Find(Free);
            page ??= company.pages.Count > 0 ? company.pages[0] : "front";

            Render(p, company, day, page);
            calendar.Add(p.entry);
        }
    }

    private static void Add(Dictionary<(string, int), List<Pending>> map, string company, int day, Pending p)
    {
        if (!map.TryGetValue((company, day), out var list)) map[(company, day)] = list = new List<Pending>();
        list.Add(p);
    }

    // =====================================================================
    // 4. 埋め草
    // =====================================================================

    private static NewsFillerData PickFiller(
        Random rng, IReadOnlyList<NewsFillerData> fillers, string company, int day,
        Dictionary<(string, string), int> lastUse, HashSet<string> usedToday)
    {
        if (fillers.Count == 0) return null;

        bool CooldownOk(NewsFillerData f) =>
            !lastUse.TryGetValue((company, f.fillerId), out var last)
            || day - last >= Math.Max(1, f.cooldown > 0 ? f.cooldown : NewsTuning.FillerDefaultCooldown);

        // 1. その社向け・間隔OK → 2. 他社向けを借りる・間隔OK → 3. その社向けで一番古いもの
        var pick = WeightedPick(rng, fillers, f => f.AllowedFor(company) && CooldownOk(f) && !usedToday.Contains(f.fillerId));
        pick ??= WeightedPick(rng, fillers, f => CooldownOk(f) && !usedToday.Contains(f.fillerId));
        if (pick != null) return pick;

        NewsFillerData oldest = null;
        int oldestDay = int.MaxValue;
        foreach (var f in fillers)
        {
            if (usedToday.Contains(f.fillerId)) continue;
            int last = lastUse.TryGetValue((company, f.fillerId), out var l) ? l : int.MinValue;
            if (last < oldestDay) { oldestDay = last; oldest = f; }
        }
        return oldest;
    }

    private static NewsFillerData WeightedPick(Random rng, IReadOnlyList<NewsFillerData> list, Func<NewsFillerData, bool> ok)
    {
        int total = 0;
        foreach (var f in list) if (ok(f)) total += Math.Max(1, f.weight);
        if (total <= 0) return null;
        int roll = rng.Next(total);
        foreach (var f in list)
        {
            if (!ok(f)) continue;
            roll -= Math.Max(1, f.weight);
            if (roll < 0) return f;
        }
        return null;
    }

    // =====================================================================
    // 5. 面の割り当てと記事の組み立て
    // =====================================================================

    private static void AssignPagesAndRender(
        NewspaperCompanyData company, int day, List<Pending> body, List<Pending> corrections, List<NewsIssueEntry> output)
    {
        var used = new Dictionary<string, int>();
        bool Free(string p) => company.HasPage(p) && (used.TryGetValue(p, out var n) ? n : 0) < company.SlotOf(p);
        void Take(string p) => used[p] = (used.TryGetValue(p, out var n) ? n : 0) + 1;
        string FirstFree(bool allowFront)
        {
            foreach (var p in company.pages)
                if ((allowFront || p != "front") && Free(p)) return p;
            return null;
        }
        string Fallback() => company.pages.Count > 0 ? company.pages[0] : "front";

        bool topReportPlaced = false;
        foreach (var p in body)
        {
            string page = null;
            if (p.entry.kind == NewsEntryKind.Filler)
            {
                // 報道の無い日は一面が空かないよう、最初の埋め草を一面に置く
                if (!topReportPlaced && Free("front")) { page = "front"; topReportPlaced = true; }
                if (page == null && !string.IsNullOrEmpty(p.filler.page) && Free(p.filler.page)) page = p.filler.page;
                page ??= FirstFree(allowFront: false);
                page ??= Free("front") ? "front" : null;
            }
            else
            {
                string tp = p.template?.page;
                // 誤報の一部（FalseReportHideRate）だけ目立たない面へ。全部を隠すと載り方だけで見抜けてしまう
                bool isFalse = p.entry.kind == NewsEntryKind.Report && p.ev != null && p.ev.IsFalse
                    && StableHash(p.ev.key + "|page") % 100 < (int)(NewsTuning.FalseReportHideRate * 100);
                if (isFalse)
                {
                    // 誤報は目立たない面へ寄せる（うわさ欄 → 一面以外の面）。手がかりを「社」ではなく「載り方」に置く
                    if (Free("rumor")) page = "rumor";
                    if (page == null)
                    {
                        string cp = NewsTuning.PageOfCategory(p.ev.category);
                        if (cp != "front" && Free(cp)) page = cp;
                    }
                    page ??= FirstFree(allowFront: false);
                }
                // 本物の報道は、その日の最初の1本を一面に（かわら版の本物も一面に載る）
                if (page == null && p.entry.kind == NewsEntryKind.Report && !topReportPlaced && Free("front")) page = "front";
                if (page == null && !string.IsNullOrEmpty(tp) && Free(tp)) page = tp;
                if (page == null)
                {
                    string cp = NewsTuning.PageOfCategory(p.ev?.category);
                    if (Free(cp)) page = cp;
                }
                page ??= Free("front") ? "front" : null;
                page ??= FirstFree(allowFront: true);
                if (p.entry.kind == NewsEntryKind.Report) topReportPlaced = true;
            }
            page ??= Fallback();
            Take(page);
            Render(p, company, day, page);
            output.Add(p.entry);
        }

        foreach (var p in corrections)
        {
            Render(p, company, day, company.HasPage("rumor") ? "rumor" : Fallback());
            output.Add(p.entry);
        }
    }

    private static void Render(Pending p, NewspaperCompanyData company, int day, string page)
    {
        var e = p.entry;
        var ev = p.ev;
        // 掲載ごとの乱数（再現性のため、シードではなく掲載の中身から作る）
        var local = new Random(StableHash($"{company.companyId}|{day}|{e.articleId}|{ev?.key}"));

        // 差し込み: 事象の値 → 無ければその場でプールから
        var binds = ev != null ? ev.bindings : new Dictionary<string, string>();
        string Fill(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            return Placeholder.Replace(s, m =>
            {
                var name = m.Groups[1].Value;
                if (name == "company") return company.companyName;
                if (binds.TryGetValue(name, out var v)) return v;
                if (name == "season") return SeasonOf(day);
                if (p.filler != null) return FromPool(local, name);
                return m.Value;
            });
        }

        string headline, lead, bodyText, summary, category, clarity;
        if (p.filler != null)
        {
            headline = Fill(p.filler.headline);
            lead = Fill(p.filler.lead);
            bodyText = Fill(p.filler.body);
            summary = Fill(p.filler.summaryEn);
            category = "filler";
            clarity = PickClarity(local, company, null);
        }
        else
        {
            var t = p.template;
            headline = Fill(t.headline);
            lead = Fill(t.lead);
            bodyText = Fill(t.body);
            summary = Fill(t.summaryEn);
            category = e.kind == NewsEntryKind.Correction ? "correction" : ev?.category;
            clarity = e.kind == NewsEntryKind.Correction ? "confirmed" : PickClarity(local, company, t.clarity);
            // 誤報は半段あいまいにずらす（決め打ちで見抜けないよう、社の分布の上で）
            if (ev != null && ev.IsFalse && e.kind == NewsEntryKind.Report && string.IsNullOrEmpty(t.clarity))
                clarity = ShiftClarity(clarity);
        }

        var article = new NewsArticleData
        {
            id = e.articleId,
            category = category,
            page = page,
            sourceClarity = clarity,
            headline = headline,
            lead = lead,
            body = bodyText,
            byline = PickByline(company, ev, e, local),
            crossRefGroup = ev?.key,
            truth = ev != null ? ev.truth : "true",
            summaryEn = summary,
            weight = 1,
        };
        e.SetArticle(article);

        // 跳ね（1段目）: 報道の日ごと・同じ事象は1日1回（その日の一番大きい面）
        if (ev != null && e.kind == NewsEntryKind.Report && ev.HasTarget && ev.trendDelta != 0f)
            // 誤報は目立たない面に載せるが、噂は広まるので跳ねは一面並みにする（高値掴みの大きさを変えないため）
            ev.AddHype(day, NewsTuning.DefaultHypeRate(ev.IsFalse ? "front" : page, ev.trendDelta));
    }

    /// <summary>署名。同じ事象・同じ社は同じ記者（続報も）。署名率を外れたら無署名。</summary>
    private static string PickByline(NewspaperCompanyData c, NewsEventInstance ev, NewsIssueEntry e, Random local)
    {
        if (c.bylines.Count == 0) return string.Empty;
        var r = ev != null ? new Random(StableHash(ev.key + "|" + c.companyId)) : local;
        // 誤報は無署名に寄せる（記者が裏を取っていない）。本物は社の署名率どおり
        float rate = ev != null && ev.IsFalse && e.kind == NewsEntryKind.Report
            ? c.bylineRate * NewsTuning.FalseReportBylineFactor
            : c.bylineRate;
        if (r.NextDouble() >= rate) return string.Empty;
        return c.bylines[r.Next(c.bylines.Count)];
    }

    private static string PickClarity(Random r, NewspaperCompanyData c, string fixedValue)
    {
        if (!string.IsNullOrEmpty(fixedValue)) return fixedValue;
        float sum = c.clarityConfirmed + c.clarityPresumed + c.clarityAnonymous;
        if (sum <= 0f) return "presumed";
        double x = r.NextDouble() * sum;
        if (x < c.clarityConfirmed) return "confirmed";
        if (x < c.clarityConfirmed + c.clarityPresumed) return "presumed";
        return "anonymous";
    }

    private static string ShiftClarity(string c)
    {
        int i = Array.IndexOf(ClarityOrder, c);
        return i < 0 ? "presumed" : ClarityOrder[Math.Min(ClarityOrder.Length - 1, i + 1)];
    }

    // =====================================================================
    // 補助
    // =====================================================================

    /// <summary>季節はターンで決める（紙面ごとに季節が食い違わないように）。10ターンで次の季節。</summary>
    private static string SeasonOf(int turn) =>
        NewsTuning.Seasons[((Math.Max(1, turn) - 1) / 10) % NewsTuning.Seasons.Length];

    private static bool TryAny(Random rng, NewsWorld world, out NewsWorld.Dungeon d)
    {
        if (world == null || world.dungeons.Count == 0) { d = default; return false; }
        d = world.dungeons[rng.Next(world.dungeons.Count)];
        return true;
    }

    private static string FromPool(Random rng, string name)
    {
        string[] pool = name switch
        {
            "region" => NewsTuning.Regions,
            "village" => NewsTuning.Villages,
            "season" => NewsTuning.Seasons,
            "monster" => NewsTuning.Monsters,
            "hero" => new[] { "勇者" },
            _ => null,
        };
        return pool != null && pool.Length > 0 ? pool[rng.Next(pool.Length)] : "{" + name + "}";
    }

    /// <summary>
    /// 出現条件。nextDungeon=X / turn>=N / turn<=N を判定する。
    /// heroLevel / shopLevel は発行カレンダーを組む時点では分からないので通す（周の途中で変わるため）。
    /// 複数は「;」か「&amp;」で区切る（すべて満たす）。
    /// </summary>
    private static bool ConditionOk(string condition, int turn, NewsWorld world)
    {
        if (string.IsNullOrEmpty(condition)) return true;
        foreach (var raw in condition.Split(';', '&'))
        {
            var c = raw.Trim();
            if (c.Length == 0) continue;
            if (c.StartsWith("nextDungeon="))
            {
                var want = c.Substring("nextDungeon=".Length).Trim();
                if (world == null || !world.TryNextBattle(turn, out var d)) return false;
                if (d.key != want && d.name != want) return false;
            }
            else if (c.StartsWith("turn>=") && int.TryParse(c.Substring(6), out var a))
            {
                if (turn < a) return false;
            }
            else if (c.StartsWith("turn<=") && int.TryParse(c.Substring(6), out var b))
            {
                if (turn > b) return false;
            }
        }
        return true;
    }

    /// <summary>実行環境に依存しない文字列ハッシュ（FNV-1a）。</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    /// <summary>テンプレートを事象・社・種類で引けるようにまとめたもの。</summary>
    private class TemplateIndex
    {
        private readonly Dictionary<(string ev, string company, string kind), List<NewsTemplateData>> map = new();
        private readonly Dictionary<string, HashSet<string>> placeholders = new();
        private readonly HashSet<string> withReports = new();

        public TemplateIndex(IReadOnlyList<NewsTemplateData> templates)
        {
            foreach (var t in templates)
            {
                string company = t.IsAnyCompany ? NewsTemplateData.AnyCompany : t.companyId;
                var key = (t.eventId, company, t.kind);
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<NewsTemplateData>();
                list.Add(t);
                if (t.kind == "report" && !t.IsAnyCompany) withReports.Add(t.eventId);

                if (!placeholders.TryGetValue(t.eventId, out var set)) placeholders[t.eventId] = set = new HashSet<string>();
                foreach (var s in new[] { t.headline, t.lead, t.body, t.summaryEn })
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    foreach (Match m in Placeholder.Matches(s)) set.Add(m.Groups[1].Value);
                }
            }
        }

        public bool HasReports(string eventId) => withReports.Contains(eventId);

        public bool UsesPlaceholder(string eventId, string name) =>
            placeholders.TryGetValue(eventId, out var s) && s.Contains(name);

        public IEnumerable<string> PlaceholdersOf(string eventId) =>
            placeholders.TryGetValue(eventId, out var s) ? s : (IEnumerable<string>)Array.Empty<string>();

        public List<NewsTemplateData> ReportsOf(string eventId, string company) =>
            map.TryGetValue((eventId, company, "report"), out var l) ? l : new List<NewsTemplateData>();

        public NewsTemplateData PickReport(Random rng, string eventId, string company, bool wantLoud)
        {
            var list = ReportsOf(eventId, company);
            if (list.Count == 0) return null;
            var prefer = list.FindAll(t => t.IsLoud == wantLoud);
            var from = prefer.Count > 0 ? prefer : list;
            return from[rng.Next(from.Count)];
        }

        /// <summary>続報。その社のもの → 社共通（*）の順に探す。</summary>
        public NewsTemplateData FollowUpOf(string eventId, string company, string kind)
        {
            if (map.TryGetValue((eventId, company, kind), out var l) && l.Count > 0) return l[0];
            if (map.TryGetValue((eventId, NewsTemplateData.AnyCompany, kind), out var a) && a.Count > 0) return a[0];
            return null;
        }
    }
}
