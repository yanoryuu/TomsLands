using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R3;
using UnityEngine;

/// <summary>
/// 金融資産（債券・ファンド）の保有・売買・満期償還・永続化を担当するモデル。
/// - ファンドの基準価額(NAV) = fundBaseUnitPrice × 解放済み構成銘柄の (現在価格/基準価格) の平均
///   ※ 未解放銘柄（鍛冶屋レベル超）は価格が凍結されているため NAV 計算から除外する
/// - 債券は購入時に資金ロック、満期ターンの朝（GameFlowManager.NextTurn）に元本+利息で償還
/// - 破産判定は現金のみ。ただし強制返済時に不足があれば LiquidateForDebt で割増手数料付き強制売却できる
/// お金(PlayerMoney)の増減は TomsModel 経由で行う。
/// </summary>
public class PortfolioModel
{
    private const string FileName = "portfolioData.json";

    private readonly List<FinancialProductData> products;
    private readonly FinanceSettings settings;

    public List<FinancePosition> Positions { get; } = new();

    /// <summary>ファンド基準価額の履歴（チャート用・セーブ対象）。</summary>
    private readonly Dictionary<string, List<int>> navHistories = new();

    /// <summary>保有資産の概算評価額合計（UIバインド用）。RefreshEstimate で更新。</summary>
    public ReactiveProperty<int> TotalAssetsEstimate { get; } = new(0);

    public PortfolioModel(List<FinancialProductData> products, FinanceSettings settings)
    {
        this.products = products ?? new List<FinancialProductData>();
        this.settings = settings;
        LoadData();
    }

    public FinancialProductData GetProduct(string productId) =>
        products.FirstOrDefault(p => p.productId == productId);

    /// <summary>情報屋レベルで解放済みの商品一覧。</summary>
    public List<FinancialProductData> GetUnlockedProducts(int infoBrokerLevel) =>
        products.Where(p => p.unlockInfoBrokerLevel <= infoBrokerLevel).ToList();

    public List<FinancialProductData> AllProducts => products;

    // ========================================
    // 基準価額（NAV）
    // ========================================

    /// <summary>
    /// ファンドの現在の1口価格。構成銘柄（解放済みのみ）の現在価格/基準価格の平均に連動する。
    /// </summary>
    public int CalculateFundUnitPrice(FinancialProductData product, ItemModel itemModel, int blacksmithLevel)
    {
        if (product == null || product.kind != FinancialProductKind.IndexFund) return 0;
        if (itemModel == null) return product.fundBaseUnitPrice;

        float sum = 0f;
        int count = 0;
        foreach (var r in itemModel.RuntimeItems)
        {
            if (r.RequiredLevel.Value > blacksmithLevel) continue; // 未解放銘柄は価格凍結のため除外
            if (product.useAttributeFilter && r.ItemAttribute != product.attribute) continue;

            var master = itemModel.GetMasterItem(r.ItemId);
            if (master == null || master.basePrice <= 0) continue;

            sum += (float)r.CurrentPrice.Value / master.basePrice;
            count++;
        }

        if (count == 0) return product.fundBaseUnitPrice;
        return Mathf.Max(1, Mathf.RoundToInt(product.fundBaseUnitPrice * (sum / count)));
    }

    public IReadOnlyList<int> GetNavHistory(string productId) =>
        navHistories.TryGetValue(productId, out var list) ? list : new List<int>();

    // ========================================
    // 売買
    // ========================================

    /// <summary>債券を購入する（資金ロック）。成功なら true。</summary>
    public bool BuyBond(FinancialProductData product, int units, TomsModel tomsModel, int currentTurn)
    {
        if (product == null || product.kind != FinancialProductKind.Bond || units <= 0) return false;

        int cost = product.bondUnitPrice * units;
        if (tomsModel.PlayerMoney.Value < cost) return false;

        tomsModel.PurchaseItem(cost);
        Positions.Add(new FinancePosition
        {
            PositionId = Guid.NewGuid().ToString("N"),
            ProductId = product.productId,
            Kind = FinancialProductKind.Bond,
            Units = units,
            UnitCost = product.bondUnitPrice,
            AcquiredTurn = currentTurn,
            MaturityTurn = currentTurn + Mathf.Max(1, product.bondMaturityTurns),
        });

        SaveData();
        tomsModel.SavePlayerMoney();
        Debug.Log($"[Finance] 債券購入: {product.productName} ×{units} = {cost}G (満期: Turn {currentTurn + product.bondMaturityTurns})");
        return true;
    }

