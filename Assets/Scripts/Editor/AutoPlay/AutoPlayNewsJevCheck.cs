using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 新聞記事の Jev 検証（Docs/News_Phase3_Spec.md §8）。エディタ専用・開発時のみ。
/// 発行カレンダーを本物の NewsModel / NewsCalendarBuilder で数周組み、実際に紙面へ載る形（差し込み済み）の記事を Jev に読ませる。
///   J1  解読可能性: 報道1本 / 同じ事象の報道全部 → 13択（属性6×種別2＋対象なし）＋L3 用に「高額帯」
///   J1' 埋め草: 同じ選択肢で「対象なし」に寄るか
///   J2  誤報の見抜きやすさ: 社名・面・sourceClarity・署名・他社の同報の数＋見出し/リード → 誤報らしさ（noul）
///   J3  社の個性: 署名と社名を伏せた本文 → 5社（各社の文体メモつき）
/// 日本語本文と summaryEn の両方で J1 を測る。結果は AutoPlayReports/news_jev_*/ に CSV と JSON。
/// </summary>
public static class AutoPlayNewsJevCheck
{
    public static bool IsRunning { get; private set; }
    public static string LastMessage { get; private set; } = "";
    public static string LastResultDir { get; private set; }
    public static long InputTokens { get; private set; }
    public static int Requests { get; private set; }

    private static readonly string[] Attributes = { "Fire", "Water", "Earth", "Wind", "Light", "Dark" };
    private static readonly string[] Types = { "Weapon", "Armor" };

    // プレイヤーが店（品物の説明文）から知りうる相性。Docs/News_Content_Review.md §2 の表からダンジョン名を抜いたもの
    private const string Context =
        "You read the morning newspaper in a fantasy shop game. Shop goods have an attribute (Fire, Water, Earth, Wind, Light, Dark) " +
        "and a type (Weapon or Armor). From the shop's item descriptions everyone knows: against FIRE monsters/heat, Water weapons " +
        "are effective and Water armor blocks the damage; against WATER/ICE monsters, Earth weapons (heavy crushing blows) are effective " +
        "and Earth armor blocks water attacks; against WIND monsters (beasts, plants of the forest), Fire weapons are effective and Fire " +
        "armor resists wind; against EARTH monsters (rock), Wind weapons are effective and Wind armor deflects earth; against DARK " +
        "monsters (undead, demons), Light weapons are effective and Light armor resists darkness; against LIGHT monsters (holy machines), " +
        "Dark weapons are effective and Dark armor resists light. Weapons sell when monsters must be killed; armor sells when people must " +
        "protect themselves. Rich adventurers buy expensive high-tier goods of any attribute. Articles describe only causes, never the goods.";

    private sealed class Row
    {
        public string Test, Id, Lang, Expected, Chosen, Note;
        public float PCorrect, PNone, MaxWrong;
        public string MaxWrongKey;
        public bool Pass;
    }

    [MenuItem("Tools/TomsLands/Jev 新聞記事の検証（J1〜J3）", priority = 3)]
    private static void RunFromMenu() => Start();

