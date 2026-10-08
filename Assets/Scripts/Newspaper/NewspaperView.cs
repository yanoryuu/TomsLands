using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 朝刊画面（情報ターミナル・C案 3ペイン）のビュー。Docs/News_Spec.md §10.2。
///
///   上部バー … 第N号 / 次の戦闘まで N日 / 所持金
///   左       … 新聞社カード×5、購読枠 N/M、契約更新まで あとNターン
///   中央     … 題字・一面トップ・小記事グリッド・お詫びと訂正・面タブとページ送り
///   右上     … スクラップ（カード×3・まとめて確認）
///   右下     … 相場（種別ごとのスパークライン・変動率・Heat）
///
/// どの参照も未設定で動く（null-safe）。フェーズ1のプレハブ（題字・号数・所持金・記事一覧・閉じる）
/// のままでも中央の紙面だけは表示される。
/// </summary>
public class NewspaperView : MonoBehaviour
{
    // ---------------- データ ----------------

    public struct ArticleData
    {
        public string entryKey;
        public string headline;
        public string lead;
        public string body;
        public string byline;
        public bool isRead;
        public bool bodyOpen;
        public bool canScrapKind;   // 通常記事か（スクラップボタンを出すか）
        public bool canScrap;       // 押せるか（枠が満杯・貼り済みなら不可）
    }

    public class PaperData
    {
        public string masthead;
        public Sprite emblem;
        public bool hasLead;
        public ArticleData lead;
        public List<ArticleData> grid = new();
        public List<string> corrections = new();
        public bool isShopPage;
        public List<string> shopLines = new();
    }

    public struct ScrapData
    {
        public string entryKey;
        public string headline;
        public string lead;
        public ScrapState state;
        public bool canAfford;
    }

    [Serializable]
    public class CompanyEmblem
    {
        public string companyId;
        public Sprite emblem;
    }

    // ---------------- 上部バー ----------------

    [Header("上部バー")]
    [SerializeField] private TextMeshProUGUI issueText;      // 第N号
    [SerializeField] private GameObject nextBattleRoot;
    [SerializeField] private TextMeshProUGUI nextBattleText; // 次の戦闘まで N日
    [SerializeField] private TextMeshProUGUI moneyText;      // 所持金
    [SerializeField] private Button closeButton;

    // ---------------- 左: 購読 ----------------

    [Header("左: 購読")]
    [SerializeField] private Transform companyCardParent;
    [SerializeField] private NewspaperCompanyCardUI companyCardPrefab;
    [SerializeField] private List<CompanyEmblem> emblems = new();
    [SerializeField] private TextMeshProUGUI slotText;       // 購読枠 2/3
    [SerializeField] private TextMeshProUGUI renewalText;    // 契約更新まで あと3ターン

    // ---------------- 中央: 紙面 ----------------

    [Header("中央: 紙面")]
    [SerializeField] private TextMeshProUGUI mastheadText;   // 題字（社名）
    [SerializeField] private Image[] mastheadEmblems;        // 題字の左右の紋章
    [SerializeField] private GameObject paperArticlesRoot;   // 記事面（自店面のとき隠す）
    [SerializeField] private NewsLeadArticleUI leadArticle;  // 一面トップ
    [SerializeField] private Transform articleParent;        // 小記事グリッド
    [SerializeField] private NewsArticleCellUI articleCellPrefab;
    [SerializeField] private GameObject correctionRoot;      // 17お詫びと訂正枠
    [SerializeField] private TextMeshProUGUI correctionText;
    [SerializeField] private GameObject shopPageRoot;        // 自店面
    [SerializeField] private TextMeshProUGUI shopReportText;
    [SerializeField] private NewsPageTabUI[] pageTabs;
    [SerializeField] private Button prevPageButton;
    [SerializeField] private Button nextPageButton;

    // ---------------- 右上: スクラップ ----------------

    [Header("右上: スクラップ")]
    [SerializeField] private TextMeshProUGUI scrapHeaderText;  // スクラップ 3/3
    [SerializeField] private Transform scrapCardParent;
    [SerializeField] private NewsScrapCardUI scrapCardPrefab;
    [SerializeField] private Button batchConfirmButton;        // 24まとめて確認
    [SerializeField] private TextMeshProUGUI batchConfirmLabel;

    // ---------------- 右下: 相場 ----------------

    [Header("右下: 相場")]
    [SerializeField] private NewsMarketRowUI[] marketRows;

    // ---------------- イベント ----------------

    private readonly Subject<Unit> onCloseClicked = new();
    private readonly Subject<string> onArticleClicked = new();
    private readonly Subject<string> onScrapClicked = new();
    private readonly Subject<string> onCompanySelected = new();
    private readonly Subject<string> onSubscribeClicked = new();
    private readonly Subject<string> onPageSelected = new();
    private readonly Subject<int> onPageStep = new();
    private readonly Subject<string> onConfirmClicked = new();
    private readonly Subject<Unit> onBatchConfirmClicked = new();

