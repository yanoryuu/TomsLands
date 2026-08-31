using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// 取引所（金融商品の売買UI）のロジック。情報屋画面の取引所タブから使う。
/// リストは武具と同じ ItemShopSlot 行（SetFinance）を共用し、右ペインは FinanceDetailPanel。
/// View への依存はデリゲート経由（populateRows / detail / showMessage）で受け取る。
/// </summary>
public class ExchangePanelController : IDisposable
{
    private readonly PortfolioModel portfolioModel;
    private readonly FinanceSettings financeSettings;
    private readonly TomsModel tomsModel;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly Func<int, List<ItemShopSlot>> populateRows;
    private readonly Func<FinanceDetailPanel> detailGetter;
    private readonly Action<string> showMessage;

    private CompositeDisposable disposables = new();
    private List<ItemShopSlot> rows = new();
    private string selectedProductId;

    public ExchangePanelController(
        PortfolioModel portfolioModel,
        FinanceSettings financeSettings,
        TomsModel tomsModel,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        Func<int, List<ItemShopSlot>> populateRows,
        Func<FinanceDetailPanel> detailGetter,
        Action<string> showMessage)
    {
        this.portfolioModel = portfolioModel;
        this.financeSettings = financeSettings;
        this.tomsModel = tomsModel;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.populateRows = populateRows;
        this.detailGetter = detailGetter;
        this.showMessage = showMessage;
    }

    /// <summary>商品リストと詳細を再構築する（タブを開いたとき・売買後に呼ぶ）。</summary>
    public void Refresh()
    {
        disposables.Dispose();
        disposables = new CompositeDisposable();

        // 解放済みを先頭に、あとは解禁レベル順
        int brokerLevel = tomsModel.InfoBrokerLevel.Value;
        var products = portfolioModel.AllProducts
            .OrderBy(p => p.unlockInfoBrokerLevel > brokerLevel ? 1 : 0)
            .ThenBy(p => p.unlockInfoBrokerLevel)
            .ToList();

        rows = populateRows(products.Count);

        for (int i = 0; i < rows.Count && i < products.Count; i++)
        {
            var product = products[i];
            var slot = rows[i];
            bool unlocked = product.unlockInfoBrokerLevel <= brokerLevel;

            int unitPrice = GetUnitPrice(product);
            int prevPrice = unitPrice;
            string marketLabel;
            Color marketColor;
            if (!unlocked)
            {
                marketLabel = $"情報屋Lv{product.unlockInfoBrokerLevel}で解禁";
                marketColor = Color.gray;
            }
            else if (product.kind == FinancialProductKind.Bond)
            {
                marketLabel = $"利率{product.bondInterestRate:P0}";
                marketColor = new Color(1f, 0.6f, 0.25f); // 武具の高需要と同じオレンジ
            }
            else
            {
                // ファンドは前日比%（武具の需要%と同じ欄に市況として表示）
                var history = portfolioModel.GetNavHistory(product.productId);
                if (history.Count >= 2) prevPrice = history[history.Count - 2];
                float change = prevPrice > 0 ? (unitPrice - prevPrice) / (float)prevPrice : 0f;
                marketLabel = change.ToString("+0.0%;-0.0%;±0.0%");
                marketColor = change > 0f ? Color.red : (change < 0f ? Color.cyan : Color.gray);
            }

            slot.SetFinance(product.productId, product.productName, product.icon, unitPrice,
                portfolioModel.GetHeldUnits(product.productId), marketLabel, marketColor, prevPrice, unlocked);
            slot.SetSelected(product.productId == selectedProductId);

            // 行クリック/アイコンで選択、ホバー/情報ボタンで説明
            var captured = product;
            slot.OnRowSelected.Subscribe(id => SelectProduct(id)).AddTo(disposables);
            slot.OnIconClicked.Subscribe(id => SelectProduct(id)).AddTo(disposables);
            slot.OnInfoRequested.Subscribe(_ => showMessage(captured.description)).AddTo(disposables);
        }

        var detail = detailGetter();
        if (detail != null)
        {
            detail.OnBuyClicked.Subscribe(qty => HandleBuy(qty)).AddTo(disposables);
            detail.OnSellClicked.Subscribe(qty => HandleSell(qty)).AddTo(disposables);
        }

        // 先頭（解放済み）を自動選択。選択中の商品があればそれを維持
        if (string.IsNullOrEmpty(selectedProductId) || products.All(p => p.productId != selectedProductId))
        {
            var first = products.FirstOrDefault(p => p.unlockInfoBrokerLevel <= brokerLevel);
            selectedProductId = first != null ? first.productId : null;
        }

        if (!string.IsNullOrEmpty(selectedProductId))
        {
            foreach (var row in rows)
                if (row != null) row.SetSelected(row.itemId == selectedProductId);
            ShowDetail();
        }
        else
        {
            detail?.Hide();
        }
    }