    /// <summary>非同期で開始（async void）。メインスレッドで同期待ちしないこと。</summary>
    public static async void Start(int seeds = 6, double costLimitUsd = 0.9, string only = null)
    {
        if (IsRunning) return;
        if (!JevApi.HasApiKey) { LastMessage = "TYPESAFE_API_KEY が無い"; return; }
        IsRunning = true;
        InputTokens = 0;
        Requests = 0;
        var rows = new List<Row>();
        var cts = new CancellationTokenSource();
        try
        {
            LastMessage = "準備中";
            NewsMasterLoader.ClearCache();
            var companies = NewsMasterLoader.LoadCompanies();
            var fillers = NewsMasterLoader.LoadFillers();
            var templates = NewsMasterLoader.LoadTemplates().ToDictionary(t => t.templateId, t => t);

            // --- 発行カレンダーを数周組む（本物の NewsModel） ---
            var entries = new List<NewsIssueEntry>();
            // カレンダーの組み立て・連載の分岐はセーブ（newsArcs.json）を書くので、隔離フォルダで行う
            string prevRoot = SaveSlotManager.DevRootOverride;
            SaveSlotManager.DevRootOverride = Path.Combine(Application.temporaryCachePath, "news_jev_check");
            Directory.CreateDirectory(SaveSlotManager.DevRootOverride);
            try
            {
                for (int s = 0; s < seeds; s++)
                {
                    var model = new NewsModel();
                    model.SetWorld(BuildWorld(s));
                    model.Build(9001 + s);
                    // 連載の分岐を毎朝決める（配信の勝敗・在庫はランダム）。決まった続きの話がカレンダーに入る
                    var arcRng = new System.Random(77 + s);
                    for (int t = 1; t <= NewsModel.CalendarTurns; t++)
                    {
                        if (t % 4 == 1 && t > 1) model.SetLastStreamResult(arcRng.NextDouble() < 0.5);
                        model.ResolveArcs(t, ev => arcRng.NextDouble() < 0.5);
                    }
                    entries.AddRange(model.Calendar);
                }
            }
            finally
            {
                SaveSlotManager.DevRootOverride = prevRoot;
            }

            var reports = entries.Where(e => e.kind == NewsEntryKind.Report && e.eventInstance != null && e.Article != null).ToList();
            var firstByTemplate = reports.GroupBy(e => e.articleId).Select(g => g.First()).ToList();
            // only に "revised" を含めると、reviewNote に「修正済み10/08(2)」「(3)」のある行だけを測る（報道＋結果文）
            bool revisedOnly = only != null && only.Contains("revised");
            HashSet<string> revisedIds = null;
            if (revisedOnly)
            {
                revisedIds = new HashSet<string>();
                foreach (var line in File.ReadAllLines(Path.Combine(Application.dataPath, "Resources_moved", "NewsTemplates.csv")))
                    if (line.Contains("修正済み10/08(2)") || line.Contains("修正済み10/08(3)")) revisedIds.Add(line.Split('	')[0]);
                var results = entries.Where(e => e.kind == NewsEntryKind.Result && e.eventInstance != null && e.Article != null)
                    .GroupBy(e => e.articleId).Select(g => g.First()).Where(e => revisedIds.Contains(e.articleId));
                firstByTemplate = firstByTemplate.Where(e => revisedIds.Contains(e.articleId)).Concat(results).ToList();
            }
            // only に "new" を含めると、難易度が easy 以外・レア・連載の事象の報道と結果文だけを測る（Note に 難易度/レア/連載）
            bool newOnly = only != null && only.Contains("new");
            var eventTags = new Dictionary<string, string>();
            foreach (var ev in NewsMasterLoader.LoadEvents())
            {
                var line = File.ReadAllLines(Path.Combine(Application.dataPath, "Resources_moved", "NewsEvents.csv"))
                    .FirstOrDefault(l => l.StartsWith(ev.eventId + "	"));
                if (line == null) continue;
                var cols = line.Split('	');
                var head = File.ReadLines(Path.Combine(Application.dataPath, "Resources_moved", "NewsEvents.csv")).First().Split('	');
                string Col(string n) { int i = Array.IndexOf(head, n); return i >= 0 && i < cols.Length ? cols[i] : ""; }
                string diff = Col("difficulty"), rar = Col("rarity"), arc = Col("arcId");
                eventTags[ev.eventId] = $"{(string.IsNullOrEmpty(diff) ? "easy" : diff)}/{(string.IsNullOrEmpty(rar) ? "common" : rar)}/{(string.IsNullOrEmpty(arc) ? "-" : "arc")}";
            }
            // only に "c1" を含めると、reviewNote に「修正10/09-c1」のある行も "new" の範囲に足す
            var c1Ids = new HashSet<string>();
            if (only != null && only.Contains("c1"))
                foreach (var line in File.ReadAllLines(Path.Combine(Application.dataPath, "Resources_moved", "NewsTemplates.csv")))
                    if (line.Contains("修正10/09-c1") || line.Contains("修正10/09-c2") || line.Contains("修正10/09-c3") || line.Contains("修正10/09-c4") || line.Contains("修正10/09-c5") || line.Contains("修正10/09-c6") || line.Contains("修正10/09-c7") || line.Contains("修正10/09-c8") || line.Contains("修正10/09-c9") || line.Contains("修正10/09-c10") || line.Contains("修正10/09-c11")) c1Ids.Add(line.Split('\t')[0]);
            bool InNewScope(NewsIssueEntry e) => e.eventInstance != null && (c1Ids.Contains(e.articleId) ||
                (eventTags.TryGetValue(e.eventInstance.eventId, out var tg) && tg != "easy/common/-"));
            if (newOnly)
            {
                var results = entries.Where(e => e.kind == NewsEntryKind.Result && e.eventInstance != null && e.Article != null)
                    .GroupBy(e => e.articleId).Select(g => g.First()).Where(InNewScope);
                firstByTemplate = firstByTemplate.Where(InNewScope).Concat(results).ToList();
                revisedOnly = true; // J1all・埋め草・J2 を省く
            }
            var fillerEntries = entries.Where(e => e.kind == NewsEntryKind.Filler && e.Article != null)
                .GroupBy(e => e.articleId).Select(g => g.First()).ToList();
            var unseen = templates.Values.Where(t => t.kind == "report" && !firstByTemplate.Any(e => e.articleId == t.templateId)).Select(t => t.templateId).ToList();

            if (only != null && !only.Contains("J1")) goto SkipJ1;
            // --- J1: 報道1本 ---
            foreach (var e in firstByTemplate)
            {
                var ev = e.eventInstance;
                foreach (var lang in new[] { "ja", "en" })
                {
                    string text = lang == "ja" ? Ja(e.Article) : e.Article.summaryEn;
                    if (string.IsNullOrEmpty(text)) continue;
                    var a = await AskTarget(new { context = Context, article = text }, "Based only on this article", cts.Token, costLimitUsd);
                    if (a == null) goto Done;
                    var r1 = ScoreTarget("J1", e.articleId, lang, ev, a, single: true);
                    if (eventTags.TryGetValue(ev.eventId, out var tag1)) r1.Note = tag1 + " " + r1.Note;
                    rows.Add(r1);
                }
            }

            if (revisedOnly) goto SkipJ1;
            // --- J1: 同じ事象の報道全部 ---
            foreach (var g in reports.GroupBy(e => e.eventInstance.eventId))
            {
                var inst = g.GroupBy(e => e.eventInstance.key).OrderByDescending(x => x.Select(y => y.companyId).Distinct().Count()).First().ToList();
                var ev = inst[0].eventInstance;
                foreach (var lang in new[] { "ja", "en" })
                {
                    var texts = inst.GroupBy(x => x.companyId).Select(x => x.First())
                        .Select(x => lang == "ja" ? Ja(x.Article) : x.Article.summaryEn).Where(t => !string.IsNullOrEmpty(t)).ToList();
                    var a = await AskTarget(new { context = Context, articles = texts }, "Based on all these articles about the same event", cts.Token, costLimitUsd);
                    if (a == null) goto Done;
                    var row = ScoreTarget("J1all", g.Key, lang, ev, a, single: false);
                    row.Note = $"{texts.Count}本";
                    rows.Add(row);
                }
            }

            // --- J1': 埋め草 ---
            foreach (var e in fillerEntries)
            {
                foreach (var lang in new[] { "ja", "en" })
                {
                    string text = lang == "ja" ? Ja(e.Article) : e.Article.summaryEn;
                    if (string.IsNullOrEmpty(text)) continue;
                    var a = await AskTarget(new { context = Context, article = text }, "Based only on this article", cts.Token, costLimitUsd);
                    if (a == null) goto Done;
                    float pNone = P(a, "none");
                    var top = a.Probabilities?.Where(kv => kv.Key != "none").OrderByDescending(kv => kv.Value).FirstOrDefault() ?? default;
                    rows.Add(new Row
                    {
                        Test = "J1filler", Id = e.articleId, Lang = lang, Expected = "none", Chosen = a.Choice,
                        PNone = pNone, PCorrect = pNone, MaxWrong = top.Value, MaxWrongKey = top.Key, Pass = pNone >= 0.6f,
                    });
                }
            }

        SkipJ1:
            if (only != null && !only.Contains("J2")) goto SkipJ2;
            // --- J2: 誤報の見抜きやすさ（本物と誤報を同数） ---
            var falses = reports.Where(e => e.eventInstance.truth == "false").Take(60).ToList();
            var trues = reports.Where(e => e.eventInstance.truth == "true").OrderBy(e => StableHash(e.Key)).Take(Math.Max(20, falses.Count)).ToList();
            foreach (var e in falses.Concat(trues))
            {
                int crossRefs = reports.Count(x => x.eventInstance.key == e.eventInstance.key && x.companyId != e.companyId
                                                   && Math.Abs(x.publishTurn - e.publishTurn) <= 3);
                var state = new
                {
                    context = "You judge whether a newspaper report is a false report (a rumor that will not come true). " +
                              "Clues: the paper's reputation, the page (front / second / market / rumor), the source clarity " +
                              "(confirmed / presumed / anonymous), whether the article is signed, and whether other papers report the same thing.",
                    paper = e.Company?.companyName ?? e.companyId,
                    paperTrust = e.Company?.trustRating ?? 0,
                    page = e.Article.page,
                    source = e.Article.sourceClarity,
                    signed = !string.IsNullOrEmpty(e.Article.byline),
                    otherPapersReportingSameEvent = crossRefs,
                    headline = e.Article.headline,
                    lead = e.Article.lead,
                };
                var req = new JevRequest { State = state };
                req.Questions["false"] = JevQuestion.Noul("Is this report likely to be a false report?", "It is likely false", "It is likely true");
                var resp = await Send(req, cts.Token, costLimitUsd);
                if (resp == null) goto Done;
                float p = resp.Answers.TryGetValue("false", out var ans) && ans.Noul.HasValue ? ans.Noul.Value : 0.5f;
                bool isFalse = e.eventInstance.truth == "false";
                rows.Add(new Row
                {
                    Test = "J2", Id = e.articleId, Lang = "ja", Expected = isFalse ? "false" : "true",
                    Chosen = p >= 0.5f ? "false" : "true", PCorrect = isFalse ? p : 1f - p,
                    Pass = (p >= 0.5f) == isFalse, Note = $"{e.companyId}/{e.Article.page}/{e.Article.sourceClarity}/signed={!string.IsNullOrEmpty(e.Article.byline)}/xref={crossRefs}",
                });
            }

        SkipJ2:
            if (only != null && !only.Contains("J3")) goto Done;
            // --- J3: 社の個性（社名・署名を伏せる） ---
            var companyOptions = companies.ToDictionary(c => c.companyId, c => $"{c.companyName}: {c.styleNote}");
            foreach (var e in firstByTemplate)
            {
                if (e.kind != NewsEntryKind.Report) continue;
                if (revisedOnly && !newOnly && e.companyId != "royal") continue;
                string text = Ja(e.Article);
                foreach (var c in companies) text = text.Replace(c.companyName, "（本紙）");
                foreach (var c in companies) foreach (var b in c.bylines) if (!string.IsNullOrEmpty(b)) text = text.Replace(b, "（記者）");
                var req = new JevRequest { State = new { article = text } };
                req.Questions["paper"] = JevQuestion.Choice("Which newspaper most likely wrote this article, judging by its writing style and topic?", companyOptions);
                var resp = await Send(req, cts.Token, costLimitUsd);
                if (resp == null) goto Done;
                resp.Answers.TryGetValue("paper", out var a);
                rows.Add(new Row
                {
                    Test = "J3", Id = e.articleId, Lang = "ja", Expected = e.companyId, Chosen = a?.Choice,
                    PCorrect = P(a, e.companyId), Pass = a?.Choice == e.companyId,
                });
            }

        Done:
            LastResultDir = Write(rows, unseen);
            LastMessage = $"完了: {rows.Count} 件 / {Requests} req / {InputTokens:N0} tokens / ${JevApi.EstimateCostUsd(InputTokens):F4} → {LastResultDir}";
            Debug.Log("[NewsJev] " + LastMessage);
        }
        catch (Exception e)
        {
            LastMessage = "失敗: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            IsRunning = false;
            cts.Dispose();
        }
    }