    /// <summary>ファンドを購入する（購入手数料込み）。成功なら true。</summary>
    public bool BuyFund(FinancialProductData product, int units, TomsModel tomsModel, ItemModel itemModel, int blacksmithLevel, int currentTurn)
    {
        if (product == null || product.kind != FinancialProductKind.IndexFund || units <= 0) return false;

        int unitPrice = CalculateFundUnitPrice(product, itemModel, blacksmithLevel);
        float feeRate = settings != null ? settings.fundBuyFeeRate : 0.02f;
        int cost = Mathf.RoundToInt(unitPrice * units * (1f + feeRate));
        if (tomsModel.PlayerMoney.Value < cost) return false;

        tomsModel.PurchaseItem(cost);
        Positions.Add(new FinancePosition
        {
            PositionId = Guid.NewGuid().ToString("N"),
            ProductId = product.productId,
            Kind = FinancialProductKind.IndexFund,
            Units = units,
            UnitCost = unitPrice,
            AcquiredTurn = currentTurn,
            MaturityTurn = 0,
        });

        SaveData();
        tomsModel.SavePlayerMoney();
        Debug.Log($"[Finance] ファンド購入: {product.productName} ×{units}口 @ {unitPrice}G (手数料込み {cost}G)");
        return true;
    }

    /// <summary>
    /// ファンドを解約する（先入れ先出し）。解約口数ぶんの現金（手数料差引後）を入金して返す。
    /// </summary>
    public int SellFund(string productId, int units, TomsModel tomsModel, ItemModel itemModel, int blacksmithLevel, bool forced = false)
    {
        var product = GetProduct(productId);
        if (product == null || product.kind != FinancialProductKind.IndexFund || units <= 0) return 0;

        int held = GetHeldUnits(productId);
        units = Mathf.Min(units, held);
        if (units <= 0) return 0;

        int unitPrice = CalculateFundUnitPrice(product, itemModel, blacksmithLevel);
        float feeRate = settings != null ? settings.fundSellFeeRate : 0.02f;
        if (forced && settings != null) feeRate += settings.forcedSaleExtraFeeRate;

        int income = Mathf.RoundToInt(unitPrice * units * (1f - feeRate));

        // 先入れ先出しでポジションから口数を減らす
        int remaining = units;
        foreach (var pos in Positions.Where(p => p.ProductId == productId).OrderBy(p => p.AcquiredTurn).ToList())
        {
            if (remaining <= 0) break;
            int take = Mathf.Min(pos.Units, remaining);
            pos.Units -= take;
            remaining -= take;
            if (pos.Units <= 0) Positions.Remove(pos);
        }

        tomsModel.AddRevenue(income);
        SaveData();
        tomsModel.SavePlayerMoney();
        Debug.Log($"[Finance] ファンド解約: {product.productName} ×{units}口 @ {unitPrice}G → +{income}G (forced={forced})");
        return income;
    }

    /// <summary>指定商品の保有口数合計。</summary>
    public int GetHeldUnits(string productId) =>
        Positions.Where(p => p.ProductId == productId).Sum(p => p.Units);

    // ========================================
    // ターン処理（満期償還・NAV履歴）
    // ========================================

    /// <summary>
    /// 日送り時に呼ぶ。満期を迎えた債券を償還（入金は呼び出し側）し、ファンドのNAV履歴を記録する。
    /// </summary>
    public FinanceTurnResult ApplyTurn(int currentTurn, ItemModel itemModel, int blacksmithLevel, RelicEffectResolver relicResolver = null)
    {
        var result = new FinanceTurnResult();

        // 満期債券の償還
        foreach (var pos in Positions.Where(p => p.Kind == FinancialProductKind.Bond && p.MaturityTurn <= currentTurn).ToList())
        {
            var product = GetProduct(pos.ProductId);
            float rate = product != null ? product.bondInterestRate : 0f;
            int principal = pos.Principal;
            int interest = Mathf.RoundToInt(principal * rate);

            // レリック補正（不労所得ビルド: FinanceYieldMul）。利息のみ増幅（元本は不変）
            if (relicResolver != null)
                interest = Mathf.Max(0, relicResolver.ModifyInt(RelicStatId.FinanceYieldMul, interest));

            result.BondPayout += principal + interest;
            result.MaturedBonds.Add((product != null ? product.productName : pos.ProductId, principal, interest));
            Positions.Remove(pos);
        }

        // ファンドNAV履歴の記録（価格更新後に呼ばれる前提）
        int capacity = settings != null ? Mathf.Max(1, settings.navHistoryCapacity) : 12;
        foreach (var product in products.Where(p => p.kind == FinancialProductKind.IndexFund))
        {
            if (string.IsNullOrEmpty(product.productId)) continue;
            if (!navHistories.TryGetValue(product.productId, out var history))
            {
                history = new List<int>();
                navHistories[product.productId] = history;
            }
            history.Add(CalculateFundUnitPrice(product, itemModel, blacksmithLevel));
            while (history.Count > capacity) history.RemoveAt(0);
        }

        RefreshEstimate(itemModel, blacksmithLevel);
        return result;
    }

