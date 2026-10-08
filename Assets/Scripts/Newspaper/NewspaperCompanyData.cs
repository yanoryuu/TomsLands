using System;
using System.Collections.Generic;

/// <summary>
/// 新聞社1社分のマスター。Newspapers.csv（タブ区切り）1行 = 1社。
///
/// 料金と速報性は<b>逆相関</b>させてある。高い社は裏を取ってから出すので遅く、
/// 安い社は早いが誤報が多い。こうしないと「金ができたら最上位紙に固定」で
/// 選択が消えてしまう（Docs/News_Spec.md §4.1）。
/// </summary>
public class NewspaperCompanyData
{
    public string companyId;
    public string companyName;
    public int price;
    public int contractTurns;
    public int trustRating;      // 公称信頼度 1〜5（UIの星）
    public int speedRating;      // 公称速報性 1〜5（UIの星）
    public int leadTurns;        // 掲載から効果発現までの実効ターン数
    public float falseRate;      // 誤報率 0〜1
    public List<string> pages = new();       // 保有する面。網羅バーの本数と一致する
    public List<string> categories = new();  // 扱う記事カテゴリ
    public int articlesPerIssue;   // 旧列。フェーズ3では issueMin/issueMax を使う

    // --- フェーズ3（Docs/News_Phase3_Spec.md §2.1） ---
    public int issueMin = 1;
    public int issueMax = 1;
    public Dictionary<string, int> pageSlots = new();   // front:1,second:1 ...
    public List<string> bylines = new();                // 記者名の候補。空 = 無署名のみ
    public float bylineRate = 1f;                       // 署名が付く確率
    public float clarityConfirmed = 1f, clarityPresumed, clarityAnonymous;
    public float reportRate = 1f;                       // 自社のカテゴリの事象を報じる確率
    public string styleNote;                            // AI 下書き用。製品には出さない

    /// <summary>その面の上限。指定が無い面は issueMax まで。</summary>
    public int SlotOf(string page) => pageSlots.TryGetValue(page, out var n) ? n : Math.Max(1, issueMax);

    public bool HasPage(string page) => pages.Contains(page);
    public bool HandlesCategory(string category) => categories.Contains(category);

    public override string ToString() =>
        $"{companyName}({companyId}) {price}G/{contractTurns}T lead={leadTurns} false={falseRate:P0}";
}