    // -----------------------------------------------------------------

    private static NewsWorld BuildWorld(int seed)
    {
        var w = new NewsWorld();
        var ds = AutoPlayLevelDesignTool.LoadDungeons().OrderBy(d => d.key.ToString()).ToList();
        foreach (var so in ds)
            w.dungeons.Add(new NewsWorld.Dungeon { key = so.key.ToString(), name = so.dungeonName, weakness = so.requiredAttribute.ToString() });
        // 4ターンおきの配信。周ごとに順番を回して、{dungeon} がいろいろなダンジョンになるようにする
        for (int t = 4, i = seed; t <= NewsModel.CalendarTurns; t += 4, i++)
            w.battles.Add((t, ds[i % ds.Count].key.ToString()));
        return w;
    }

    private static string Ja(NewsArticleData a) => $"{a.headline}\n{a.lead}\n{a.body}";

    private static Dictionary<string, string> TargetOptions()
    {
        var o = new Dictionary<string, string>();
        foreach (var at in Attributes)
            foreach (var ty in Types)
                o[$"{at}_{ty}"] = $"{at} {ty.ToLowerInvariant()}s will sell differently (more or less) because of this";
        o["any_Weapon"] = "Weapons in general (of every attribute) will sell differently";
        o["any_Armor"] = "Armor in general (of every attribute) will sell differently";
        o["high_end"] = "Expensive high-tier goods of any attribute will sell differently";
        o["none"] = "No particular goods are affected; this article has nothing to do with what the shop sells";
        return o;
    }

