using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 新聞まわりのマスターを読み込む（EventDataLoader と同じキャッシュ付き静的ローダー）。
///
///   Newspapers.csv     … 新聞社（5行）
///   NewsEvents.csv     … 事象パターン（フェーズ3。効果・真偽はここだけが持つ）
///   NewsTemplates.csv  … 社別テンプレート（報道／結果／訂正）
///   NewsFillers.csv    … 埋め草（効果なし）
///   NewsArticles.csv   … 旧形式の記事（フェーズ1〜2）。NewsEvents が空のときだけ使う
///
/// ファイルは<b>タブ区切り</b>だが拡張子は .csv にしてある。Unity が TextAsset として
/// 取り込む拡張子に .tsv が含まれておらず、.tsv だと Addressables から読めないため。
/// フェーズ3の3枚と新聞社マスターは<b>ヘッダー名で列を引く</b>（列の並び替え・追加に強くするため）。
/// </summary>
public static class NewsMasterLoader
{
    private static List<NewspaperCompanyData> _companies;
    private static List<NewsArticleData> _articles;
    private static List<NewsEventData> _events;
    private static List<NewsTemplateData> _templates;
    private static List<NewsFillerData> _fillers;

    // ---------------------------------------------------------------
    // 新聞社
    // ---------------------------------------------------------------

    public static List<NewspaperCompanyData> LoadCompanies()
    {
        if (_companies != null) return _companies;

        _companies = new List<NewspaperCompanyData>();
        foreach (var r in ReadTable("Newspapers"))
        {
            var c = new NewspaperCompanyData
            {
                companyId = r.Get("companyId"),
                companyName = r.Get("companyName"),
                price = ToInt(r.Get("price")),
                contractTurns = ToInt(r.Get("contractTurns")),
                trustRating = ToInt(r.Get("trustRating")),
                speedRating = ToInt(r.Get("speedRating")),
                leadTurns = ToInt(r.Get("leadTurns")),
                falseRate = ToFloat(r.Get("falseRate")),
                pages = SplitList(r.Get("pages")),
                categories = SplitList(r.Get("categories")),
                articlesPerIssue = ToInt(r.Get("articlesPerIssue")),
                styleNote = r.Get("styleNote"),
            };
            if (string.IsNullOrEmpty(c.companyId)) continue;

            // フェーズ3の列（無ければ旧列から推定）
            int perIssue = Mathf.Clamp(c.articlesPerIssue, 1, 3);
            c.issueMin = r.Has("issueMin") ? Mathf.Max(1, ToInt(r.Get("issueMin"))) : 1;
            c.issueMax = r.Has("issueMax") ? Mathf.Max(c.issueMin, ToInt(r.Get("issueMax"))) : Mathf.Max(c.issueMin, perIssue);
            c.pageSlots = ParseIntMap(r.Get("pageSlots"));
            c.bylines = SplitList(r.Get("bylines"), '|');
            c.bylineRate = r.Has("bylineRate") ? Mathf.Clamp01(ToFloat(r.Get("bylineRate"))) : 1f;
            c.reportRate = r.Has("reportRate") ? Mathf.Clamp01(ToFloat(r.Get("reportRate"))) : 0.8f;
            var cw = ParseFloatMap(r.Get("clarityWeights"));
            if (cw.Count > 0)
            {
                c.clarityConfirmed = cw.TryGetValue("confirmed", out var a) ? a : 0f;
                c.clarityPresumed = cw.TryGetValue("presumed", out var b) ? b : 0f;
                c.clarityAnonymous = cw.TryGetValue("anonymous", out var d) ? d : 0f;
            }
            _companies.Add(c);
        }

        Debug.Log($"[NewsMasterLoader] 新聞社 {_companies.Count} 社を読み込みました。");
        return _companies;
    }

    // ---------------------------------------------------------------
    // フェーズ3: 事象・テンプレート・埋め草
    // ---------------------------------------------------------------