    public Observable<Unit> OnCloseClicked => onCloseClicked;
    /// <summary>記事（一面トップ or 小記事）のクリック。掲載キーが流れる。</summary>
    public Observable<string> OnArticleClicked => onArticleClicked;
    public Observable<string> OnScrapClicked => onScrapClicked;
    public Observable<string> OnCompanySelected => onCompanySelected;
    public Observable<string> OnSubscribeClicked => onSubscribeClicked;
    public Observable<string> OnPageSelected => onPageSelected;
    /// <summary>ページ送り（-1 / +1）。</summary>
    public Observable<int> OnPageStep => onPageStep;
    public Observable<string> OnConfirmClicked => onConfirmClicked;
    public Observable<Unit> OnBatchConfirmClicked => onBatchConfirmClicked;

    public IReadOnlyList<NewsMarketRowUI> MarketRows => marketRows ?? Array.Empty<NewsMarketRowUI>();

    private readonly Dictionary<string, NewspaperCompanyCardUI> cards = new();
    private readonly List<NewsScrapCardUI> scrapCards = new();

    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => onCloseClicked.OnNext(Unit.Default));
        if (prevPageButton != null)
            prevPageButton.onClick.AddListener(() => onPageStep.OnNext(-1));
        if (nextPageButton != null)
            nextPageButton.onClick.AddListener(() => onPageStep.OnNext(1));
        if (batchConfirmButton != null)
            batchConfirmButton.onClick.AddListener(() => onBatchConfirmClicked.OnNext(Unit.Default));
        if (leadArticle != null)
        {
            leadArticle.OnClicked += key => onArticleClicked.OnNext(key);
            leadArticle.OnScrapClicked += key => onScrapClicked.OnNext(key);
        }
        if (pageTabs != null)
            foreach (var tab in pageTabs)
                if (tab != null) tab.OnClicked += page => onPageSelected.OnNext(page);
    }

    // ---------------- 上部バー ----------------

    public void ShowHeader(string issueLabel, int money, int turnsUntilBattle)
    {
        if (issueText != null) issueText.text = issueLabel;
        if (moneyText != null) moneyText.text = $"{money:N0}G";

        bool hasBattle = turnsUntilBattle >= 0;
        if (nextBattleRoot != null) nextBattleRoot.SetActive(hasBattle);
        if (nextBattleText != null)
        {
            nextBattleText.gameObject.SetActive(hasBattle);
            nextBattleText.text = turnsUntilBattle == 0 ? "次の戦闘まで 本日" : $"次の戦闘まで {turnsUntilBattle}日";
        }
    }

    // ---------------- 左: 購読 ----------------

    public Sprite EmblemOf(string companyId)
    {
        if (emblems == null) return null;
        foreach (var e in emblems)
            if (e != null && e.companyId == companyId) return e.emblem;
        return null;
    }

    public void ShowCompanies(IReadOnlyList<NewspaperCompanyCardUI.Data> list)
    {
        if (companyCardParent == null || companyCardPrefab == null) return;

        foreach (var d in list)
        {
            if (!cards.TryGetValue(d.companyId, out var card) || card == null)
            {
                card = Instantiate(companyCardPrefab, companyCardParent);
                card.OnSelectClicked += id => onCompanySelected.OnNext(id);
                card.OnSubscribeClicked += id => onSubscribeClicked.OnNext(id);
                cards[d.companyId] = card;
            }
            card.SetData(d);
        }
    }

    public void PopCompany(string companyId)
    {
        if (cards.TryGetValue(companyId, out var card) && card != null) UIFx.Pop(card.transform, 0.94f, 0.2f);
    }

    public void ShowSlots(int used, int slots, int turnsUntilRenewal)
    {
        if (slotText != null) slotText.text = $"購読枠 {used}/{slots}";
        if (renewalText != null)
        {
            renewalText.gameObject.SetActive(turnsUntilRenewal > 0);
            renewalText.text = $"契約更新まで あと{turnsUntilRenewal}ターン";
        }
    }

    // ---------------- 中央: 紙面 ----------------

    public void ShowPaper(PaperData p)
    {
        if (p == null) return;

        if (mastheadText != null) mastheadText.text = p.masthead;
        if (mastheadEmblems != null)
            foreach (var img in mastheadEmblems)
            {
                if (img == null) continue;
                img.sprite = p.emblem;
                img.enabled = p.emblem != null;
            }

        if (shopPageRoot != null) shopPageRoot.SetActive(p.isShopPage);
        if (paperArticlesRoot != null) paperArticlesRoot.SetActive(!p.isShopPage);
        if (shopReportText != null)
            shopReportText.text = p.isShopPage ? string.Join("\n", p.shopLines) : string.Empty;

        if (leadArticle != null)
        {
            bool showLead = !p.isShopPage && p.hasLead;
            leadArticle.gameObject.SetActive(showLead);
            if (showLead) leadArticle.SetData(p.lead);
        }

        if (articleParent != null && articleCellPrefab != null)
        {
            for (int i = articleParent.childCount - 1; i >= 0; i--)
                Destroy(articleParent.GetChild(i).gameObject);

            if (!p.isShopPage)
            {
                // 一面トップを持たないプレハブ（フェーズ1版）では、トップも小記事として並べる
                if (leadArticle == null && p.hasLead) AddCell(p.lead);
                foreach (var a in p.grid) AddCell(a);
            }
            FitGrid(p.isShopPage ? 0 : p.grid.Count);
        }

        bool hasCorrection = !p.isShopPage && p.corrections.Count > 0;
        if (correctionRoot != null) correctionRoot.SetActive(hasCorrection);
        if (correctionText != null) correctionText.text = hasCorrection ? string.Join("\n", p.corrections) : string.Empty;
    }

    /// <summary>
    /// 小記事が1本だけの日は横幅いっぱいに出す（右半分が空いて紙面がまばらに見えないように）。
    /// 2本以上は2段組。
    /// </summary>
    private void FitGrid(int count)
    {
        var grid = articleParent != null ? articleParent.GetComponent<GridLayoutGroup>() : null;
        if (grid == null) return;
        if (gridCellSize == Vector2.zero) gridCellSize = grid.cellSize;
        float full = ((RectTransform)articleParent).rect.width;
        bool single = count == 1 && full > gridCellSize.x * 1.5f;
        grid.constraintCount = single ? 1 : 2;
        grid.cellSize = single ? new Vector2(full, gridCellSize.y) : gridCellSize;
    }

    private Vector2 gridCellSize;

    private void AddCell(ArticleData a)
    {
        var cell = Instantiate(articleCellPrefab, articleParent);
        cell.SetData(a);
        cell.OnClicked += key => onArticleClicked.OnNext(key);
        if (a.bodyOpen) cell.ShowBody(a.body);
    }

    public void ShowTabs(ICollection<string> visiblePages, string selected, bool canStepPrev, bool canStepNext)
    {
        if (pageTabs != null)
            foreach (var tab in pageTabs)
                if (tab != null) tab.SetState(visiblePages.Contains(tab.PageKey), tab.PageKey == selected);
        if (prevPageButton != null) prevPageButton.interactable = canStepPrev;
        if (nextPageButton != null) nextPageButton.interactable = canStepNext;
    }

    // ---------------- 右上: スクラップ ----------------

    public void ShowScraps(IReadOnlyList<ScrapData> list, int occupied, int capacity,
        bool canBatch, int confirmCost, int batchCost)
    {
        if (scrapHeaderText != null) scrapHeaderText.text = $"スクラップ {occupied}/{capacity}";

        if (scrapCardParent != null && scrapCardPrefab != null)
        {
            while (scrapCards.Count < list.Count)
            {
                var card = Instantiate(scrapCardPrefab, scrapCardParent);
                card.OnConfirmClicked += key => onConfirmClicked.OnNext(key);
                scrapCards.Add(card);
            }
            for (int i = 0; i < scrapCards.Count; i++)
            {
                var card = scrapCards[i];
                if (card == null) continue;
                bool used = i < list.Count;
                card.gameObject.SetActive(used);
                if (used)
                {
                    var s = list[i];
                    card.SetData(s.entryKey, s.headline, s.lead, s.state, s.canAfford, confirmCost);
                }
            }
        }

        if (batchConfirmButton != null) batchConfirmButton.interactable = canBatch;
        if (batchConfirmLabel != null) batchConfirmLabel.text = $"まとめて確認 {batchCost:N0}G";
    }

    // ---------------- 右下: 相場 ----------------

    public void ShowMarketRow(int index, NewsMarketSummary.Row row)
    {
        if (marketRows == null || index < 0 || index >= marketRows.Length || marketRows[index] == null) return;
        marketRows[index].SetData(row);
    }

    private void OnDestroy()
    {
        onCloseClicked.Dispose();
        onArticleClicked.Dispose();
        onScrapClicked.Dispose();
        onCompanySelected.Dispose();
        onSubscribeClicked.Dispose();
        onPageSelected.Dispose();
        onPageStep.Dispose();
        onConfirmClicked.Dispose();
        onBatchConfirmClicked.Dispose();
    }
}