    private static async Task<JevAnswer> AskTarget(object state, string lead, CancellationToken ct, double limit)
    {
        var req = new JevRequest { State = state };
        req.Questions["target"] = JevQuestion.Choice(
            lead + ", which goods' demand will change? Consider only the cause described.", TargetOptions());
        var resp = await Send(req, ct, limit);
        if (resp == null) return null;
        return resp.Answers.TryGetValue("target", out var a) ? a : new JevAnswer();
    }

    private static async Task<JevResponse> Send(JevRequest req, CancellationToken ct, double limit)
    {
        if (JevApi.EstimateCostUsd(InputTokens) > limit)
        {
            LastMessage = "費用上限で中止";
            return null;
        }
        var resp = await JevApi.SendAsync(req, ct);
        Requests++;
        if (resp?.Usage != null) InputTokens += resp.Usage.InputTokens;
        LastMessage = $"{Requests} req / ${JevApi.EstimateCostUsd(InputTokens):F4}";
        return resp;
    }

    private static float P(JevAnswer a, string key) =>
        a?.Probabilities != null && a.Probabilities.TryGetValue(key, out var v) ? v : 0f;

    /// <summary>正解に当たる選択肢の集合（属性だけ・種別だけの対象は複数の選択肢の合計）。</summary>
    private static HashSet<string> CorrectKeys(NewsEventInstance ev)
    {
        var set = new HashSet<string>();
        if (ev == null || !ev.HasTarget) { set.Add("none"); return set; }
        if (ev.minRequiredLevel > 0 && string.IsNullOrEmpty(ev.targetAttribute) && string.IsNullOrEmpty(ev.targetType)) { set.Add("high_end"); return set; }
        string attr = ev.targetAttribute, type = ev.targetType;
        if (!string.IsNullOrEmpty(ev.targetItemId))
        {
            var item = AssetDatabase.FindAssets("t:ItemData").Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(i => i != null && i.itemId == ev.targetItemId);
            if (item != null) { attr = item.itemAttribute.ToString(); type = item.itemType.ToString(); }
        }
        foreach (var at in Attributes)
            foreach (var ty in Types)
                if ((string.IsNullOrEmpty(attr) || attr == at) && (string.IsNullOrEmpty(type) || type == ty)) set.Add($"{at}_{ty}");
        if (string.IsNullOrEmpty(attr) && !string.IsNullOrEmpty(type)) set.Add("any_" + type);
        if (string.IsNullOrEmpty(attr) && string.IsNullOrEmpty(type)) { set.Add("any_Weapon"); set.Add("any_Armor"); }
        return set;
    }