    // ========================================
    // 評価額・強制売却
    // ========================================

    /// <summary>保有資産の概算評価額（ファンド=現在NAV、債券=元本）を再計算する。</summary>
    public void RefreshEstimate(ItemModel itemModel, int blacksmithLevel)
    {
        int total = 0;
        foreach (var pos in Positions)
        {
            if (pos.Kind == FinancialProductKind.Bond)
            {
                total += pos.Principal;
            }
            else
            {
                var product = GetProduct(pos.ProductId);
                total += CalculateFundUnitPrice(product, itemModel, blacksmithLevel) * pos.Units;
            }
        }
        TotalAssetsEstimate.Value = total;
    }

    /// <summary>強制売却で調達できる金額の概算（割増手数料・中途解約ペナルティ適用後）。</summary>
    public int GetForcedLiquidationValue(ItemModel itemModel, int blacksmithLevel)
    {
        float fundFee = settings != null ? settings.fundSellFeeRate + settings.forcedSaleExtraFeeRate : 0.12f;
        float bondRate = settings != null ? settings.bondEarlyRedemptionRate : 0.85f;

        int total = 0;
        foreach (var pos in Positions)
        {
            if (pos.Kind == FinancialProductKind.Bond)
            {
                total += Mathf.RoundToInt(pos.Principal * bondRate);
            }
            else
            {
                var product = GetProduct(pos.ProductId);
                int unitPrice = CalculateFundUnitPrice(product, itemModel, blacksmithLevel);
                total += Mathf.RoundToInt(unitPrice * pos.Units * (1f - fundFee));
            }
        }
        return total;
    }

    /// <summary>
    /// 借金の強制返済時、現金が不足しているときの強制売却。
    /// 不足額を満たすまでファンド → 債券の順に売却し、調達額を入金して返す。
    /// </summary>
    public int LiquidateForDebt(int shortfall, TomsModel tomsModel, ItemModel itemModel, int blacksmithLevel)
    {
        if (shortfall <= 0) return 0;

        int raised = 0;
        float bondRate = settings != null ? settings.bondEarlyRedemptionRate : 0.85f;

        // まずファンドから（流動性が高い）
        foreach (var product in products.Where(p => p.kind == FinancialProductKind.IndexFund))
        {
            if (raised >= shortfall) break;
            int held = GetHeldUnits(product.productId);
            if (held <= 0) continue;
            raised += SellFund(product.productId, held, tomsModel, itemModel, blacksmithLevel, forced: true);
        }

        // 足りなければ債券を中途解約（元本の一部のみ・利息なし）
        foreach (var pos in Positions.Where(p => p.Kind == FinancialProductKind.Bond).OrderBy(p => p.MaturityTurn).ToList())
        {
            if (raised >= shortfall) break;
            int refund = Mathf.RoundToInt(pos.Principal * bondRate);
            tomsModel.AddRevenue(refund);
            raised += refund;
            Positions.Remove(pos);
            Debug.Log($"[Finance] 債券を中途解約: {pos.ProductId} 元本{pos.Principal}G → {refund}G");
        }

        SaveData();
        tomsModel.SavePlayerMoney();
        Debug.Log($"[Finance] 強制売却で {raised}G を調達（不足額 {shortfall}G）");
        return raised;
    }

    // ========================================
    // 永続化
    // ========================================

    public void SaveData()
    {
        var data = new PortfolioSaveData
        {
            positions = Positions.Select(p => p.ToPlain()).ToList(),
            navHistories = navHistories
                .Select(kv => new FundNavHistoryPlain { productId = kv.Key, navHistory = new List<int>(kv.Value) })
                .ToList(),
        };
        File.WriteAllText(SaveSlotManager.GetPath(FileName), JsonUtility.ToJson(data, true));
    }

    public void LoadData()
    {
        Positions.Clear();
        navHistories.Clear();

        string path = SaveSlotManager.GetPath(FileName);
        if (File.Exists(path))
        {
            var data = JsonUtility.FromJson<PortfolioSaveData>(File.ReadAllText(path));
            if (data?.positions != null)
            {
                foreach (var plain in data.positions)
                {
                    if (string.IsNullOrEmpty(plain.productId) || plain.units <= 0) continue;
                    Positions.Add(FinancePosition.FromPlain(plain));
                }
            }
            if (data?.navHistories != null)
            {
                foreach (var nav in data.navHistories)
                {
                    if (string.IsNullOrEmpty(nav.productId)) continue;
                    navHistories[nav.productId] = new List<int>(nav.navHistory ?? new List<int>());
                }
            }
        }
        // 旧セーブ（ファイルなし）はポジションゼロとして扱う
    }

    /// <summary>ニューゲーム用リセット。</summary>
    public void Clear()
    {
        Positions.Clear();
        navHistories.Clear();
        TotalAssetsEstimate.Value = 0;
    }
}
