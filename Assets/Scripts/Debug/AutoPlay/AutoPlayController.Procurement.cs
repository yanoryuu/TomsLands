#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>DebugAutoPlayer から AutoPlay に任せた操作の進行状況。</summary>
public enum AutoPlayExternalStep
{
    /// <summary>AutoPlay は扱わない（DebugAutoPlayer の従来処理で進める）。</summary>
    NotHandled,
    /// <summary>AutoPlay が操作中（待つ）。</summary>
    Busy,
    /// <summary>AutoPlay が済ませた。</summary>
    Done,
}

/// <summary>
/// 仕入れの UI 操作（Docs/AutoPlay_Usage.md「配信前の仕入れ」）。
///
/// - 鍛冶屋: 「お任せ購入」ボタン → 予算ポップアップで方針ドロップダウンと予算を入力 → 「購入」を実際に押す。
///   次の配信が近い（StreamPrepTurns 以内・配信日の朝）なら、戦略の ChooseStreamPurchase に従い
///   まず「次ダンジョン特化」（弱点属性に一致する武具を優先）で買い、残りをいつもの方針で買う。
/// - 情報屋: 次の配信先の弱点が分からないとき（IntelPrepTurns 以内）、戦略が買うと判断したら
///   店の「情報屋」→ 地図 → そのダンジョン → 「情報を買う」→ 閉じる、を押してから鍛冶屋へ行く。
///   弱点は情報を買うまで仕入れ画面のバナーにも出ず、お任せの「次ダンジョン特化」も効かない（ゲーム仕様）。
/// どちらも DebugAutoPlayer のフック（ExternalBlacksmithBuyer / ExternalBeforeProcurement）から呼ばれ、
/// 操作中は DebugAutoPlayer.ExternalBusy で DebugAutoPlayer を止める。
/// </summary>
public sealed partial class AutoPlayController
{
    private const float UiStepSeconds = 0.45f;

    private string _buyRunningKey;
    private string _buyDoneKey;
    private string _buyFailedKey;
    private string _intelCheckedKey;
    private bool _intelRunning;
    private string _prefetchKey;

    // 店への投資（IAutoPlayShopInvestor）。鍛冶屋の開発はお任せ購入の前に押すので、結果はそこで締める
    private AutoPlayShopInvestResult _investResult;
    private bool _smithPending;
    // 陳列フェーズでする寄り道（魔王軍支援・店の改装・設備）。ボタンが陳列フェーズにしか無いので、仕入れの前に決めてここで押す
    private bool _displayPending;
    private bool _displayRunning;
    private string _displayDoneKey;
    private int _displayDemonLevels;
    private AutoPlayStreamPrepContext _displayDemonPrep;
    private AutoPlayShopInvestPlan _displayInvest;

    private void InstallProcurementHooks()
    {
        DebugAutoPlayer.ExternalBlacksmithBuyer = OnBlacksmithBuy;
        DebugAutoPlayer.ExternalBeforeProcurement = OnBeforeProcurement;
        DebugAutoPlayer.ExternalBeforeDisplay = OnBeforeDisplay;
    }

    private static void UninstallProcurementHooks()
    {
        DebugAutoPlayer.ExternalBlacksmithBuyer = null;
        DebugAutoPlayer.ExternalBeforeProcurement = null;
        DebugAutoPlayer.ExternalBeforeDisplay = null;
        DebugAutoPlayer.ExternalBusy = false;
    }

    // =====================================================
    // 鍛冶屋: お任せ購入
    // =====================================================

    private AutoPlayExternalStep OnBlacksmithBuy(string key, int budget, AutoBuyStrategy strategy)
    {
        if (!AutoPlay.IsRunning || _cts == null) return AutoPlayExternalStep.NotHandled;
        if (_buyDoneKey == key) return AutoPlayExternalStep.Done;
        if (_buyFailedKey == key) return AutoPlayExternalStep.NotHandled;
        if (_buyRunningKey != null) return AutoPlayExternalStep.Busy;

        _buyRunningKey = key;
        RunBlacksmithBuyAsync(key, budget, strategy, _cts.Token).Forget();
        return AutoPlayExternalStep.Busy;
    }

