using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 配信コメントへの返事（PersonaCommentLines / PersonaCommentEngine）と、判断ごとの一言の増やした行（PersonaLines / ConsultLines）。
/// 一時ファイルはプロジェクトの Temp 以下（C ドライブに書かない）。
/// </summary>
public class PersonaCommentTests
{
    private string _dir;

    private static PersonaCommentFacts InRun => new()
    {
        InRun = true, Money = 27000, Day = 17, DebtDaysLeft = 2, DebtAmount = 50000, VillageFunds = 5264446, Runs = 91, LastStreamDelta = -8844,
    };

    private static PersonaCommentFacts Between => new() { InRun = false, VillageFunds = 5264446, Runs = 91 };

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Temp", "PersonaCommentTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); }
        catch (Exception) { /* 消せなくても Temp なので残ってよい */ }
    }

    private static PersonaComment C(string cat, string text, bool first = false, string amount = null) =>
        new() { Id = "t-" + Guid.NewGuid().ToString("N"), UnixMs = 0, Author = "たろう", Text = text, Cat = cat, First = first, SuperchatAmount = amount };

    // =====================================================
    // 返事の選び方
    // =====================================================

    [Test]
    public void EveryCat_Replies_InRunAndBetweenRuns()
    {
        var samples = new Dictionary<string, string[]>
        {
            ["greet"] = new[] { "こんにちは", "おはよう", "こんばんは", "初見です" },
            ["cheer"] = new[] { "がんばれ", "かわいい", "天才" },
            ["tease"] = new[] { "今日も金欠？", "破産しそう", "また外した", "へー" },
            ["bet_hero"] = new[] { "勇者に張れ" },
            ["bet_demon"] = new[] { "魔王いけ" },
            ["ask"] = new[] { "今いくら？", "何日目？", "返済いつ？", "村資金は？", "目標は？", "調子どう？" },
            ["superchat"] = new[] { "", "応援してます" },
            ["leave"] = new[] { "おやすみ", "おつ" },
            ["laugh"] = new[] { "草", "www" },
            ["clap"] = new[] { "8888", "おめでとう" },
            ["game"] = new[] { "何のゲーム？", "Steamにある？" },
            ["ai"] = new[] { "AIなの？", "中の人いる？" },
            ["toko"] = new[] { "トコちゃん" },
            ["other"] = new[] { "なるほど", "ずんだ餅食べたい" },
        };
        CollectionAssert.AreEquivalent(PersonaCommentLines.Cats, samples.Keys);
        foreach (var facts in new[] { InRun, Between })
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var rng = new System.Random(seed);
                foreach (var kv in samples)
                    foreach (var text in kv.Value)
                    {
                        var c = C(kv.Key, text, first: text == "初見です", amount: kv.Key == "superchat" ? "¥500" : null);
                        string cat = PersonaCommentLines.NormalizeCat(c);
                        Assert.AreEqual(kv.Key, cat, text);
                        var lines = PersonaCommentLines.Reply(c, cat, facts, rng, new Dictionary<string, int>(), new List<string>());
                        Assert.That(lines.Count, Is.InRange(1, 2), $"{cat} {text}");
                        foreach (var l in lines)
                        {
                            Assert.That(l.Speaker, Is.EqualTo("z").Or.EqualTo("m"));
                            Assert.IsFalse(string.IsNullOrWhiteSpace(l.Line));
                            Assert.IsFalse(l.Line.Contains("{") || l.Line.Contains("["), l.Line);
                            Assert.IsTrue(PersonaLines.IsClean(l.Line), l.Line);
                            Assert.IsFalse(l.Line.Contains("たろう"), "名前は一言に入れない");
                            if (!string.IsNullOrEmpty(text) && text.Length >= 6) Assert.IsFalse(l.Line.Contains(text), "本文は一言に入れない");
                            Assert.IsFalse(string.IsNullOrEmpty(l.E));
                            if (!facts.InRun) Assert.IsFalse(l.Line.Contains("日目"), "ランの合間に日を言わない: " + l.Line);
                        }
                        if (lines.Count == 2) Assert.AreNotEqual(lines[0].Speaker, lines[1].Speaker);
                    }
            }
        }
    }

    [Test]
    public void Classify_NewCats_FromText_AndKeepsOldCats()
    {
        var expect = new Dictionary<string, string>
        {
            // 前からの種類（変わらない）
            ["こんにちは"] = "greet", ["初見です"] = "greet", ["勇者に張れ"] = "bet_hero", ["魔王いけ"] = "bet_demon",
            ["今日も金欠？"] = "tease", ["また外した"] = "tease", ["今いくら？"] = "ask", ["がんばれ"] = "cheer", ["なるほど"] = "other",
            // 見送り
            ["おやすみ"] = "leave", ["おつ"] = "leave", ["またね"] = "leave", ["そろそろ落ちます"] = "leave", ["寝ます"] = "leave", ["ばいばい"] = "leave",
            // 笑い
            ["草"] = "laugh", ["www"] = "laugh", ["ｗｗｗ"] = "laugh", ["それはないｗ"] = "laugh", ["笑った"] = "laugh", ["わろた"] = "laugh",
            ["薬草売れ"] = "other",
            // 拍手
            ["8888"] = "clap", ["８８８８"] = "clap", ["おめでとう"] = "clap", ["ナイス"] = "clap", ["GG"] = "clap", ["うまい"] = "clap", ["やるやん"] = "clap",
            // ゲーム（宣伝）
            ["これ何のゲーム？"] = "game", ["タイトル教えて"] = "game", ["どこで買える？"] = "game", ["Steamにある？"] = "game",
            ["面白そう"] = "game", ["やってみたい"] = "game", ["ＳＴＥＡＭ"] = "game",
            // AI
            ["AIなの？"] = "ai", ["ＡＩ？"] = "ai", ["中の人いる？"] = "ai", ["自動で動いてる？"] = "ai", ["本当に考えてるの？"] = "ai", ["人間？"] = "ai",
            ["mail見て"] = "other",
            // トコ
            ["トコちゃんかわいい"] = "toko", ["トコ"] = "toko",
            // めたんへのからかい
            ["めたん慎重すぎ"] = "tease", ["ビビりすぎ"] = "tease", ["弱気だね"] = "tease", ["臆病"] = "tease",
        };
        foreach (var kv in expect)
        {
            Assert.AreEqual(kv.Value, PersonaCommentLines.Classify(kv.Key), kv.Key);
            Assert.AreEqual(kv.Value, PersonaCommentLines.NormalizeCat(C(null, kv.Key)), "cat 無し: " + kv.Key);
            Assert.AreEqual(kv.Value, PersonaCommentLines.NormalizeCat(C("unknown_cat", kv.Key)), "知らない cat: " + kv.Key);
        }
        // 受け箱の cat は知っている種類なら信じる
        foreach (var cat in new[] { "leave", "laugh", "clap", "game", "ai", "toko" })
            Assert.AreEqual(cat, PersonaCommentLines.NormalizeCat(C(cat, "なるほど")), cat);
    }

    private static readonly string[] NewKeys =
    {
        "z|leave", "m|leave", "z+|leave", "m+|leave", "z|laugh", "m|laugh", "z+|laugh", "m+|laugh",
        "z|clap", "m|clap", "z+|clap", "m+|clap", "z|game", "m|game", "z+|game", "m+|game",
        "z|ai", "m|ai", "z+|ai", "m+|ai", "m|toko", "z|toko", "z+|toko", "m+|toko",
        "m|tease|coward", "z+|tease|coward", "z|other|zunda", "m+|other|zunda",
    };

    [Test]
    public void NewKeys_ReturnCleanLines_InRunAndBetweenRuns()
    {
        foreach (var key in NewKeys)
        {
            Assert.IsTrue(PersonaCommentLines.HasKey(key), key);
            var parts = key.Split('|');
            string speaker = parts[0].TrimEnd('+');
            bool follow = parts[0].EndsWith("+", StringComparison.Ordinal);
            string sub = parts.Length > 2 ? parts[2] : null;
            Assert.Greater(PersonaCommentLines.All().Count(x => x.key == key), 1, key);
            foreach (var facts in new[] { InRun, Between })
            {
                var seen = new HashSet<string>();
                for (int seed = 0; seed < 40; seed++)
                {
                    var r = PersonaCommentLines.Pick(speaker, follow, parts[1], sub, facts, null, new System.Random(seed), null, null);
                    Assert.IsTrue(r.HasValue, $"{key} {(facts.InRun ? "run" : "between")}");
                    string line = r.Value.line;
                    Assert.IsTrue(PersonaLines.IsClean(line), $"{key}: {line}");
                    Assert.IsFalse(line.Contains("{") || line.Contains("["), line);
                    Assert.IsTrue(ToneOk(speaker == "z", line), $"{key}: {line}");
                    Assert.LessOrEqual(line.Length, 32, $"吹き出しに収まる長さ: {key}: {line}");
                    if (!facts.InRun) Assert.IsFalse(line.Contains("日目") || line.Contains("万G"), "ランの合間に所持金・日を言わない: " + line);
                    seen.Add(line);
                }
                Assert.Greater(seen.Count, 1, key);
            }
        }
        // ゲームを聞かれたら、タイトルと Steam を言う行がある
        var game = PersonaCommentLines.All().Where(x => x.key.EndsWith("|game", StringComparison.Ordinal)).Select(x => x.t.Text).ToList();
        Assert.IsTrue(game.Any(l => l.Contains("トコのトムトムランド") && l.Contains("Steam")), "タイトルと Steam を一緒に言う行");
        foreach (var who in new[] { "z|game", "m|game" })
        {
            var lines = PersonaCommentLines.All().Where(x => x.key == who).Select(x => x.t.Text).ToList();
            Assert.GreaterOrEqual(lines.Count(l => l.Contains("トコのトムトムランド")), 1, who);
            Assert.GreaterOrEqual(lines.Count(l => l.Contains("Steam")), 2, who);
        }
        // 「トコのトムランド」など違う名前で言わない
        foreach (var (key, t) in PersonaCommentLines.All())
            if (t.Text.Contains("トム")) StringAssert.Contains("トコのトムトムランド", t.Text, key);
    }

    [Test]
    public void To_SpeaksFirst_OverridesDraw()
    {
        foreach (var (cat, text) in new[] { ("cheer", "がんばれ"), ("ask", "今いくら？"), ("greet", "こんにちは"), ("toko", "トコちゃん"), ("game", "何のゲーム？") })
            foreach (var to in new[] { "z", "m" })
                for (int seed = 0; seed < 40; seed++)
                {
                    var c = C(cat, text);
                    c.To = to;
                    var lines = PersonaCommentLines.Reply(c, cat, InRun, new System.Random(seed), new Dictionary<string, int>(), new List<string>());
                    Assert.Greater(lines.Count, 0);
                    Assert.AreEqual(to, lines[0].Speaker, $"{cat} to={to} seed={seed}");
                    if (lines.Count == 2) Assert.AreNotEqual(to, lines[1].Speaker);
                }
        // 名指しが無ければ今までどおり両方が先に話すことがある
        var leads = new HashSet<string>();
        for (int seed = 0; seed < 60; seed++)
            leads.Add(PersonaCommentLines.Reply(C("cheer", "がんばれ"), "cheer", InRun, new System.Random(seed), null, null)[0].Speaker);
        CollectionAssert.AreEquivalent(new[] { "z", "m" }, leads);
        // 受け箱の to を読む（知らない値は無し）
        Assert.AreEqual("m", PersonaComment.Parse("{\"id\":\"a\",\"unixMs\":1,\"text\":\"めたん頑張れ\",\"to\":\"m\"}").To);
        Assert.AreEqual("z", PersonaComment.Parse("{\"id\":\"a\",\"unixMs\":1,\"text\":\"x\",\"to\":\"z\"}").To);
        Assert.IsNull(PersonaComment.Parse("{\"id\":\"a\",\"unixMs\":1,\"text\":\"x\",\"to\":\"q\"}").To);
        Assert.IsNull(PersonaComment.Parse("{\"id\":\"a\",\"unixMs\":1,\"text\":\"x\"}").To);
    }

    [Test]
    public void To_SurvivesInbox_AndPendingReload()
    {
        long now = 1_793_289_100_000;
        string path = Path.Combine(_dir, "inbox.jsonl");
        for (int i = 0; i < 6; i++)
        {
            string to = i % 2 == 0 ? "m" : "z";
            File.AppendAllText(path, $"{{\"id\":\"to-{i}\",\"unixMs\":{now},\"author\":\"テスト\",\"text\":\"がんばれ\",\"cat\":\"cheer\",\"superchat\":null,\"first\":false,\"to\":\"{to}\"}}\n");
        }
        var e = Engine(() => now);
        e.LastDecisionWriteMs = () => now;   // 判断の一言の直後: 読むだけで返さない（受け箱 → 待ち行列）
        Assert.AreEqual(0, e.Tick("s1").Count);
        Assert.AreEqual(6, e.Current.Pending.Count);
        for (int i = 0; i < 6; i++)
        {
            now += 1_000;
            var fresh = Engine(() => now);   // 状態ファイルから読み直しても名指しが残る
            fresh.GapMs = 0;
            var rows = fresh.Tick("s1");
            Assert.Greater(rows.Count, 0);
            string id = (string)rows[0]["commentId"];
            string expected = int.Parse(id.Substring(3)) % 2 == 0 ? "m" : "z";
            Assert.AreEqual(expected, rows[0]["speaker"], id);
        }
        Assert.AreEqual(6, Rows().Select(r => r["commentId"]).Distinct().Count());
    }

    [Test]
    public void Tease_Coward_RoutesToMetanFirst()
    {
        var cowardLines = PersonaCommentLines.All().Where(x => x.key == "m|tease|coward").Select(x => x.t.Text).ToList();
        var zFollow = PersonaCommentLines.All().Where(x => x.key == "z+|tease|coward").Select(x => x.t.Text).ToList();
        bool followSeen = false;
        foreach (var text in new[] { "めたん慎重すぎ", "ビビりすぎでしょ", "弱気だなあ", "臆病すぎ" })
        {
            Assert.AreEqual("coward", PersonaCommentLines.SubOf("tease", C("tease", text)), text);
            for (int seed = 0; seed < 40; seed++)
            {
                var lines = PersonaCommentLines.Reply(C("tease", text), "tease", InRun, new System.Random(seed), new Dictionary<string, int>(), new List<string>());
                Assert.AreEqual("m", lines[0].Speaker, text);
                CollectionAssert.Contains(cowardLines, lines[0].Line, text);
                if (lines.Count == 2)
                {
                    CollectionAssert.Contains(zFollow, lines[1].Line, text);
                    followSeen = true;
                }
            }
        }
        Assert.IsTrue(followSeen, "ずんだもんの後の一言も sub で引く");
        // ずんだもんを名指ししていたら、めたんへのからかいではない
        var toZ = C("tease", "ずんだもんビビりすぎ");
        toZ.To = "z";
        Assert.AreNotEqual("coward", PersonaCommentLines.SubOf("tease", toZ));
        // 前からの sub は変わらない
        Assert.AreEqual("broke", PersonaCommentLines.SubOf("tease", C("tease", "今日も金欠？")));
        Assert.AreEqual("missed", PersonaCommentLines.SubOf("tease", C("tease", "また外した")));
        // ずんだ餅の話（名指しの「ずんだもん」は除く）
        Assert.AreEqual("zunda", PersonaCommentLines.SubOf("other", C("other", "ずんだ餅食べたい")));
        Assert.IsNull(PersonaCommentLines.SubOf("other", C("other", "ずんだもん、なるほど")));
    }

    [Test]
    public void Superchat_ReadsAmount_AndAsk_UsesState()
    {
        Assert.AreEqual("500円", PersonaCommentLines.AmountJa("¥500"));
        Assert.AreEqual("1,000円", PersonaCommentLines.AmountJa("￥1,000"));
        Assert.AreEqual("$5.00", PersonaCommentLines.AmountJa("$5.00"));
        bool amount = false, money = false;
        for (int seed = 0; seed < 60; seed++)
        {
            var sc = PersonaCommentLines.Reply(C("superchat", "", amount: "¥500"), "superchat", InRun, new System.Random(seed), null, null);
            amount |= sc.Any(l => l.Line.Contains("500円"));
            var ask = PersonaCommentLines.Reply(C("ask", "今いくら？"), "ask", InRun, new System.Random(seed), null, null);
            money |= ask.Any(l => l.Line.Contains("2.7万G"));
        }
        Assert.IsTrue(amount, "スパチャの額を読む");
        Assert.IsTrue(money, "所持金で答える");
    }

    [Test]
    public void SameLine_AtMostThreeTimesPerStream()
    {
        var counts = new Dictionary<string, int>();
        var recent = new List<string>();
        var rng = new System.Random(1);
        for (int i = 0; i < 200; i++)
            PersonaCommentLines.Reply(C("cheer", "がんばれ"), "cheer", InRun, rng, counts, recent);
        Assert.IsTrue(counts.Values.All(n => n <= PersonaCommentLines.MaxPerStream));
        Assert.Greater(counts.Count, 8);
    }

    [Test]
    public void EveryTemplateLine_IsClean_AndKnowsItsTags()
    {
        var known = new HashSet<string> { "run", "between", "poor", "rich", "debtSoon", "win", "lose" };
        var holes = new HashSet<string> { "money", "day", "debtDays", "debt", "funds", "runs", "stream", "amount" };
        int n = 0;
        foreach (var (key, t) in PersonaCommentLines.All())
        {
            n++;
            Assert.IsTrue(PersonaLines.IsClean(t.Text), $"{key}: {t.Text}");
            Assert.IsTrue(t.Conds.All(known.Contains), $"{key}: {t.Raw}");
            Assert.IsTrue(t.Holes.All(holes.Contains), $"{key}: {t.Raw}");
            CollectionAssert.Contains(new[] { "ama", "hero", "hiso", "namida", "tsun", "normal", "surprise" }, t.E, t.Raw);
            Assert.IsTrue(ToneOk(key.StartsWith("z", StringComparison.Ordinal), t.Text), $"話し手の口調: {key}: {t.Text}");
        }
        Assert.Greater(n, 200);
    }

    /// <summary>口調の大まかな検査: ずんだもんはお嬢様口調で終わらない、めたんは「のだ」を言わない。</summary>
    private static bool ToneOk(bool zundamon, string line) =>
        zundamon ? !System.Text.RegularExpressions.Regex.IsMatch(line, "(わ|のよ|かしら)[。！]?$") : !line.Contains("のだ");

    // =====================================================
    // 受け箱と書き出し
    // =====================================================

    private PersonaCommentEngine Engine(Func<long> now) => new()
    {
        InboxPath = Path.Combine(_dir, "inbox.jsonl"),
        StatePath = Path.Combine(_dir, "state.json"),
        OutDir = Path.Combine(_dir, "out"),
        NowMs = now,
        Facts = () => InRun,
        Context = () => ("run_x", 17, "day"),
        Rng = new System.Random(3),
    };

    private void Inbox(string id, long unixMs, string text, string cat, bool append = true, string superchat = null)
    {
        string sc = superchat != null ? $"{{\"amount\":\"{superchat}\"}}" : "null";
        string line = $"{{\"id\":\"{id}\",\"unixMs\":{unixMs},\"author\":\"たろう\",\"text\":\"{text}\",\"cat\":\"{cat}\",\"superchat\":{sc},\"first\":true}}\n";
        string path = Path.Combine(_dir, "inbox.jsonl");
        if (append) File.AppendAllText(path, line);
        else File.WriteAllText(path, line);
    }

    private List<Dictionary<string, object>> Rows() =>
        Directory.Exists(Path.Combine(_dir, "out"))
            ? Directory.GetFiles(Path.Combine(_dir, "out"), "comment_log_*.jsonl")
                .SelectMany(File.ReadAllLines).Select(JsonConvert.DeserializeObject<Dictionary<string, object>>).ToList()
            : new List<Dictionary<string, object>>();

    [Test]
    public void Reply_WritesRowsInSpecShape()
    {
        long now = 1_793_289_100_000;
        var e = Engine(() => now);
        Inbox("fake-1", now - 1000, "ずんだもん今日も金欠？", "tease");
        var rows = e.Tick("s1");
        Assert.That(rows.Count, Is.InRange(1, 2));
        var all = Rows();
        Assert.AreEqual(rows.Count, all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            var r = all[i];
            Assert.AreEqual("comment", r["kind"]);
            Assert.AreEqual((long)i, r["seq"]);
            Assert.AreEqual(1L, r["exchange"]);
            Assert.AreEqual("fake-1", r["commentId"]);
            Assert.AreEqual("たろう", r["commentAuthor"]);
            Assert.AreEqual("ずんだもん今日も金欠？", r["commentText"]);
            Assert.AreEqual("tease", r["commentCat"]);
            Assert.AreEqual(now, r["unixMs"]);
            Assert.AreEqual("run_x", r["run"]);
            Assert.AreEqual(17L, r["day"]);
            Assert.IsFalse(r.ContainsKey("decision"), "集計に混ざらないように decision は付けない");
            foreach (var k in new[] { "time", "phase", "speaker", "line", "e" }) Assert.IsTrue(r.ContainsKey(k), k);
        }
    }

    [Test]
    public void StaleComment_IsDropped()
    {
        long now = 1_793_289_100_000;
        var e = Engine(() => now);
        Inbox("fake-old", now - PersonaCommentEngine.StaleMs - 1, "こんにちは", "greet");
        Assert.AreEqual(0, e.Tick("s1").Count);
        Assert.AreEqual(0, Rows().Count);
        // 届いたときは新しくても、判断の一言が続いて返せないまま 60 秒を過ぎたら捨てる
        Inbox("fake-wait", now, "こんにちは", "greet");
        e.LastDecisionWriteMs = () => now;
        Assert.AreEqual(0, e.Tick("s1").Count);
        Assert.AreEqual(1, e.Current.Pending.Count);
        now += PersonaCommentEngine.StaleMs + 1;
        Assert.AreEqual(0, e.Tick("s1").Count);
        Assert.AreEqual(0, e.Current.Pending.Count);
    }

    [Test]
    public void SameComment_NotAnsweredTwice_AcrossReload_AndInboxReset()
    {
        long now = 1_793_289_100_000;
        Inbox("fake-1", now, "がんばれ", "cheer");
        Assert.Greater(Engine(() => now).Tick("s1").Count, 0);
        int written = Rows().Count;

        now += 60_000;
        // 読み込み直し（ドメインの再読み込み・次のラン）: 同じ受け箱をもう一度見ても返さない
        Assert.AreEqual(0, Engine(() => now).Tick("s1").Count);
        // 受け箱が空にされて（live_chat.py の起動）同じ id がまた書かれても返さない。新しい id には返す
        Inbox("fake-1", now, "がんば", "cheer", append: false);   // 前より短い = 空にされてから書かれた
        Assert.AreEqual(0, Engine(() => now).Tick("s1").Count);
        Inbox("fake-2", now, "がんばれ", "cheer");
        Assert.Greater(Engine(() => now).Tick("s1").Count, 0);
        Assert.AreEqual(2, Rows().Select(r => r["commentId"]).Distinct().Count());
        Assert.Greater(Rows().Count, written);
    }

    [Test]
    public void Gap_AndQuiet_AreRespected()
    {
        long now = 1_793_289_100_000;
        long lastWrite = 0;
        var e = Engine(() => now);
        e.LastDecisionWriteMs = () => lastWrite;
        Inbox("fake-1", now, "こんにちは", "greet");
        Assert.Greater(e.Tick("s1").Count, 0);
        now += 5_000;
        Inbox("fake-2", now, "がんばれ", "cheer");
        Assert.AreEqual(0, e.Tick("s1").Count, "前の返事から 40 秒たっていない");
        now += PersonaCommentEngine.MinGapMs;
        lastWrite = now - 2_000;
        Assert.AreEqual(0, e.Tick("s1").Count, "判断の一言の直後（やり取りの途中）には挟まない");
        now += 10_000;
        var rows = e.Tick("s1");
        Assert.Greater(rows.Count, 0, "静かになったら返す");
        Assert.AreEqual("fake-2", rows[0]["commentId"]);
        Inbox("fake-3", now, "がんばれ", "cheer");
        Assert.AreEqual(0, e.Tick("s1").Count, "返したばかり");
    }

    [Test]
    public void Inactive_WithoutLiveFlag_OrInbox()
    {
        string flag = Path.Combine(_dir, "live.flag");
        string inbox = Path.Combine(_dir, "inbox.jsonl");
        File.WriteAllText(inbox, "");
        Assert.IsFalse(PersonaCommentResponder.Active(flag, inbox), "live.flag が無ければ動かない");
        File.WriteAllText(flag, "");
        Assert.IsTrue(PersonaCommentResponder.Active(flag, inbox));
        Assert.IsFalse(PersonaCommentResponder.Active(flag, ""), "commentInboxPath が空なら動かない");
        Assert.IsFalse(PersonaCommentResponder.Active(flag, Path.Combine(_dir, "none.jsonl")), "受け箱が無ければ動かない");
    }

    // =====================================================
    // 判断ごとの一言（増やした行）
    // =====================================================

    private static readonly string[] DayKeys =
    {
        "budget", "buy_style", "newspaper_yes", "newspaper_no", "intel_yes", "intel_no", "shop_upgrade_yes", "shop_upgrade_no",
        "smith_upgrade_yes", "smith_upgrade_no", "broker_upgrade_yes", "broker_upgrade_no", "machine_upgrade_yes", "machine_upgrade_no",
        "machine_repair_yes", "machine_repair_no", "machine_yes", "machine_no", "finance_fund", "finance_bond", "finance_sell", "finance_no",
        "finance_share", "hero_tactics", "fallback",
    };

    [Test]
    public void PersonaLines_NewKeys_ProduceCleanLines()
    {
        foreach (var persona in new[] { "zundamon", "metan" })
        {
            foreach (var q in DayKeys)
            {
                var seen = new HashSet<string>();
                foreach (PersonaLines.Band band in Enum.GetValues(typeof(PersonaLines.Band)))
                    for (int seed = 0; seed < 60; seed++)
                    {
                        string choice = "テスト品";
                        string line = PersonaLines.Pick(persona, q, band, choice, "3万G", new System.Random(seed), null, seed % 3 == 0 ? "sad" : null);
                        Assert.IsFalse(string.IsNullOrEmpty(line), $"{persona} {q}");
                        Assert.IsTrue(PersonaLines.IsClean(line), $"{persona} {q}: {line}");
                        Assert.IsFalse(line.Contains("{"), line);
                        Assert.IsTrue(ToneOk(persona == "zundamon", line), $"{persona} {q}: {line}");
                        seen.Add(line);
                    }
                if (q != "fallback") Assert.GreaterOrEqual(seen.Count, 6, $"{persona} {q} の行が少ない");
            }
            foreach (var q in new[] { "village_result", "stream_result", "run_result" })
                foreach (var win in new[] { true, false })
                    for (int seed = 0; seed < 30; seed++)
                    {
                        string line = PersonaLines.PickResult(persona, q, win, new System.Random(seed));
                        Assert.IsFalse(string.IsNullOrEmpty(line));
                        Assert.IsTrue(PersonaLines.IsClean(line), line);
                    }
        }
    }

    [Test]
    public void ConsultLines_AreClean()
    {
        foreach (var kv in ConsultLines.Templates)
            foreach (var l in kv.Value)
            {
                Assert.IsTrue(PersonaLines.IsClean(l), $"{kv.Key}: {l}");
                Assert.IsTrue(ToneOk(kv.Key.StartsWith("z|", StringComparison.Ordinal), l), $"{kv.Key}: {l}");
            }
        foreach (var key in new[] { "m|told", "z|cry", "z|recover", "m|mount_reply", "m|thanks_reply", "m|blame_reply", "z|mount" })
            Assert.GreaterOrEqual(ConsultLines.Templates[key].Length, 8, key);
    }
}