    public static List<NewsEventData> LoadEvents()
    {
        if (_events != null) return _events;

        _events = new List<NewsEventData>();
        foreach (var r in ReadTable("NewsEvents", optional: true))
        {
            var id = r.Get("eventId");
            if (string.IsNullOrEmpty(id)) continue;
            var e = new NewsEventData
            {
                eventId = id,
                family = r.Get("family"),
                difficulty = string.IsNullOrEmpty(r.Get("difficulty")) ? "easy" : r.Get("difficulty").ToLowerInvariant(),
                rarity = string.IsNullOrEmpty(r.Get("rarity")) ? "common" : r.Get("rarity").ToLowerInvariant(),
                arcId = r.Get("arcId"),
                arcStep = ToInt(r.Get("arcStep")),
                arcNext = ParseArcNext(r.Get("arcNext")),
                category = r.Get("category"),
                scale = string.IsNullOrEmpty(r.Get("scale")) ? "small" : r.Get("scale").ToLowerInvariant(),
                sign = ToInt(r.Get("sign")) < 0 ? -1 : 1,
                targetRule = r.Get("targetRule"),
                binds = ParseStringMap(r.Get("binds")),
                hasResult = !IsFalseText(r.Get("hasResult")),
                condition = r.Get("condition"),
                weight = r.Get("weight").Length == 0 ? 1 : Mathf.Max(0, ToInt(r.Get("weight"))),
                cooldown = r.Get("cooldown").Length == 0 ? 10 : Mathf.Max(0, ToInt(r.Get("cooldown"))),
                summaryEn = r.Get("summaryEn"),
            };
            var tw = ParseFloatMap(r.Get("truthWeights"));
            if (tw.Count > 0)
            {
                e.wTrue = tw.TryGetValue("true", out var t) ? t : 0f;
                e.wExaggerated = tw.TryGetValue("exaggerated", out var x) ? x : 0f;
                e.wFalse = tw.TryGetValue("false", out var f) ? f : 0f;
            }
            _events.Add(e);
        }
        Debug.Log($"[NewsMasterLoader] 事象パターン {_events.Count} 件を読み込みました。");
        return _events;
    }

    public static List<NewsTemplateData> LoadTemplates()
    {
        if (_templates != null) return _templates;

        _templates = new List<NewsTemplateData>();
        foreach (var r in ReadTable("NewsTemplates", optional: true))
        {
            var t = new NewsTemplateData
            {
                templateId = r.Get("templateId"),
                eventId = r.Get("eventId"),
                companyId = r.Get("companyId"),
                kind = string.IsNullOrEmpty(r.Get("kind")) ? "report" : r.Get("kind").ToLowerInvariant(),
                tone = string.IsNullOrEmpty(r.Get("tone")) ? "normal" : r.Get("tone").ToLowerInvariant(),
                page = r.Get("page"),
                headline = r.Get("headline"),
                lead = r.Get("lead"),
                body = r.Get("body"),
                clarity = r.Get("clarity"),
                status = r.Get("status"),
                summaryEn = r.Get("summaryEn"),
            };
            if (string.IsNullOrEmpty(t.eventId) || !IsUsable(t.status)) continue;
            if (string.IsNullOrEmpty(t.templateId)) t.templateId = $"{t.eventId}.{t.companyId}.{t.kind}.{_templates.Count}";
            _templates.Add(t);
        }
        Debug.Log($"[NewsMasterLoader] テンプレート {_templates.Count} 本を読み込みました。");
        return _templates;
    }