    private async UniTaskVoid RunBlacksmithBuyAsync(string key, int budget, AutoBuyStrategy strategy, CancellationToken ct)
    {
        DebugAutoPlayer.ExternalBusy = true;
        bool done = false;
        try
        {
            done = await BuyAtBlacksmithAsync(budget, strategy, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogException(e);
            AutoPlay.AddLog($"鍛冶屋の購入操作で例外: {e.Message}");
        }
        finally
        {
            DebugAutoPlayer.ExternalBusy = false;
            if (done) _buyDoneKey = key;
            else _buyFailedKey = key; // 何も買えなかった → DebugAutoPlayer の従来処理に任せる
            _buyRunningKey = null;
        }
    }

    /// <summary>予算ポップアップを操作して買う。1 回でもお金が動いたら true。</summary>
    private async UniTask<bool> BuyAtBlacksmithAsync(int budget, AutoBuyStrategy strategy, CancellationToken ct)
    {
        var view = AutoPlayUi.Resolve<BlackSmithView>();
        var toms = AutoPlayUi.Resolve<TomsModel>();
        var items = AutoPlayUi.Resolve<ItemModel>();
        if (view == null || toms == null) return false;

        // 配信前なら「次ダンジョン特化」と「いつもの方針」に分けて買う
        var orders = new List<(int budget, AutoBuyStrategy strategy, string label)>();
        var prep = BuildStreamPrepContext(budget, AutoPlay.Settings.StreamPrepTurns);
        AutoPlayStreamPurchasePlan plan = default;
        if (prep != null)
        {
            plan = AutoPlay.Strategy.ChooseStreamPurchase(prep);
            if (!string.IsNullOrEmpty(plan.Reason)) AutoPlay.AddLog($"配信前の仕入れ: {plan.Reason}");
            // FocusBudget は個別の武器購入とダンジョン特化のお任せで共有（使った分は差し引く）
            if (plan.FocusBudget > 0) orders.Add((plan.FocusBudget, AutoBuyStrategy.DungeonFocus, "ダンジョン向け"));
            orders.Add((-1, plan.RegularStrategy, "残り"));
        }
        else
        {
            orders.Add((budget, strategy, null));
        }

        // 店への投資: 鍛冶屋の開発（レベルアップ）は買う前に押す（新しく並ぶ商品も買えるように）
        if (_smithPending && _investResult != null)
        {
            _smithPending = false;
            int moneyBefore = toms.PlayerMoney.Value;
            await UpgradeSmithAsync(view, toms, _investResult, ct);
            if (!_displayPending) FinishShopInvest();
            // 開発に使った分だけ仕入れの予算を減らす（残す額は変えない）
            budget = Mathf.Max(0, budget - Mathf.Max(0, moneyBefore - toms.PlayerMoney.Value));
        }

        var before = StockSnapshot(items);
        int startMoney = toms.PlayerMoney.Value;
        bool anyPressed = false;

        // 1) ダンジョン向けの武器を商品一覧から個別に買う（行を選ぶ → 数量 → 購入）
        if (plan.WeaponOrders != null && plan.WeaponOrders.Count > 0)
        {
            foreach (var (itemId, quantity) in plan.WeaponOrders)
            {
                if (startMoney - toms.PlayerMoney.Value >= budget) break;
                if (await BuyItemManuallyAsync(view, items, itemId, quantity, ct)) anyPressed = true;
            }
        }

        int focusUsed = 0;
        foreach (var o in orders)
        {
            int spent = startMoney - toms.PlayerMoney.Value;
            if (o.strategy == AutoBuyStrategy.DungeonFocus) focusUsed = spent; // 武器の個別購入で使った分
            int b = o.budget >= 0 ? Mathf.Min(o.budget - focusUsed, budget - spent) : budget - spent;
            if (b <= 0) continue;
            if (!await RunAutoBuyPopupAsync(view, b, o.strategy, o.label, ct)) break;
            anyPressed = true;
            await UniTask.Delay(TimeSpan.FromSeconds(ScaledWait(AutoPlay.Settings.ActionWait)), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        LogPurchases(before, items, prep, startMoney - toms.PlayerMoney.Value);
        return anyPressed;
    }

    /// <summary>「お任せ購入」→ 方針 → 予算 → 「購入」。</summary>
    private async UniTask<bool> RunAutoBuyPopupAsync(BlackSmithView view, int budget, AutoBuyStrategy strategy, string label,
        CancellationToken ct)
    {
        var popup = AutoPlayUi.GetField<AutoBuyBudgetPopup>(view, "autoBuyBudgetPopup");
        if (popup == null) return false;

        if (!popup.gameObject.activeInHierarchy)
        {
            if (!await PressAsync(AutoPlayUi.GetField<Button>(view, "autoBuyButton"), "鍛冶屋: お任せ購入を開く", ct)) return false;
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            if (popup == null || !popup.gameObject.activeInHierarchy) return false;
        }

        // 方針（ドロップダウンを開いて選ぶところまで見せる）。初回チュートリアルのロック中は触れない
        var dropdown = AutoPlayUi.GetField<TMP_Dropdown>(popup, "strategyDropdown");
        if (dropdown != null && dropdown.interactable && dropdown.value != (int)strategy)
        {
            dropdown.Show();
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            dropdown.value = (int)strategy;
            dropdown.Hide();
            AutoPlay.AddLog($"鍛冶屋: 方針「{StrategyLabel(strategy)}」を選択");
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        // 予算（1 文字ずつ入力）
        var input = AutoPlayUi.GetField<TMP_InputField>(popup, "budgetInputField");
        if (input != null && input.interactable)
        {
            string text = budget.ToString();
            input.text = "";
            foreach (char c in text)
            {
                input.text += c;
                await UniTask.Delay(TimeSpan.FromSeconds(0.04f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 0.5f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        string what = string.IsNullOrEmpty(label) ? StrategyLabel(strategy) : $"{label}・{StrategyLabel(strategy)}";
        return await PressAsync(AutoPlayUi.GetField<Button>(popup, "confirmButton"), $"鍛冶屋: お任せ購入（{what} / 予算 {budget:N0}G）", ct);
    }

    /// <summary>商品一覧から 1 銘柄を選んで数量を決め、詳細パネルの「購入」を押す。在庫が増えたら true。</summary>
    private async UniTask<bool> BuyItemManuallyAsync(BlackSmithView view, ItemModel items, string itemId, int quantity, CancellationToken ct)
    {
        var item = items?.GetRuntimeItem(itemId);
        if (item == null || quantity <= 0) return false;
        string name = $"{item.ItemName}（{AttributeLabel(item.ItemAttribute)}）";

        // タブを合わせる
        bool weapon = item.ItemType == ItemTypeData.ItemType.Weapon;
        var tab = AutoPlayUi.GetField<Button>(view, weapon ? "weaponButton" : "armorButton");
        if (AutoPlayUi.IsClickable(tab))
        {
            await PressAsync(tab, weapon ? "鍛冶屋: 武器タブ" : "鍛冶屋: 防具タブ", ct);
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        var slot = FindObjectsByType<ItemShopSlot>(FindObjectsSortMode.None)
            .FirstOrDefault(s => s != null && s.itemId == itemId && s.gameObject.activeInHierarchy);
        if (slot == null)
        {
            AutoPlay.AddLog($"鍛冶屋: 「{name}」が一覧に見つからない");
            return false;
        }

        // 一覧をスクロールして行を見せてから選ぶ
        var scroll = AutoPlayUi.GetField<ScrollRect>(view, "scrollRect");
        if (scroll != null && scroll.content != null && slot.transform.parent == scroll.content)
        {
            int count = scroll.content.childCount;
            int index = slot.transform.GetSiblingIndex();
            if (count > 1) scroll.verticalNormalizedPosition = 1f - (float)index / (count - 1);
        }
        if (!await AutoPlayUi.PointerClickAsync(slot.gameObject, AutoPlay.Settings.ButtonHoldSeconds, ct)) return false;
        AutoPlay.AddLog($"鍛冶屋: 「{name}」を選ぶ");
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        // 数量（スライダー。上限は所持金・在庫の空きで Presenter 側がクランプする）
        var panel = view.DetailPanel;
        var slider = AutoPlayUi.GetField<Slider>(panel, "quantitySlider");
        if (slider == null) return false;
        slider.value = Mathf.Min(quantity, slider.maxValue);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        int beforeStock = item.Stock.Value;
        int shown = Mathf.RoundToInt(slider.value);
        await PressAsync(AutoPlayUi.GetField<Button>(panel, "purchaseButton"), $"鍛冶屋: 「{name}」を {shown} 個購入", ct);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        return item.Stock.Value > beforeStock;
    }

    private static Dictionary<string, int> StockSnapshot(ItemModel items)
    {
        var map = new Dictionary<string, int>();
        if (items?.RuntimeItems == null) return map;
        foreach (var r in items.RuntimeItems)
            if (r != null) map[r.ItemId] = r.Stock.Value;
        return map;
    }

    private static void LogPurchases(Dictionary<string, int> before, ItemModel items, AutoPlayStreamPrepContext prep, int spent)
    {
        if (items?.RuntimeItems == null) return;
        var bought = new List<(RuntimeItemData item, int qty)>();
        foreach (var r in items.RuntimeItems)
        {
            if (r == null) continue;
            int prev = before.TryGetValue(r.ItemId, out var p) ? p : 0;
            if (r.Stock.Value > prev) bought.Add((r, r.Stock.Value - prev));
        }
        if (bought.Count == 0)
        {
            AutoPlay.AddLog("仕入れ結果: 買えた商品なし");
            return;
        }

        string list = string.Join("、", bought.OrderByDescending(b => b.qty).Take(6)
            .Select(b => $"{b.item.ItemName}({AttributeLabel(b.item.ItemAttribute)})×{b.qty}"));
        if (prep != null && prep.Weakness.HasValue)
        {
            var w = prep.Weakness.Value;
            int match = bought.Where(b => b.item.ItemAttribute == w).Sum(b => b.qty);
            int matchWeapons = bought.Where(b => b.item.ItemAttribute == w && b.item.ItemType == ItemTypeData.ItemType.Weapon).Sum(b => b.qty);
            int total = bought.Sum(b => b.qty);
            var eff = prep.EffectiveAttributes;
            int effAll = bought.Where(b => eff.Contains(b.item.ItemAttribute)).Sum(b => b.qty);
            int effWeapons = bought.Where(b => eff.Contains(b.item.ItemAttribute) && b.item.ItemType == ItemTypeData.ItemType.Weapon).Sum(b => b.qty);
            string effLabel = string.Join(",", eff.Select(AttributeLabel));
            AutoPlay.AddLog($"仕入れ結果（{prep.DungeonLabel} 弱点:{AttributeLabel(w)}）: {AttributeLabel(w)}属性 {match}/{total}個（うち武器 {matchWeapons}）" +
                            $"・有利属性({effLabel}) {effAll}個（うち武器 {effWeapons}） / {spent:N0}G : {list}");
        }
        else
        {
            AutoPlay.AddLog($"仕入れ結果: {spent:N0}G : {list}");
        }
    }

    // =====================================================
    // 情報屋: 次の配信先の情報を買う
    // =====================================================

    private bool OnBeforeProcurement(string key)
    {
        if (!AutoPlay.IsRunning || _cts == null) return false;
        if (_intelRunning) return true;

        // 人格プレイ: その日の判断を先にまとめてモデルに聞く（答えが来るまで店は待つ）。寄り道の判断はその後
        if (AutoPlay.Strategy is IAutoPlayPrefetchStrategy prefetch && _prefetchKey != key)
        {
            _prefetchKey = key;
            _intelRunning = true;
            RunPrefetchAsync(prefetch, _cts.Token).Forget();
            return true;
        }

        if (_intelCheckedKey == key) return false;
        _intelCheckedKey = key;

        var prep = BuildStreamPrepContext(0, AutoPlay.Settings.IntelPrepTurns);
        bool buyIntel = false;
        if (prep != null && !prep.WeaknessKnown)
        {
            if (prep.IntelCost <= 0)
                AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の弱点は不明（情報屋で扱っていない）");
            else if (!AutoPlay.Strategy.ShouldBuyDungeonIntel(prep))
                AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の情報（{prep.IntelCost:N0}G）は買わない（所持金 {prep.Money:N0}G）");
            else
                buyIntel = true;
        }

        // 実況用の寄り道（新聞・地図・魔王軍支援）。戦略が IAutoPlayJikkyoExtras のときだけ
        float newspaperSeconds = 0f, mapSeconds = 0f;
        int demonLevels = 0;
        AutoPlayStreamPrepContext demonPrep = null;
        if (AutoPlay.Strategy is IAutoPlayJikkyoExtras extras)
        {
            var ctx = CreateContext();
            newspaperSeconds = extras.NewspaperReadSeconds(ctx);
            mapSeconds = extras.MapPeekSeconds(ctx);
            demonPrep = BuildStreamPrepContext(0, Mathf.Max(1, AutoPlay.Settings.StreamPrepTurns));
            if (demonPrep != null) demonLevels = extras.ChooseDemonSupportLevels(demonPrep);
        }

        // 店への投資（改装・設備・取引所・勇者の装備。鍛冶屋の開発は鍛冶屋の画面で）。戦略が IAutoPlayShopInvestor のときだけ
        FinishShopInvest();   // 前の日の結果が締まっていなければ締める
        AutoPlayShopInvestPlan invest = default;
        if (AutoPlay.Strategy is IAutoPlayShopInvestor investor)
        {
            try { invest = investor.ChooseShopInvestments(CreateContext()); }
            catch (Exception e) { Debug.LogException(e); invest = default; }
            if (invest.Any)
            {
                _investResult = new AutoPlayShopInvestResult { Turn = AutoPlay.CurrentTurn };
                _smithPending = invest.UpgradeSmith;
                if (!string.IsNullOrEmpty(invest.Reason)) AutoPlay.AddLog($"店への投資の予定: {invest.Reason}");
            }
        }
        bool sideInvest = (!string.IsNullOrEmpty(invest.FinanceProductId) && invest.FinanceUnits != 0)
                          || invest.HeroWeaponId != null || invest.HeroArmorId != null || invest.HeroTactics != null || invest.UpgradeBroker;

        // 魔王軍支援・店の改装・設備のボタンは陳列フェーズにしか出ない（TurnPhaseView.displayGroup）。決めた内容を覚えて陳列の頭で押す
        // （2026-10-28 まで仕入れの前に押そうとしてボタンが押せず、魔王軍支援は一度も払われていなかった）
        _displayDemonLevels = demonPrep != null ? demonLevels : 0;
        _displayDemonPrep = demonPrep;
        _displayInvest = new AutoPlayShopInvestPlan
        {
            UpgradeShop = invest.UpgradeShop, MachineId = invest.MachineId,
            UpgradeMachineId = invest.UpgradeMachineId, RepairMachineId = invest.RepairMachineId,
        };
        _displayPending = _displayDemonLevels > 0 || _displayInvest.UpgradeShop || !string.IsNullOrEmpty(_displayInvest.MachineId)
                          || _displayInvest.HasMachineCare;
        _displayDoneKey = null;

        if (!buyIntel && newspaperSeconds <= 0f && mapSeconds <= 0f && !sideInvest)
        {
            if (!_smithPending && !_displayPending) FinishShopInvest();
            return false;
        }

        _intelRunning = true;
        var procInvest = invest;
        procInvest.UpgradeShop = false;
        procInvest.MachineId = null;
        procInvest.UpgradeMachineId = null;
        procInvest.RepairMachineId = null;
        procInvest.UpgradeSmith = false;
        RunSideTripsAsync(buyIntel ? prep : null, newspaperSeconds, mapSeconds, 0, null, sideInvest ? procInvest : default, _cts.Token).Forget();
        return true;
    }

    /// <summary>陳列フェーズの頭（オート陳列の前）。魔王軍支援・店の改装・設備を押す（1 日 1 回）。</summary>
    private bool OnBeforeDisplay(string key)
    {
        if (!AutoPlay.IsRunning || _cts == null) return false;
        if (_displayRunning) return true;
        if (_displayDoneKey == key) return false;
        _displayDoneKey = key;
        if (!_displayPending) return false;
        _displayRunning = true;
        RunDisplayTripsAsync(_cts.Token).Forget();
        return true;
    }

    private async UniTaskVoid RunDisplayTripsAsync(CancellationToken ct)
    {
        DebugAutoPlayer.ExternalBusy = true;
        try
        {
            if (_displayDemonLevels > 0 && _displayDemonPrep != null) await SupportDemonArmyAsync(_displayDemonPrep, _displayDemonLevels, ct);
            if (_investResult != null && (_displayInvest.UpgradeShop || !string.IsNullOrEmpty(_displayInvest.MachineId) || _displayInvest.HasMachineCare))
            {
                var toms = AutoPlayUi.Resolve<TomsModel>();
                if (toms != null)
                {
                    int start = toms.PlayerMoney.Value;
                    try { await ShopFacilityAsync(_displayInvest, _investResult, toms, ct); }
                    finally
                    {
                        _investResult.Spent += start - toms.PlayerMoney.Value;
                        AutoPlay.AddLog($"店への投資（陳列フェーズ）: {Summary(_investResult)}（{start - toms.PlayerMoney.Value:+#,0;-#,0;0}G、残り {toms.PlayerMoney.Value:N0}G）");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogException(e);
            AutoPlay.AddLog($"陳列フェーズの寄り道で例外: {e.Message}");
        }
        finally
        {
            var state = AutoPlayUi.Resolve<StateManager>();
            if (state != null && state.CurrentPhase.Value == GamePhase.TomsShop
                && state.CurrentTomsShopPhase.Value is TomsShopGamePhase.DungeonLevelUp or TomsShopGamePhase.ShopUpgrade or TomsShopGamePhase.MachineShop)
                state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
            _displayPending = false;
            _displayDemonLevels = 0;
            _displayDemonPrep = null;
            if (!_smithPending) FinishShopInvest();
            DebugAutoPlayer.ExternalBusy = false;
            _displayRunning = false;
        }
    }

    /// <summary>店への投資の結果を戦略に渡す（1 日 1 回。鍛冶屋の開発が残っていれば待つ）。</summary>
    private void FinishShopInvest()
    {
        if (_investResult == null) return;
        var r = _investResult;
        _investResult = null;
        _smithPending = false;
        _displayPending = false;
        try { (AutoPlay.Strategy as IAutoPlayShopInvestor)?.OnShopInvestmentsDone(r); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>人格プレイの先読み（IAutoPlayPrefetchStrategy.PrefetchShopDayAsync）。失敗しても戦略側が標準の判断で続ける。</summary>
    private async UniTaskVoid RunPrefetchAsync(IAutoPlayPrefetchStrategy prefetch, CancellationToken ct)
    {
        DebugAutoPlayer.ExternalBusy = true;
        try
        {
            var prep = BuildStreamPrepContext(0, Mathf.Max(AutoPlay.Settings.IntelPrepTurns, AutoPlay.Settings.StreamPrepTurns));
            await prefetch.PrefetchShopDayAsync(CreateContext(), prep, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogException(e);
            AutoPlay.AddLog($"人格の判断の取得で例外: {e.Message}");
        }
        finally
        {
            DebugAutoPlayer.ExternalBusy = false;
            _intelRunning = false;
        }
    }

    /// <summary>仕入れの前の寄り道をまとめて行う（新聞 → 地図 → 情報屋 → 魔王軍支援）。</summary>
    private async UniTaskVoid RunSideTripsAsync(AutoPlayStreamPrepContext intelPrep, float newspaperSeconds, float mapSeconds,
        int demonLevels, AutoPlayStreamPrepContext demonPrep, AutoPlayShopInvestPlan invest, CancellationToken ct)
    {
        DebugAutoPlayer.ExternalBusy = true;
        try
        {
            if (newspaperSeconds > 0f) await ReadNewspaperAsync(newspaperSeconds, ct);
            if (mapSeconds > 0f) await PeekMapAsync(mapSeconds, ct);
            if (intelPrep != null) await BuyDungeonIntelAsync(intelPrep, ct);
            if (demonLevels > 0 && demonPrep != null) await SupportDemonArmyAsync(demonPrep, demonLevels, ct);
            if (_investResult != null && invest.Any)
            {
                try { await ShopInvestAsync(invest, _investResult, ct); }
                finally { if (!_smithPending && !_displayPending) FinishShopInvest(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogException(e);
            AutoPlay.AddLog($"寄り道の操作で例外: {e.Message}");
        }
        finally
        {
            // 別画面を開いたまま止まらないよう、店ホームへ戻っていなければ戻す
            var state = AutoPlayUi.Resolve<StateManager>();
            if (state != null && state.CurrentPhase.Value == GamePhase.TomsShop
                && state.CurrentTomsShopPhase.Value is TomsShopGamePhase.Broker or TomsShopGamePhase.Newspaper
                    or TomsShopGamePhase.Map or TomsShopGamePhase.DungeonLevelUp
                    or TomsShopGamePhase.ShopUpgrade or TomsShopGamePhase.MachineShop or TomsShopGamePhase.Hero)
                state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
            DebugAutoPlayer.ExternalBusy = false;
            _intelRunning = false;
        }
    }

    /// <summary>朝刊を開いて眺め、途中で次のページをめくり、閉じる（実況の画づくり用）。</summary>
    private async UniTask ReadNewspaperAsync(float seconds, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        if (state == null || shop == null) return;
        if (!await PressAsync(AutoPlayUi.GetField<Button>(shop, "NewspaperButton"), "店: 新聞を読む", ct)) return;
        if (!await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Newspaper, 5f, ct)) return;
        await UniTask.Delay(TimeSpan.FromSeconds(seconds * 0.6f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        var news = AutoPlayUi.Resolve<NewspaperView>();
        var next = AutoPlayUi.GetField<Button>(news, "nextPageButton");
        if (AutoPlayUi.IsClickable(next))
        {
            await PressAsync(next, "新聞: 次の面", ct);
            await UniTask.Delay(TimeSpan.FromSeconds(seconds * 0.4f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
        await PressAsync(AutoPlayUi.GetField<Button>(news, "closeButton"), "新聞を閉じる", ct);
        await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value != TomsShopGamePhase.Newspaper, 5f, ct);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    /// <summary>地図を開いて次の配信先を眺め、戻る。</summary>
    private async UniTask PeekMapAsync(float seconds, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        if (state == null || shop == null) return;
        if (!await PressAsync(AutoPlayUi.GetField<Button>(shop, "MapButton"), "店: 地図を見る", ct)) return;
        if (!await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Map, 5f, ct)) return;
        await UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        var map = AutoPlayUi.Resolve<MapView>();
        await PressAsync(AutoPlayUi.GetField<Button>(map, "backButton"), "地図を閉じる", ct);
        await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value != TomsShopGamePhase.Map, 5f, ct);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    /// <summary>魔王軍支援を開き、次の配信先をレベルアップする（所持金の 70% を上限に、押せた回数だけ）。</summary>
    private async UniTask SupportDemonArmyAsync(AutoPlayStreamPrepContext prep, int maxLevels, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        var toms = AutoPlayUi.Resolve<TomsModel>();
        if (state == null || shop == null || toms == null) return;

        int startMoney = toms.PlayerMoney.Value;
        // 1 段も上げられない（上限・所持金が 1 段の額に足りない）なら画面を開かない（2026-10-30: 0G の支援操作が 10/46。仕入れで使い切っていた）
        var target = AutoPlayUi.Resolve<DungeonRepository>()?.GetById(prep.Dungeon);
        if (target != null && (target.currentDungeonLevel >= GameConst.MaxDungeonLevel || target.levelUpCost <= 0 || target.levelUpCost > startMoney))
        {
            AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の魔王軍支援はやめる（Lv{target.currentDungeonLevel}、1 段 {target.levelUpCost:N0}G / 所持金 {startMoney:N0}G）");
            return;
        }
        int floor = startMoney * 3 / 10;
        AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の魔王軍支援へ（所持金 {startMoney:N0}G、{floor:N0}G を残して出資）");
        if (!await PressAsync(AutoPlayUi.GetField<Button>(shop, "DungeonLevelUpButton"), "店: 魔王軍支援", ct)) return;
        if (!await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.DungeonLevelUp, 5f, ct)) return;
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 3f), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        var slot = FindObjectsByType<DungeonLevelUpSlot>(FindObjectsSortMode.None).FirstOrDefault(s => s.DungeonKey == prep.Dungeon);
        int done = 0;
        if (slot != null)
        {
            var select = AutoPlayUi.GetField<Button>(slot, "selectButton");
            if (AutoPlayUi.IsClickable(select))
            {
                await PressAsync(select, $"魔王軍支援: {prep.DungeonLabel} を選ぶ", ct);
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            for (int i = 0; i < maxLevels; i++)
            {
                // スロットは RefreshList で作り直されるので毎回取り直す
                slot = FindObjectsByType<DungeonLevelUpSlot>(FindObjectsSortMode.None).FirstOrDefault(s => s.DungeonKey == prep.Dungeon);
                var up = slot != null ? AutoPlayUi.GetField<Button>(slot, "levelUpButton") : null;
                if (!AutoPlayUi.IsClickable(up)) break;
                int before = toms.PlayerMoney.Value;
                if (!await PressAsync(up, $"魔王軍支援: {prep.DungeonLabel} をレベルアップ（{done + 1}回目）", ct)) break;
                await UniTask.Delay(TimeSpan.FromSeconds(1.2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                if (toms.PlayerMoney.Value >= before) break;   // お金が減らなかった＝上限か不足
                done++;
                if (toms.PlayerMoney.Value <= floor) break;
            }
        }
        AutoPlay.AddLog($"魔王軍支援: {prep.DungeonLabel} を {done} 回レベルアップ（{startMoney - toms.PlayerMoney.Value:N0}G 出資、残り {toms.PlayerMoney.Value:N0}G）");
        await UniTask.Delay(TimeSpan.FromSeconds(2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        var view = AutoPlayUi.Resolve<DungeonLevelUpView>();
        await PressAsync(AutoPlayUi.GetField<Button>(view, "closeButton"), "魔王軍支援を閉じる", ct);
        await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value != TomsShopGamePhase.DungeonLevelUp, 5f, ct);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    private async UniTask BuyDungeonIntelAsync(AutoPlayStreamPrepContext prep, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        if (state == null || shop == null) return;

        AutoPlay.AddLog($"配信前: 次の配信先 {prep.DungeonLabel} の弱点が分からないので情報屋へ（情報料 {prep.IntelCost:N0}G）");
        if (!await PressAsync(AutoPlayUi.GetField<Button>(shop, "InfoButton"), "店: 情報屋へ", ct)) return;
        if (!await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Broker, 5f, ct)) return;
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        var broker = AutoPlayUi.Resolve<InfoBrokerView>();
        var mapButton = AutoPlayUi.GetField<Button>(broker, "mapButton");
        if (AutoPlayUi.IsClickable(mapButton))
        {
            await PressAsync(mapButton, "情報屋: 地図", ct);
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        // 配信先のダンジョンを選ぶ
        var slot = FindObjectsByType<MapPurchaseSlot>(FindObjectsSortMode.None).FirstOrDefault(s => s.DungeonKey == prep.Dungeon);
        if (slot != null)
        {
            var select = AutoPlayUi.GetField<Button>(slot, "infoButton") ?? AutoPlayUi.GetField<Button>(slot, "purchaseButton");
            await PressAsync(select, $"情報屋: {prep.DungeonLabel} を選ぶ", ct);
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }

        var mapView = AutoPlayUi.Resolve<MapInfoView>();
        var buy = AutoPlayUi.GetField<Button>(mapView, "purchaseButton");
        if (await PressAsync(buy, $"情報屋: {prep.DungeonLabel} の情報を買う（{prep.IntelCost:N0}G）", ct))
        {
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 3f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            var after = BuildStreamPrepContext(0, int.MaxValue);
            AutoPlay.AddLog(after != null && after.WeaknessKnown && after.Weakness.HasValue
                ? $"情報屋: {prep.DungeonLabel} の弱点は {AttributeLabel(after.Weakness.Value)} 属性と判明"
                : $"情報屋: {prep.DungeonLabel} の情報を買えなかった");
        }

        await PressAsync(AutoPlayUi.GetField<Button>(broker, "closeButton"), "情報屋を閉じる", ct);
        await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value != TomsShopGamePhase.Broker, 5f, ct);
    }

    private static async UniTask<bool> WaitUntilAsync(Func<bool> predicate, float timeoutSeconds, CancellationToken ct)
    {
        float deadline = Time.unscaledTime + timeoutSeconds;
        while (!predicate())
        {
            if (Time.unscaledTime > deadline) return false;
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
        return true;
    }

    // =====================================================
    // 店への投資（IAutoPlayShopInvestor）: 勇者の装備・店の改装・設備・情報屋の開発・取引所。鍛冶屋の開発は鍛冶屋の画面で
    // 画面のボタンを実際に押す（リフレクション名: TomsShopView の HeroButton / ShopUpgradeButton / InfoButton、
    // HeroPanelView の weaponDropdown / armorDropdown / tacticsDropdown / weaponIds / armorIds / tacticsValues / closeButton、
    // ShopUpgradeView の upgradeButton / closeButton / tabs、ShopFacilityTabsView の machineTab、
    // ShopMachineView の purchaseButton / upgradeButton / repairButton / closeButton、
    // ShopMachineSlotUI の selectButton、InfoBrokerView の developmentButton / levelUpButton / exchangeButton / closeButton、
    // FinanceDetailPanel の quantitySlider / buyButton / sellButton、BlackSmithView の developButton / levelUpButton / weaponButton）
    // =====================================================

    private async UniTask ShopInvestAsync(AutoPlayShopInvestPlan plan, AutoPlayShopInvestResult result, CancellationToken ct)
    {
        var toms = AutoPlayUi.Resolve<TomsModel>();
        if (toms == null) return;
        int startMoney = toms.PlayerMoney.Value;
        try
        {
            if (plan.HeroWeaponId != null || plan.HeroArmorId != null || plan.HeroTactics != null)
                await EquipHeroAsync(plan, result, ct);
            if (plan.UpgradeShop || !string.IsNullOrEmpty(plan.MachineId) || plan.HasMachineCare)
                await ShopFacilityAsync(plan, result, toms, ct);
            if (plan.UpgradeBroker || (!string.IsNullOrEmpty(plan.FinanceProductId) && plan.FinanceUnits != 0))
                await BrokerInvestAsync(plan, result, toms, ct);
        }
        finally
        {
            result.Spent += startMoney - toms.PlayerMoney.Value;
            AutoPlay.AddLog($"店への投資: {Summary(result)}（{startMoney - toms.PlayerMoney.Value:+#,0;-#,0;0}G、残り {toms.PlayerMoney.Value:N0}G）");
        }
    }

    private static string Summary(AutoPlayShopInvestResult r)
    {
        var parts = new List<string>();
        if (r.HeroWeaponEquipped != null) parts.Add($"勇者の武器 {r.HeroWeaponEquipped}");
        if (r.HeroArmorEquipped != null) parts.Add($"勇者の防具 {r.HeroArmorEquipped}");
        if (r.HeroTacticsSet != null) parts.Add($"作戦 {r.HeroTacticsSet}");
        if (r.ShopUpgraded) parts.Add($"店 Lv{r.ShopLevelAfter}");
        if (!string.IsNullOrEmpty(r.MachinePlaced)) parts.Add($"設備 {r.MachinePlaced}");
        if (!string.IsNullOrEmpty(r.MachineRepaired)) parts.Add($"修理 {r.MachineRepaired}");
        if (!string.IsNullOrEmpty(r.MachineUpgraded)) parts.Add($"強化 {r.MachineUpgraded}");
        if (r.BrokerUpgraded) parts.Add($"情報屋 Lv{r.BrokerLevelAfter}");
        if (r.SmithUpgraded) parts.Add($"鍛冶屋 Lv{r.SmithLevelAfter}");
        if (r.FinanceUnits != 0) parts.Add($"{r.FinanceProductId} {(r.FinanceUnits > 0 ? "買い" : "売り")} {Math.Abs(r.FinanceUnits)} 口");
        parts.AddRange(r.Notes);
        return parts.Count > 0 ? string.Join(" / ", parts) : "押せたものなし";
    }

    private static T ViewOf<T>() where T : UnityEngine.Object =>
        AutoPlayUi.Resolve<T>() ?? FindFirstObjectByType<T>(FindObjectsInactive.Include);

    private async UniTask<bool> OpenShopScreenAsync(string buttonField, TomsShopGamePhase phase, string label, AutoPlayShopInvestResult result, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        if (state == null || shop == null) return false;
        if (state.CurrentTomsShopPhase.Value == phase) return true;
        var button = AutoPlayUi.GetField<Button>(shop, buttonField);
        if (!AutoPlayUi.IsClickable(button))
        {
            result.Notes.Add($"{label}のボタンが押せない（まだ解禁されていない）");
            return false;
        }
        if (!await PressAsync(button, $"店: {label}", ct)) return false;
        if (await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == phase, 5f, ct))
        {
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            return true;
        }
        result.Notes.Add($"{label}の画面が開かなかった");
        return false;
    }

    private async UniTask CloseToShopAsync(object view, string closeField, string label, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        if (state == null || state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Shop) return;
        await PressFieldAsync(view, closeField, $"{label}を閉じる", ct);
        if (!await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Shop, 5f, ct))
            state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
    }

    /// <summary>ドロップダウンで itemId / 値を選ぶ（開いて見せてから値を入れる。onValueChanged → View の Subject → Presenter の本物の経路）。</summary>
    private async UniTask<bool> PickDropdownAsync<TKey>(object view, string dropdownField, string idsField, TKey wanted, string label, CancellationToken ct)
    {
        var dd = AutoPlayUi.GetField<TMP_Dropdown>(view, dropdownField);
        var ids = AutoPlayUi.GetFieldValue(view, idsField) as System.Collections.IList;
        if (dd == null || ids == null || !dd.interactable) return false;
        int index = -1;
        for (int i = 0; i < ids.Count; i++)
            if (Equals(ids[i], wanted)) { index = i; break; }
        if (index < 0) return false;
        if (dd.value == index) return true;
        dd.Show();
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        dd.value = index;
        dd.Hide();
        AutoPlay.AddLog(label);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        return true;
    }

    // ---------------- 勇者の装備 ----------------

    private async UniTask EquipHeroAsync(AutoPlayShopInvestPlan plan, AutoPlayShopInvestResult result, CancellationToken ct)
    {
        if (!await OpenShopScreenAsync("HeroButton", TomsShopGamePhase.Hero, "勇者", result, ct)) return;
        var view = ViewOf<HeroPanelView>();
        var hero = AutoPlayUi.Resolve<HeroModel>()?.heroData;
        var items = AutoPlayUi.Resolve<ItemModel>();
        if (view == null || hero == null) return;
        string NameOf(string id) => items?.GetRuntimeItem(id)?.ItemName ?? id;

        if (plan.HeroWeaponId != null && hero.weaponId.Value != plan.HeroWeaponId
            && await PickDropdownAsync(view, "weaponDropdown", "weaponIds", plan.HeroWeaponId, $"勇者: 武器を「{NameOf(plan.HeroWeaponId)}」に", ct))
        {
            if (hero.weaponId.Value == plan.HeroWeaponId) result.HeroWeaponEquipped = plan.HeroWeaponId;
            else result.Notes.Add("武器を替えられなかった");
        }
        if (plan.HeroArmorId != null && hero.armorId.Value != plan.HeroArmorId
            && await PickDropdownAsync(view, "armorDropdown", "armorIds", plan.HeroArmorId, $"勇者: 防具を「{NameOf(plan.HeroArmorId)}」に", ct))
        {
            if (hero.armorId.Value == plan.HeroArmorId) result.HeroArmorEquipped = plan.HeroArmorId;
            else result.Notes.Add("防具を替えられなかった");
        }
        if (plan.HeroTactics != null && Enum.TryParse<HeroTactics>(plan.HeroTactics, out var tactics) && hero.tactics.Value != tactics
            && await PickDropdownAsync(view, "tacticsDropdown", "tacticsValues", tactics, $"勇者: 作戦を {tactics} に", ct))
        {
            if (hero.tactics.Value == tactics) result.HeroTacticsSet = plan.HeroTactics;
        }
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        await CloseToShopAsync(view, "closeButton", "勇者の画面", ct);
    }

    // ---------------- 店の改装・設備 ----------------

    private async UniTask ShopFacilityAsync(AutoPlayShopInvestPlan plan, AutoPlayShopInvestResult result, TomsModel toms, CancellationToken ct)
    {
        if (!await OpenShopScreenAsync("ShopUpgradeButton", TomsShopGamePhase.ShopUpgrade, "店（改装・設備）", result, ct)) return;
        var state = AutoPlayUi.Resolve<StateManager>();
        var upgradeView = ViewOf<ShopUpgradeView>();
        object lastView = upgradeView;
        if (plan.UpgradeShop && upgradeView != null)
        {
            int before = toms.ShopLevel.Value;
            if (await PressFieldAsync(upgradeView, "upgradeButton", $"店: 改装（Lv{before} → Lv{before + 1}）", ct))
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                result.ShopUpgraded = toms.ShopLevel.Value > before;
                result.ShopLevelAfter = toms.ShopLevel.Value;
            }
            if (!result.ShopUpgraded) result.Notes.Add("改装のボタンが押せなかった（お金か最大レベル）");
        }

        if ((!string.IsNullOrEmpty(plan.MachineId) || plan.HasMachineCare) && upgradeView != null)
        {
            var tabs = AutoPlayUi.GetField<ShopFacilityTabsView>(upgradeView, "tabs");
            var tab = tabs != null ? AutoPlayUi.GetField<Button>(tabs, "machineTab") : null;
            if (await PressAsync(tab, "店: 設備のタブ", ct)
                && await WaitUntilAsync(() => state.CurrentTomsShopPhase.Value == TomsShopGamePhase.MachineShop, 5f, ct))
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                var machineView = ViewOf<ShopMachineView>();
                var model = AutoPlayUi.Resolve<ShopMachineModel>();
                lastView = machineView;
                // 修理 → 強化 → 購入の順（壊れたものを先に直す）
                if (!string.IsNullOrEmpty(plan.RepairMachineId))
                {
                    var placed = model?.PlacementOf(plan.RepairMachineId);
                    bool wasBroken = placed != null && placed.Broken;
                    int days = placed?.DaysSinceService ?? -1;
                    if (await PressMachineButtonAsync(machineView, plan.RepairMachineId, "repairButton", "修理する", model, ct)
                        && placed != null && (wasBroken ? !placed.Broken : placed.DaysSinceService < days))
                        result.MachineRepaired = plan.RepairMachineId;
                    if (result.MachineRepaired == null) result.Notes.Add($"設備「{MachineName(model, plan.RepairMachineId)}」を修理できなかった");
                }
                if (!string.IsNullOrEmpty(plan.UpgradeMachineId))
                {
                    int lvBefore = model != null ? model.LevelOf(plan.UpgradeMachineId) : 0;
                    if (await PressMachineButtonAsync(machineView, plan.UpgradeMachineId, "upgradeButton", "強化する", model, ct)
                        && model != null && model.LevelOf(plan.UpgradeMachineId) > lvBefore)
                        result.MachineUpgraded = plan.UpgradeMachineId;
                    if (result.MachineUpgraded == null) result.Notes.Add($"設備「{MachineName(model, plan.UpgradeMachineId)}」を強化できなかった");
                }
                if (!string.IsNullOrEmpty(plan.MachineId))
                {
                    int before = model != null ? model.CountOf(plan.MachineId) : 0;
                    if (await PressMachineButtonAsync(machineView, plan.MachineId, "purchaseButton", "買って置く", model, ct)
                        && model != null && model.CountOf(plan.MachineId) > before)
                        result.MachinePlaced = plan.MachineId;
                    if (result.MachinePlaced == null) result.Notes.Add($"設備「{MachineName(model, plan.MachineId)}」を置けなかった");
                }
            }
            else result.Notes.Add("設備のタブが開かなかった");
        }
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        await CloseToShopAsync(lastView, "closeButton", "店の画面", ct);
    }

    private static string MachineName(ShopMachineModel model, string machineId) => model?.GetMachine(machineId)?.machineName ?? machineId;

    /// <summary>設備の画面でマシンを選び（ShopMachineSlotUI の selectButton）、詳細のボタン（ShopMachineView の buttonField）を押す。</summary>
    private async UniTask<bool> PressMachineButtonAsync(ShopMachineView machineView, string machineId, string buttonField, string verb,
        ShopMachineModel model, CancellationToken ct)
    {
        string name = MachineName(model, machineId);
        var slot = FindObjectsByType<ShopMachineSlotUI>(FindObjectsSortMode.None)
            .FirstOrDefault(s => s != null && s.MachineId == machineId && s.gameObject.activeInHierarchy);
        if (slot == null || !await PressFieldAsync(slot, "selectButton", $"設備: 「{name}」を選ぶ", ct)) return false;
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        if (!await PressFieldAsync(machineView, buttonField, $"設備: 「{name}」を{verb}", ct)) return false;
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        return true;
    }

    // ---------------- 情報屋の開発・取引所 ----------------

    private async UniTask BrokerInvestAsync(AutoPlayShopInvestPlan plan, AutoPlayShopInvestResult result, TomsModel toms, CancellationToken ct)
    {
        if (!await OpenShopScreenAsync("InfoButton", TomsShopGamePhase.Broker, "情報屋", result, ct)) return;
        var broker = ViewOf<InfoBrokerView>();
        if (broker == null) return;
        if (plan.UpgradeBroker)
        {
            int before = toms.InfoBrokerLevel.Value;
            if (await PressFieldAsync(broker, "developmentButton", "情報屋: 開発のタブ", ct))
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                if (await PressFieldAsync(broker, "levelUpButton", $"情報屋: 開発（Lv{before} → Lv{before + 1}）", ct))
                    await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            result.BrokerUpgraded = toms.InfoBrokerLevel.Value > before;
            result.BrokerLevelAfter = toms.InfoBrokerLevel.Value;
            if (!result.BrokerUpgraded) result.Notes.Add("情報屋の開発が押せなかった");
        }

        if (!string.IsNullOrEmpty(plan.FinanceProductId) && plan.FinanceUnits != 0)
            await TradeFinanceAsync(broker, plan.FinanceProductId, plan.FinanceUnits, result, ct);

        await CloseToShopAsync(broker, "closeButton", "情報屋", ct);
    }

    private async UniTask TradeFinanceAsync(InfoBrokerView broker, string productId, int units, AutoPlayShopInvestResult result, CancellationToken ct)
    {
        var portfolio = AutoPlayUi.Resolve<PortfolioModel>();
        var product = portfolio?.GetProduct(productId);
        if (product == null)
        {
            result.Notes.Add($"取引所に {productId} が無い");
            return;
        }
        if (!await PressFieldAsync(broker, "exchangeButton", "情報屋: 取引所のタブ", ct))
        {
            result.Notes.Add("取引所のタブが押せない");
            return;
        }
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 3f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        var row = FindObjectsByType<ItemShopSlot>(FindObjectsSortMode.None)
            .FirstOrDefault(s => s != null && s.itemId == productId && s.gameObject.activeInHierarchy);
        if (row == null || !await AutoPlayUi.PointerClickAsync(row.gameObject, AutoPlay.Settings.ButtonHoldSeconds, ct))
        {
            result.Notes.Add($"取引所で「{product.productName}」の行が見つからない");
            return;
        }
        AutoPlay.AddLog($"取引所: 「{product.productName}」を選ぶ");
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);

        var detail = broker.FinanceDetail;
        var slider = detail != null ? AutoPlayUi.GetField<Slider>(detail, "quantitySlider") : null;
        if (slider == null)
        {
            result.Notes.Add("取引所の数量を入れられない");
            return;
        }
        int want = Math.Abs(units);
        slider.value = Mathf.Min(want, slider.maxValue);   // 上限（買える口数・保有口数）は Presenter 側の maxQuantity で切られる
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        int qty = detail.CurrentQuantity;
        int heldBefore = portfolio.GetHeldUnits(productId);
        bool buy = units > 0;
        await PressFieldAsync(detail, buy ? "buyButton" : "sellButton",
            $"取引所: 「{product.productName}」を {qty} 口{(buy ? "買う" : "売る")}", ct);
        await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        int moved = portfolio.GetHeldUnits(productId) - heldBefore;
        if (moved != 0)
        {
            result.FinanceProductId = productId;
            result.FinanceUnits = moved;
        }
        else result.Notes.Add($"取引所で「{product.productName}」を{(buy ? "買え" : "売れ")}なかった");
    }

    // ---------------- 鍛冶屋の開発（お任せ購入の前） ----------------

    private async UniTask UpgradeSmithAsync(BlackSmithView view, TomsModel toms, AutoPlayShopInvestResult result, CancellationToken ct)
    {
        int before = toms.BlacksmithLevel.Value;
        try
        {
            if (await PressFieldAsync(view, "developButton", "鍛冶屋: 開発のタブ", ct))
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                if (await PressFieldAsync(view, "levelUpButton", $"鍛冶屋: 開発（Lv{before} → Lv{before + 1}）", ct))
                    await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 3f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            result.SmithUpgraded = toms.BlacksmithLevel.Value > before;
            result.SmithLevelAfter = toms.BlacksmithLevel.Value;
            if (!result.SmithUpgraded) result.Notes.Add("鍛冶屋の開発が押せなかった");
            AutoPlay.AddLog(result.SmithUpgraded ? $"鍛冶屋: Lv{before} → Lv{toms.BlacksmithLevel.Value}" : "鍛冶屋: 開発できなかった");
        }
        finally
        {
            // 買い物は武器のタブから（お任せ購入・個別購入のボタンがある画面へ戻す）
            var weaponTab = AutoPlayUi.GetField<Button>(view, "weaponButton");
            if (AutoPlayUi.IsClickable(weaponTab))
            {
                await PressAsync(weaponTab, "鍛冶屋: 武器タブへ戻る", ct);
                await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
        }
    }

    // =====================================================
    // 判断材料
    // =====================================================

    /// <summary>
    /// 次の配信が withinTurns 営業日以内（配信日の朝の寄り道中を含む）なら配信前の判断材料を作る。それ以外は null。
    /// GetTurnsUntilNextBattle は「次の行き先が配信」で 0。
    /// </summary>
    private AutoPlayStreamPrepContext BuildStreamPrepContext(int budget, int withinTurns)
    {
        var flow = AutoPlayUi.Resolve<GameFlowManager>();
        var repo = AutoPlayUi.Resolve<DungeonRepository>();
        if (flow == null || repo == null) return null;

        bool awaiting = flow.IsAwaitingStream.Value;
        var key = awaiting ? flow.PendingBattleDungeon : flow.GetNextBattleDungeon();
        if (!key.HasValue) return null;
        int turns = awaiting ? 0 : flow.GetTurnsUntilNextBattle();
        if (turns < 0 || turns >= Mathf.Max(1, withinTurns)) return null;
        return BuildStreamPrepContextFor(key.Value, turns, awaiting, budget);
    }

    /// <summary>指定ダンジョンを配信先とした判断材料（検証用に直接呼べるよう分けてある）。</summary>
    internal AutoPlayStreamPrepContext BuildStreamPrepContextFor(DungeonName dungeonKey, int turns, bool awaiting, int budget)
    {
        var flow = AutoPlayUi.Resolve<GameFlowManager>();
        var repo = AutoPlayUi.Resolve<DungeonRepository>();
        if (flow == null || repo == null) return null;
        DungeonName? key = dungeonKey;

        var dungeon = repo.GetById(key.Value);
        if (dungeon == null) return null;

        var intel = AutoPlayUi.Resolve<DungeonIntelModel>();
        var toms = AutoPlayUi.Resolve<TomsModel>();
        var items = AutoPlayUi.Resolve<ItemModel>();
        bool known = intel != null && intel.IsWeaknessKnown(key.Value);

        var ctx = new AutoPlayStreamPrepContext
        {
            Dungeon = key.Value,
            DungeonLabel = string.IsNullOrEmpty(dungeon.dungeonName) ? key.Value.ToString() : dungeon.dungeonName,
            TurnsUntilStream = turns,
            AwaitingStream = awaiting,
            WeaknessKnown = known,
            Weakness = known ? dungeon.requiredAttribute : (ItemTypeData.ItemAttribute?)null,
            Money = toms != null ? toms.PlayerMoney.Value : 0,
            Budget = budget,
            Random = AutoPlay.Random,
        };

        // 情報料（情報屋の価格表。購入済み・取り扱いなしは -1）
        var broker = AutoPlayUi.Resolve<InfoBrokerModel>();
        if (!known && !dungeon.isShowedInfo && broker != null && toms != null && broker.IsDungeonInfoSellable(key.Value))
            ctx.IntelCost = broker.GetDungeonInfoCost(toms.CurrentTurn.Value);

        if (known)
        {
            // 出現する魔物の属性（品出しのオート選択と同じく、情報を買ったダンジョンだけ使う）
            foreach (var enemy in dungeon.dungeonMonsters)
            {
                if (enemy != null && enemy.elementType != ElementType.None) ctx.EnemyElements.Add(enemy.elementType);
            }

            // 属性ごとに「有利な魔物の数 − 不利な魔物の数」で有利/不利を決める。弱点属性は常に有利の先頭
            ctx.EffectiveAttributes.Add(dungeon.requiredAttribute);
            foreach (ItemTypeData.ItemAttribute a in Enum.GetValues(typeof(ItemTypeData.ItemAttribute)))
            {
                int score = 0;
                foreach (var e in ctx.EnemyElements)
                {
                    if (ElementAttributeMapper.IsEffective(a, e)) score++;
                    else if (ElementAttributeMapper.IsWeak(a, e)) score--;
                }
                if (a == dungeon.requiredAttribute) continue;
                if (score > 0) ctx.EffectiveAttributes.Add(a);
                else if (score < 0) ctx.WeakAttributes.Add(a);
            }
        }

        if (known && items?.RuntimeItems != null && toms != null)
        {
            var effective = ctx.EffectiveAttributes;
            var match = items.RuntimeItems
                .Where(r => r != null && effective.Contains(r.ItemAttribute)
                            && r.RequiredLevel.Value <= toms.BlacksmithLevel.Value
                            && r.RemainToMax() > 0 && r.CurrentPrice.Value > 0)
                .OrderBy(r => r.ItemAttribute == dungeon.requiredAttribute ? 0 : 1) // 弱点属性 → ほかの有利属性
                .ThenByDescending(ItemModel.ExpectedRevenueOf)
                .ToList();
            var relics = AutoPlayUi.Resolve<RelicEffectResolver>();
            foreach (var r in match)
            {
                ctx.Matching.Add(new AutoPlayShopCandidate(
                    r.ItemId, r.ItemName, r.ItemType == ItemTypeData.ItemType.Weapon, r.ItemAttribute,
                    RelicPricing.GetBuyUnitPrice(r.CurrentPrice.Value, relics), r.RemainToMax(), ItemModel.ExpectedRevenueOf(r)));
            }
        }
        return ctx;
    }

    public static string ElementLabel(ElementType e) => e switch
    {
        ElementType.Fire => "火",
        ElementType.Water => "水",
        ElementType.Wood => "木",
        ElementType.Light => "光",
        ElementType.Dark => "闇",
        _ => "無",
    };

    public static string AttributeLabel(ItemTypeData.ItemAttribute attr) => attr switch
    {
        ItemTypeData.ItemAttribute.Fire => "火",
        ItemTypeData.ItemAttribute.Water => "水",
        ItemTypeData.ItemAttribute.Wood => "木",
        ItemTypeData.ItemAttribute.Light => "光",
        ItemTypeData.ItemAttribute.Dark => "闇",
        _ => attr.ToString(),
    };

    private static string StrategyLabel(AutoBuyStrategy s) => s switch
    {
        AutoBuyStrategy.DungeonFocus => "次ダンジョン特化",
        AutoBuyStrategy.Bargain => "割安買い",
        AutoBuyStrategy.Dividend => "配当重視",
        _ => "おすすめ順",
    };
}
#endif
