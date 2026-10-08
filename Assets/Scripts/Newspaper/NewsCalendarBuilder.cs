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
        PlaceEvents(rng, maxTurn, companies, events, idx, world, result.events, pending);

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
        Random rng, int maxTurn,
        IReadOnlyList<NewspaperCompanyData> companies,
        IReadOnlyList<NewsEventData> events,
        TemplateIndex idx, NewsWorld world,
        List<NewsEventInstance> outEvents,
        Dictionary<(string, int), List<Pending>> pending)
    {
        var lastPlaced = new Dictionary<string, int>();
        var lastBinding = new Dictionary<string, int>();
        int largeSince = 0;

        for (int turn = 2; turn <= maxTurn; turn++)
        {
            double r = rng.NextDouble();
            int n = r < NewsTuning.EventsPerTurnZero ? 0 : r < 1.0 - NewsTuning.EventsPerTurnTwo ? 1 : 2;

            // 大事件の保証: 12ターン以上「大」が無ければ、このターンは大を1件置く
            bool forceLarge = turn - largeSince >= 12;
            if (forceLarge && n == 0) n = 1;

            for (int k = 0; k < n; k++)
            {
                var ev = PickAndInstantiate(rng, turn, events, idx, world, lastPlaced, lastBinding,
                    requireLarge: forceLarge && k == 0);
                if (ev == null && forceLarge && k == 0)
                    ev = PickAndInstantiate(rng, turn, events, idx, world, lastPlaced, lastBinding, requireLarge: false);
                if (ev == null) continue;

                if (ev.scaleRank >= 3) largeSince = turn;
                outEvents.Add(ev);
                AddReports(rng, ev, companies, idx, pending, maxTurn);
            }
        }
    }

    private static NewsEventInstance PickAndInstantiate(
        Random rng, int turn, IReadOnlyList<NewsEventData> events, TemplateIndex idx, NewsWorld world,
        Dictionary<string, int> lastPlaced, Dictionary<string, int> lastBinding, bool requireLarge)
    {
        var candidates = new List<NewsEventData>();
        int total = 0;
        foreach (var e in events)
        {
            if (e.weight <= 0) continue;
            if (requireLarge && e.ScaleRank < 3) continue;
            if (!idx.HasReports(e.eventId)) continue;
            if (lastPlaced.TryGetValue(e.eventId, out var last) && turn - last < Math.Max(1, e.cooldown)) continue;
            if (!ConditionOk(e.condition, turn, world)) continue;
            candidates.Add(e);
            total += e.weight;
        }

        for (int attempt = 0; attempt < 6 && candidates.Count > 0; attempt++)
        {
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
            string bindKey = pick.eventId + "|" + dn;
            if (lastBinding.TryGetValue(bindKey, out var lb) && turn - lb < Math.Max(1, pick.cooldown) * 3)
            {
                candidates.Remove(pick);
                total -= pick.weight;
                continue;
            }

            lastPlaced[pick.eventId] = turn;
            lastBinding[bindKey] = turn;
            return ev;
        }
        return null;
    }

    private static NewsEventInstance Instantiate(Random rng, NewsEventData e, int effectTurn, TemplateIndex idx, NewsWorld world)
    {
        var ev = new NewsEventInstance
        {
            key = $"{e.eventId}@{effectTurn}",
            eventId = e.eventId,
            category = e.category,
            scaleRank = e.ScaleRank,
            effectTurn = effectTurn,
            hasResult = e.hasResult,
        };

        // --- 差し込み枠 ---
        string dungeonKey = null;
        var binds = new Dictionary<string, string>(e.binds);
        bool needsDungeon = (e.targetRule ?? "").Contains("{dungeon}") || idx.UsesPlaceholder(e.eventId, "dungeon");
        if (needsDungeon && !binds.ContainsKey("dungeon")) binds["dungeon"] = "next";

        foreach (var kv in binds)
        {
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

            if (able.Count >= 2 && rng.NextDouble() >= NewsTuning.TrueSingleReportRate)
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
