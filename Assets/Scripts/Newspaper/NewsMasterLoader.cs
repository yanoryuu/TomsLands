using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 新聞社と記事のマスターを読み込む（EventDataLoader と同じキャッシュ付き静的ローダー）。
///
/// ファイルは<b>タブ区切り</b>だが拡張子は .csv にしてある。Unity が TextAsset として
/// 取り込む拡張子に .tsv が含まれておらず、.tsv だと Addressables から読めないため。
/// 記事本文にカンマが多用されるので、区切りはタブでなければならない。
/// </summary>
public static class NewsMasterLoader
{
    private static List<NewspaperCompanyData> _companies;
    private static List<NewsArticleData> _articles;

    public static List<NewspaperCompanyData> LoadCompanies()
    {
        if (_companies != null) return _companies;

        _companies = new List<NewspaperCompanyData>();
        var text = AddressableLoader.Load<TextAsset>("Newspapers");
        if (text == null)
        {
            Debug.LogError("[NewsMasterLoader] Newspapers が見つかりません。");
            return _companies;
        }

        foreach (var cols in ParseRows(text.text, 11))
        {
            _companies.Add(new NewspaperCompanyData
            {
                companyId = cols[0],
                companyName = cols[1],
                price = ToInt(cols[2]),
                contractTurns = ToInt(cols[3]),
                trustRating = ToInt(cols[4]),
                speedRating = ToInt(cols[5]),
                leadTurns = ToInt(cols[6]),
                falseRate = ToFloat(cols[7]),
                pages = SplitList(cols[8]),
                categories = SplitList(cols[9]),
                articlesPerIssue = ToInt(cols[10]),
            });
        }

        Debug.Log($"[NewsMasterLoader] 新聞社 {_companies.Count} 社を読み込みました。");
        return _companies;
    }

    public static List<NewsArticleData> LoadArticles()
    {
        if (_articles != null) return _articles;

        _articles = new List<NewsArticleData>();
        var text = AddressableLoader.Load<TextAsset>("NewsArticles");
        if (text == null)
        {
            Debug.LogError("[NewsMasterLoader] NewsArticles が見つかりません。");
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

        Debug.Log($"[NewsMasterLoader] 記事 {_articles.Count} 本を読み込みました。");
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

    /// <summary>テスト用にキャッシュを捨てる。</summary>
    public static void ClearCache()
    {
        _companies = null;
        _articles = null;
    }

    // ---------------------------------------------------------------

    /// <summary>1行目をヘッダとして読み飛ばし、列数を揃えて返す。</summary>
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
                // 末尾の空欄が省略されている行を許容する
                var padded = new string[columnCount];
                for (int j = 0; j < columnCount; j++)
                    padded[j] = j < cols.Length ? cols[j].Trim() : string.Empty;
                cols = padded;
            }
            else
            {
                for (int j = 0; j < cols.Length; j++) cols[j] = cols[j].Trim();
            }

            if (string.IsNullOrEmpty(cols[0])) continue; // id 無しの行は飛ばす
            yield return cols;
        }
    }

    private static List<string> SplitList(string s)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Split(','))
        {
            var t = part.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }

    private static int ToInt(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static float ToFloat(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
