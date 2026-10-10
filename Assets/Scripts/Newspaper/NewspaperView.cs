using System;
using System.Collections.Generic;
using DG.Tweening;
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
///   右上     … スクラップ（カード×3・まとめて確認）。カードを押すと記事ポップアップで全文を読める
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
        public string body;
        public string byline;       // 「第N号／社名／記者：…」
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
    [SerializeField] private GameObject correctionRoot;      // お詫びと訂正
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
    [SerializeField] private Button batchConfirmButton;        // まとめて確認
    [SerializeField] private TextMeshProUGUI batchConfirmLabel;

    // ---------------- 右下: 相場 ----------------

    [Header("右下: 相場")]
    [SerializeField] private NewsMarketRowUI[] marketRows;

    // ---------------- 記事ポップアップ（スクラップの全文） ----------------

    [Header("記事ポップアップ（スクラップを押すと開く）")]
    [SerializeField] private GameObject articlePopupRoot;     // 全画面の暗幕を含むルート（既定は非表示）
    [SerializeField] private RectTransform articlePopupPanel; // 開閉で拡大縮小する本体
    [SerializeField] private Button articlePopupBackdrop;     // 暗幕（押すと閉じる）
    [SerializeField] private Button articlePopupCloseButton;
    [SerializeField] private Image popupBadge;
    [SerializeField] private TextMeshProUGUI popupStateText;
    [SerializeField] private TextMeshProUGUI popupHeadline;
    [SerializeField] private TextMeshProUGUI popupLead;
    [SerializeField] private TextMeshProUGUI popupBody;
    [SerializeField] private TextMeshProUGUI popupByline;
    [SerializeField] private Color pendingStateColor = new Color32(0x8A, 0x5A, 0x2B, 0xFF);
    [SerializeField] private Color unconfirmedStateColor = new Color32(0x8A, 0x5A, 0x2B, 0xFF);
    [SerializeField] private Color hitStateColor = new Color32(0x3E, 0x8E, 0x3A, 0xFF);
    [SerializeField] private Color missStateColor = new Color32(0xB8, 0x3A, 0x2C, 0xFF);

    // ---------------- 演出 ----------------

    [Header("演出（任意）")]
    [SerializeField] private RectTransform topBarPane;
    [SerializeField] private RectTransform leftPane;
    [SerializeField] private RectTransform centerPane;
    [SerializeField] private RectTransform rightPane;
    [Tooltip("社や面を切り替えたときに出し直す紙面の中身（題字より下）")]
    [SerializeField] private RectTransform paperContent;
    [Tooltip("スクラップへ飛ぶカードを置く層。未設定ならこのビューのキャンバス")]
    [SerializeField] private RectTransform flyLayer;

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
    private readonly List<ScrapData> shownScraps = new();
    private readonly Dictionary<string, ScrapState> lastScrapStates = new();
    private readonly List<NewsArticleCellUI> currentCells = new();

    private readonly Dictionary<RectTransform, Vector2> homePositions = new();
    private int lastMoney = int.MinValue;

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

        if (articlePopupBackdrop != null) articlePopupBackdrop.onClick.AddListener(CloseArticlePopup);
        if (articlePopupCloseButton != null) articlePopupCloseButton.onClick.AddListener(CloseArticlePopup);
        if (articlePopupRoot != null) articlePopupRoot.SetActive(false);

        foreach (var rt in new[] { topBarPane, leftPane, centerPane, rightPane, paperContent })
            if (rt != null) homePositions[rt] = rt.anchoredPosition;
    }

    private void OnDisable()
    {
        if (articlePopupRoot != null) articlePopupRoot.SetActive(false);
    }

    // ---------------- 上部バー ----------------

    public void ShowHeader(string issueLabel, int money, int turnsUntilBattle)
    {
        if (issueText != null) issueText.text = issueLabel;
        if (moneyText != null)
        {
            moneyText.text = $"{money:N0}G";
            if (lastMoney != int.MinValue && money != lastMoney) UIFx.Pop(moneyText.transform, 1.15f, 0.25f);
        }
        lastMoney = money;

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
            currentCells.Clear();

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
        currentCells.Add(cell);
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

        shownScraps.Clear();
        shownScraps.AddRange(list);

        if (scrapCardParent != null && scrapCardPrefab != null)
        {
            while (scrapCards.Count < list.Count)
            {
                var card = Instantiate(scrapCardPrefab, scrapCardParent);
                card.OnConfirmClicked += key => onConfirmClicked.OnNext(key);
                card.OnCardClicked += OpenArticlePopup;
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
                    // 確認で枠が詰まるとカードの並びが変わるので、状態の変化は掲載キーで追う
                    bool reveal = lastScrapStates.TryGetValue(s.entryKey, out var prev) && prev != s.state;
                    card.SetData(s.entryKey, s.headline, s.lead, s.state, s.canAfford, confirmCost, reveal);
                }
            }
        }
        lastScrapStates.Clear();
        foreach (var s in list) lastScrapStates[s.entryKey] = s.state;

        if (batchConfirmButton != null) batchConfirmButton.interactable = canBatch;
        if (batchConfirmLabel != null) batchConfirmLabel.text = $"まとめて確認 {batchCost:N0}G";
    }

    // ---------------- 右下: 相場 ----------------

    public void ShowMarketRow(int index, NewsMarketSummary.Row row)
    {
        if (marketRows == null || index < 0 || index >= marketRows.Length || marketRows[index] == null) return;
        marketRows[index].SetData(row);
    }

    // ---------------- 記事ポップアップ ----------------

    private void OpenArticlePopup(string entryKey)
    {
        if (articlePopupRoot == null) return;
        int idx = shownScraps.FindIndex(s => s.entryKey == entryKey);
        if (idx < 0) return;
        var s = shownScraps[idx];
        var card = scrapCards.Find(c => c != null && c.gameObject.activeSelf && c.EntryKey == entryKey);

        if (popupHeadline != null) popupHeadline.text = s.headline;
        if (popupLead != null) popupLead.text = s.lead;
        if (popupBody != null)
        {
            popupBody.text = s.body;
            popupBody.gameObject.SetActive(!string.IsNullOrEmpty(s.body));
        }
        if (popupByline != null) popupByline.text = s.byline;
        if (popupBadge != null && card != null)
        {
            popupBadge.sprite = card.BadgeSprite;
            popupBadge.color = card.BadgeColor;
            popupBadge.enabled = popupBadge.sprite != null;
        }
        if (popupStateText != null)
        {
            (string label, Color color) = s.state switch
            {
                ScrapState.Pending => ("結果待ち", pendingStateColor),
                ScrapState.Unconfirmed => ("未確認", unconfirmedStateColor),
                ScrapState.ConfirmedHit => ("的中", hitStateColor),
                _ => ("誤報", missStateColor),
            };
            popupStateText.text = label;
            popupStateText.color = color;
        }

        articlePopupRoot.SetActive(true);
        articlePopupRoot.transform.SetAsLastSibling();
        var cg = EnsureGroup(articlePopupRoot);
        cg.DOKill();
        cg.alpha = 0f;
        cg.interactable = true;
        cg.blocksRaycasts = true;
        cg.DOFade(1f, 0.16f).SetLink(articlePopupRoot);
        if (articlePopupPanel != null)
        {
            articlePopupPanel.DOKill();
            articlePopupPanel.localScale = Vector3.one * 0.86f;
            articlePopupPanel.DOScale(1f, 0.24f).SetEase(Ease.OutBack).SetLink(articlePopupPanel.gameObject);
        }
    }

    private void CloseArticlePopup()
    {
        if (articlePopupRoot == null || !articlePopupRoot.activeSelf) return;
        var cg = EnsureGroup(articlePopupRoot);
        cg.DOKill();
        cg.interactable = false;
        cg.blocksRaycasts = false;
        cg.DOFade(0f, 0.12f).SetLink(articlePopupRoot)
            .OnComplete(() => { if (articlePopupRoot != null) articlePopupRoot.SetActive(false); });
        if (articlePopupPanel != null)
        {
            articlePopupPanel.DOKill();
            articlePopupPanel.DOScale(0.92f, 0.12f).SetEase(Ease.InCubic).SetLink(articlePopupPanel.gameObject);
        }
    }

    // ---------------- 演出 ----------------

    /// <summary>画面を開いたとき。上部バーは上から、左右のペインは外側から、紙面は下から順に入る。</summary>
    public void PlayIntro()
    {
        Slide(topBarPane, new Vector2(0f, 40f), 0f);
        Slide(leftPane, new Vector2(-60f, 0f), 0.04f);
        Slide(centerPane, new Vector2(0f, -40f), 0.08f);
        Slide(rightPane, new Vector2(60f, 0f), 0.12f);
        StaggerCells(0.25f);
    }

    /// <summary>
    /// 社や面を切り替えたとき。紙面の中身を横から差し込み、小記事を順に並べる。
    /// dir は面送りの向き（-1 / +1、社の切替は 0）。
    /// </summary>
    public void AnimatePaper(int dir)
    {
        if (paperContent != null)
        {
            var from = dir == 0 ? new Vector2(0f, -24f) : new Vector2(dir * 48f, 0f);
            Slide(paperContent, from, 0f, 0.22f);
        }
        if (mastheadText != null) UIFx.Pop(mastheadText.transform, 0.9f, 0.2f);
        StaggerCells(0.08f);
    }

    /// <summary>一面トップの「スクラップする」からスクラップ欄のカードへ、カードが飛んで貼られる。</summary>
    public void FlyToScrap(string entryKey)
    {
        var target = scrapCards.Find(c => c != null && c.gameObject.activeSelf && c.EntryKey == entryKey);
        if (target == null) return;
        if (scrapCardParent is RectTransform parentRt) LayoutRebuilder.ForceRebuildLayoutImmediate(parentRt);

        var layer = flyLayer != null ? flyLayer : GetComponentInParent<Canvas>()?.rootCanvas.transform as RectTransform;
        var source = leadArticle != null ? leadArticle.ScrapSource : null;
        var targetRt = (RectTransform)target.transform;
        var targetGroup = EnsureGroup(target.gameObject);
        if (layer == null || source == null || !source.gameObject.activeInHierarchy)
        {
            UIFx.Pop(target.transform, 0.85f, 0.25f);
            return;
        }

        var ghost = Instantiate(target.gameObject, layer);
        ghost.name = "ScrapFly";
        var ghostRt = (RectTransform)ghost.transform;
        ghostRt.anchorMin = ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
        ghostRt.pivot = targetRt.pivot;
        ghostRt.sizeDelta = targetRt.rect.size;
        ghostRt.position = source.position;
        ghostRt.localScale = Vector3.one * 0.55f;
        ghostRt.localRotation = Quaternion.Euler(0f, 0f, 8f);
        var ghostGroup = EnsureGroup(ghost);
        ghostGroup.alpha = 1f;
        ghostGroup.blocksRaycasts = false;
        // interactable=false にするとボタンの無効色で半透明になるので、色の遷移だけ止める
        foreach (var sel in ghost.GetComponentsInChildren<Selectable>(true)) sel.transition = Selectable.Transition.None;

        targetGroup.DOKill();
        targetGroup.alpha = 0f;

        // 曲線を描くように、いったん上へ持ち上げてから貼る
        Vector3 start = source.position;
        Vector3 end = targetRt.position;
        Vector3 lift = Vector3.Lerp(start, end, 0.5f) + new Vector3(0f, Mathf.Abs(end.x - start.x) * 0.18f, 0f);
        DOTween.Sequence()
            .Append(ghostRt.DOPath(new[] { lift, end }, 0.46f, PathType.CatmullRom).SetEase(Ease.InOutCubic))
            .Join(ghostRt.DOScale(1f, 0.46f).SetEase(Ease.OutCubic))
            .Join(ghostRt.DOLocalRotate(Vector3.zero, 0.46f).SetEase(Ease.OutCubic))
            .SetLink(ghost)
            .OnComplete(() =>
            {
                if (ghost != null) Destroy(ghost);
                if (target == null) return;
                targetGroup.alpha = 1f;
                target.transform.DOKill(true);
                target.transform.DOPunchScale(Vector3.one * 0.08f, 0.28f, 6, 0.6f).SetLink(target.gameObject);
            });
    }

    private void StaggerCells(float baseDelay)
    {
        for (int i = 0; i < currentCells.Count; i++)
        {
            var cell = currentCells[i];
            if (cell == null) continue;
            var cg = EnsureGroup(cell.gameObject);
            var t = cell.transform;
            cg.DOKill();
            t.DOKill();
            cg.alpha = 0f;
            t.localScale = Vector3.one * 0.94f;
            float delay = baseDelay + i * 0.05f;
            cg.DOFade(1f, 0.2f).SetDelay(delay).SetLink(cell.gameObject);
            t.DOScale(1f, 0.24f).SetDelay(delay).SetEase(Ease.OutBack).SetLink(cell.gameObject);
        }
    }

    private void Slide(RectTransform rt, Vector2 offset, float delay, float duration = 0.3f)
    {
        if (rt == null || !rt.gameObject.activeInHierarchy) return;
        if (!homePositions.TryGetValue(rt, out var home)) homePositions[rt] = home = rt.anchoredPosition;
        var cg = EnsureGroup(rt.gameObject);
        rt.DOKill();
        cg.DOKill();
        rt.anchoredPosition = home + offset;
        cg.alpha = 0f;
        rt.DOAnchorPos(home, duration).SetDelay(delay).SetEase(Ease.OutCubic).SetLink(rt.gameObject);
        cg.DOFade(1f, duration * 0.8f).SetDelay(delay).SetLink(rt.gameObject);
    }

    private static CanvasGroup EnsureGroup(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        return cg != null ? cg : go.AddComponent<CanvasGroup>();
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