    public void HideDetail() => detailGetter()?.Hide();

    private int GetUnitPrice(FinancialProductData product) =>
        product.kind == FinancialProductKind.Bond
            ? product.bondUnitPrice
            : portfolioModel.CalculateFundUnitPrice(product, itemModel, tomsModel.BlacksmithLevel.Value);

    /// <summary>1口あたりの手数料込み購入価格で買える最大口数。</summary>
    private int MaxBuyQuantity(FinancialProductData product, int unitPrice)
    {
        float fee = product.kind == FinancialProductKind.IndexFund && financeSettings != null
            ? financeSettings.fundBuyFeeRate : 0f;
        int per = Mathf.RoundToInt(unitPrice * (1f + fee));
        return per <= 0 ? 0 : tomsModel.PlayerMoney.Value / per;
    }

    private void SelectProduct(string productId)
    {
        var product = portfolioModel.GetProduct(productId);
        if (product == null) return;

        if (product.unlockInfoBrokerLevel > tomsModel.InfoBrokerLevel.Value)
        {
            showMessage($"それは情報屋レベル {product.unlockInfoBrokerLevel} で取り扱いが解禁される。");
            return;
        }

        selectedProductId = productId;
        foreach (var row in rows)
            if (row != null) row.SetSelected(row.itemId == productId);

        ShowDetail();
    }

    private void ShowDetail()
    {
        var detail = detailGetter();
        var product = portfolioModel.GetProduct(selectedProductId);
        if (detail == null || product == null) return;

        int unitPrice = GetUnitPrice(product);
        int held = portfolioModel.GetHeldUnits(product.productId);
        float buyFee = financeSettings != null ? financeSettings.fundBuyFeeRate : 0.02f;
        int maxBuy = MaxBuyQuantity(product, unitPrice);
        // スライダー上限 = 買える数と売れる数（保有）の大きい方
        int maxQuantity = Mathf.Max(1, Mathf.Max(maxBuy, held));

        detail.Show(product, unitPrice, held, portfolioModel.GetNavHistory(product.productId),
            buyFee, maxBuy >= 1, maxQuantity);
    }

    private void HandleBuy(int quantity)
    {
        var product = portfolioModel.GetProduct(selectedProductId);
        if (product == null) return;

        bool success = product.kind == FinancialProductKind.Bond
            ? portfolioModel.BuyBond(product, quantity, tomsModel, gameFlowManager.CurrentTurn.Value)
            : portfolioModel.BuyFund(product, quantity, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value, gameFlowManager.CurrentTurn.Value);

        if (success)
        {
            SoundManager.Instance?.PlaySE("営業/SE_仕入れ完了");
            showMessage(product.kind == FinancialProductKind.Bond
                ? $"{product.productName} を購入した。満期は {product.bondMaturityTurns} 日後だ。"
                : $"{product.productName} を {quantity}口 購入した。");
            portfolioModel.RefreshEstimate(itemModel, tomsModel.BlacksmithLevel.Value);
            Refresh();
        }
        else
        {
            showMessage("資金が足りないようだ。");
        }
    }

    private void HandleSell(int quantity)
    {
        var product = portfolioModel.GetProduct(selectedProductId);
        if (product == null || product.kind != FinancialProductKind.IndexFund) return;

        int income = portfolioModel.SellFund(product.productId, quantity, tomsModel, itemModel, tomsModel.BlacksmithLevel.Value);
        if (income > 0)
        {
            SoundManager.Instance?.PlaySE("営業/SE_売上音");
            showMessage($"{product.productName} を解約して {income:N0}G を受け取った。");
            portfolioModel.RefreshEstimate(itemModel, tomsModel.BlacksmithLevel.Value);
            Refresh();
        }
        else
        {
            showMessage("解約できる保有口数がない。");
        }
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