    public static List<NewsFillerData> LoadFillers()
    {
        if (_fillers != null) return _fillers;

        _fillers = new List<NewsFillerData>();
        foreach (var r in ReadTable("NewsFillers", optional: true))
        {
            var f = new NewsFillerData
            {
                fillerId = r.Get("fillerId"),
                topic = r.Get("topic"),
                companies = SplitList(r.Get("companies")),
                page = r.Get("page"),
                headline = r.Get("headline"),
                lead = r.Get("lead"),
                body = r.Get("body"),
                cooldown = r.Get("cooldown").Length == 0 ? 8 : Mathf.Max(0, ToInt(r.Get("cooldown"))),
                weight = r.Get("weight").Length == 0 ? 1 : Mathf.Max(0, ToInt(r.Get("weight"))),
                status = r.Get("status"),
                summaryEn = r.Get("summaryEn"),
            };
            if (string.IsNullOrEmpty(f.fillerId) || !IsUsable(f.status)) continue;
            _fillers.Add(f);
        }
        Debug.Log($"[NewsMasterLoader] 埋め草 {_fillers.Count} 本を読み込みました。");
        return _fillers;
    }

    /// <summary>
    /// その行を読み込むか（Docs/News_Phase3_Spec.md §7・U6）。
    /// 製品は approved（と状態の空欄）だけ。エディタと開発ビルドは draft / reviewed も読む。
    /// </summary>
    private static bool IsUsable(string status)
    {
        if (string.IsNullOrEmpty(status) || status == "approved") return true;
        return NewsTuning.IncludeDraftTemplates;
    }

    private static bool IsFalseText(string s) =>
        s == "false" || s == "FALSE" || s == "False" || s == "0" || s == "no";

    // ---------------------------------------------------------------
    // 旧形式（フェーズ1〜2）の記事
    // ---------------------------------------------------------------

    public static List<NewsArticleData> LoadArticles()
    {
        if (_articles != null) return _articles;

        _articles = new List<NewsArticleData>();
        var text = AddressableLoader.Load<TextAsset>("NewsArticles");
        if (text == null)
        {
            Debug.LogWarning("[NewsMasterLoader] NewsArticles が見つかりません。");
            return _articles;
        }

        foreach (var c in ParseRows(text.text, 21))
        {
            // 効果量は trendDelta だけ書けば残りは目盛り（NewsTuning）から埋まる。
            // 空欄と「明示的に 0」は区別する（0 と書けば既定値で上書きしない）。
            float trendDelta = ToFloat(c[13]);
            _articles.Add(new NewsArticleData
            {
                id = c[0],
                category = c[1],
                page = c[2],
                sourceClarity = c[3],
                headline = c[4],
                lead = c[5],
                body = c[6],
                byline = c[7],
                crossRefGroup = c[8],
                truth = string.IsNullOrEmpty(c[9]) ? "true" : c[9],
                targetAttribute = c[10],
                targetType = c[11],
                targetItemId = c[12],
                trendDelta = trendDelta,
                demandKick = c[14].Length == 0 ? NewsTuning.DefaultDemandKick(trendDelta) : ToFloat(c[14]),
                hypeRate = c[15].Length == 0 ? NewsTuning.DefaultHypeRate(c[2], trendDelta) : ToFloat(c[15]),
                durationTurns = c[16].Length == 0 ? NewsTuning.DefaultDuration(trendDelta) : ToInt(c[16]),
                followUpId = c[17],
                condition = c[18],
                weight = c[19].Length == 0 ? 1 : ToInt(c[19]),
                summaryEn = c[20],
            });
        }

        Debug.Log($"[NewsMasterLoader] 旧形式の記事 {_articles.Count} 本を読み込みました。");
        return _articles;
    }

    public static NewsArticleData FindArticle(string id)
    {
        foreach (var a in LoadArticles())
            if (a.id == id) return a;
        return null;
    }

    public static NewspaperCompanyData FindCompany(string id)
    {
        foreach (var c in LoadCompanies())
            if (c.companyId == id) return c;
        return null;
    }

    /// <summary>キャッシュを捨てる（テスト・CSV の再読込用）。</summary>
    public static void ClearCache()
    {
        _companies = null;
        _articles = null;
        _events = null;
        _templates = null;
        _fillers = null;
    }

    // ---------------------------------------------------------------
    // 解析
    // ---------------------------------------------------------------

