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

    private void InstallProcurementHooks()
    {
        DebugAutoPlayer.ExternalBlacksmithBuyer = OnBlacksmithBuy;
        DebugAutoPlayer.ExternalBeforeProcurement = OnBeforeProcurement;
    }

    private static void UninstallProcurementHooks()
    {
        DebugAutoPlayer.ExternalBlacksmithBuyer = null;
        DebugAutoPlayer.ExternalBeforeProcurement = null;
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
        if (_intelCheckedKey == key) return false;
        _intelCheckedKey = key;

        var prep = BuildStreamPrepContext(0, AutoPlay.Settings.IntelPrepTurns);
        if (prep == null || prep.WeaknessKnown) return false;
        if (prep.IntelCost <= 0)
        {
            AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の弱点は不明（情報屋で扱っていない）");
            return false;
        }
        if (!AutoPlay.Strategy.ShouldBuyDungeonIntel(prep))
        {
            AutoPlay.AddLog($"配信前: {prep.DungeonLabel} の情報（{prep.IntelCost:N0}G）は買わない（所持金 {prep.Money:N0}G）");
            return false;
        }

        _intelRunning = true;
        RunIntelPurchaseAsync(prep, _cts.Token).Forget();
        return true;
    }

    private async UniTaskVoid RunIntelPurchaseAsync(AutoPlayStreamPrepContext prep, CancellationToken ct)
    {
        DebugAutoPlayer.ExternalBusy = true;
        try
        {
            await BuyDungeonIntelAsync(prep, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogException(e);
            AutoPlay.AddLog($"情報屋の操作で例外: {e.Message}");
        }
        finally
        {
            // 情報屋を開いたまま止まらないよう、店ホームへ戻っていなければ戻す
            var state = AutoPlayUi.Resolve<StateManager>();
            if (state != null && state.CurrentPhase.Value == GamePhase.TomsShop
                && state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Broker)
                state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
            DebugAutoPlayer.ExternalBusy = false;
            _intelRunning = false;
        }
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
        var costs = broker?.GetDungeonInfoCosts();
        if (!known && !dungeon.isShowedInfo && costs != null && costs.TryGetValue(key.Value, out var arr) && arr != null && arr.Length > 0)
        {
            int battleCount = flow.BattleCount.Value;
            ctx.IntelCost = arr[Mathf.Clamp(battleCount, 0, arr.Length - 1)];
        }

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
        ItemTypeData.ItemAttribute.Earth => "土",
        ItemTypeData.ItemAttribute.Wind => "風",
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
