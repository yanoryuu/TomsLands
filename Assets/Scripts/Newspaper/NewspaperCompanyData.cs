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
    public int articlesPerIssue;

    public bool HasPage(string page) => pages.Contains(page);
    public bool HandlesCategory(string category) => categories.Contains(category);

    public override string ToString() =>
        $"{companyName}({companyId}) {price}G/{contractTurns}T lead={leadTurns} false={falseRate:P0}";
}