    /// <summary>ヘッダー名で列を引ける1行。</summary>
    public readonly struct Row
    {
        private readonly Dictionary<string, int> header;
        private readonly string[] cols;

        public Row(Dictionary<string, int> header, string[] cols)
        {
            this.header = header;
            this.cols = cols;
        }

        public bool Has(string name) => header.TryGetValue(name, out var i) && i < cols.Length && cols[i].Length > 0;

        public string Get(string name) =>
            header.TryGetValue(name, out var i) && i < cols.Length ? cols[i] : string.Empty;
    }

    private static IEnumerable<Row> ReadTable(string address, bool optional = false)
    {
        var text = AddressableLoader.Load<TextAsset>(address);
        if (text == null)
        {
            if (optional) Debug.LogWarning($"[NewsMasterLoader] {address} が見つかりません。");
            else Debug.LogError($"[NewsMasterLoader] {address} が見つかりません。");
            yield break;
        }

        var lines = text.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 0) yield break;

        var header = new Dictionary<string, int>();
        var head = lines[0].TrimStart('﻿').Split('\t');
        for (int i = 0; i < head.Length; i++)
        {
            var h = head[i].Trim();
            if (h.Length > 0 && !header.ContainsKey(h)) header[h] = i;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cols = lines[i].Split('\t');
            for (int j = 0; j < cols.Length; j++) cols[j] = cols[j].Trim();
            if (cols.Length == 0 || cols[0].Length == 0 || cols[0].StartsWith("#")) continue;
            yield return new Row(header, cols);
        }
    }

    /// <summary>旧形式: 1行目をヘッダーとして読み飛ばし、列数を揃えて返す。</summary>
    private static IEnumerable<string[]> ParseRows(string raw, int columnCount)
    {
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = line.Split('\t');
            if (cols.Length < columnCount)
            {
                var padded = new string[columnCount];
                for (int j = 0; j < columnCount; j++)
                    padded[j] = j < cols.Length ? cols[j].Trim() : string.Empty;
                cols = padded;
            }
            else
            {
                for (int j = 0; j < cols.Length; j++) cols[j] = cols[j].Trim();
            }

            if (string.IsNullOrEmpty(cols[0])) continue;
            yield return cols;
        }
    }

    private static List<string> SplitList(string s, char sep = ',')
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(sep))
        {
            var t = part.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    /// <summary>「a:1,b:2」形式。</summary>
    private static Dictionary<string, string> ParseStringMap(string s)
    {
        var map = new Dictionary<string, string>();
        foreach (var part in SplitList(s))
        {
            int k = part.IndexOfAny(new[] { ':', '=' });
            if (k <= 0) { map[part] = string.Empty; continue; }
            map[part.Substring(0, k).Trim()] = part.Substring(k + 1).Trim();
        }
        return map;
    }

    /// <summary>「条件:事象ID|条件:事象ID」。条件を省いた「事象ID」だけの項目は always 扱い。</summary>
    private static List<(string, string)> ParseArcNext(string s)
    {
        var list = new List<(string, string)>();
        foreach (var part in SplitList(s, '|'))
        {
            int k = part.IndexOf(':');
            if (k < 0) list.Add(("always", part));
            else list.Add((part.Substring(0, k).Trim().ToLowerInvariant() switch
            {
                "herowin" => "heroWin", "herolose" => "heroLose", "hasstock" => "hasStock", "nostock" => "noStock",
                var c => c,
            }, part.Substring(k + 1).Trim()));
        }
        return list;
    }

    private static Dictionary<string, float> ParseFloatMap(string s)
    {
        var map = new Dictionary<string, float>();
        foreach (var kv in ParseStringMap(s)) map[kv.Key] = ToFloat(kv.Value);
        return map;
    }

    private static Dictionary<string, int> ParseIntMap(string s)
    {
        var map = new Dictionary<string, int>();
        foreach (var kv in ParseStringMap(s)) map[kv.Key] = ToInt(kv.Value);
        return map;
    }

    private static int ToInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static float ToFloat(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