    private static Row ScoreTarget(string test, string id, string lang, NewsEventInstance ev, JevAnswer a, bool single)
    {
        var correct = CorrectKeys(ev);
        float pc = correct.Sum(k => P(a, k));
        var wrong = (a.Probabilities ?? new Dictionary<string, float>()).Where(kv => !correct.Contains(kv.Key)).OrderByDescending(kv => kv.Value).FirstOrDefault();
        float need = single ? 0.4f : 0.6f;
        return new Row
        {
            Test = test, Id = id, Lang = lang, Expected = string.Join("|", correct), Chosen = a.Choice,
            PCorrect = pc, PNone = P(a, "none"), MaxWrong = wrong.Value, MaxWrongKey = wrong.Key,
            Pass = pc >= need && wrong.Value <= 0.35f,
            Note = wrong.Value > 0.35f ? (wrong.Key == "none" ? "解読不能（対象なしに寄る）" : "ミスリード") : pc < need ? "解読不足" : "",
        };
    }

    private static string Write(List<Row> rows, List<string> unseen)
    {
        string dir = Path.Combine(AutoPlayBatch.ReportsRoot, "news_jev_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder("test,id,lang,expected,chosen,pCorrect,pNone,maxWrongKey,maxWrong,pass,note\n");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", r.Test, r.Id, r.Lang, r.Expected, r.Chosen, r.PCorrect.ToString("F3"), r.PNone.ToString("F3"),
                r.MaxWrongKey, r.MaxWrong.ToString("F3"), r.Pass ? 1 : 0, r.Note));
        File.WriteAllText(Path.Combine(dir, "results.csv"), sb.ToString(), new UTF8Encoding(true));

        var summary = new
        {
            requests = Requests,
            inputTokens = InputTokens,
            costUsd = JevApi.EstimateCostUsd(InputTokens),
            byTest = rows.GroupBy(r => r.Test + "/" + r.Lang).ToDictionary(g => g.Key, g => new
            {
                n = g.Count(), pass = g.Count(r => r.Pass), meanCorrect = g.Average(r => r.PCorrect),
            }),
            j2 = new
            {
                falseN = rows.Count(r => r.Test == "J2" && r.Expected == "false"),
                detected = rows.Count(r => r.Test == "J2" && r.Expected == "false" && r.Chosen == "false"),
                trueN = rows.Count(r => r.Test == "J2" && r.Expected == "true"),
                falsePositive = rows.Count(r => r.Test == "J2" && r.Expected == "true" && r.Chosen == "false"),
            },
            j3 = rows.Where(r => r.Test == "J3").GroupBy(r => r.Expected).ToDictionary(g => g.Key, g => new { n = g.Count(), hit = g.Count(r => r.Pass) }),
            unseenReportTemplates = unseen,
        };
        File.WriteAllText(Path.Combine(dir, "summary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented));
        return dir;
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s ?? "") h = h * 31 + c;
            return h;
        }
    }
}
