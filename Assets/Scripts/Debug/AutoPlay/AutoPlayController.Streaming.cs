#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 配信中（FightScene の配信パネル表示中）の自動操作。仕様: Docs/AutoPlay_Usage.md「配信中の操作」
///
/// プレイヤーが配信中にできる操作を、StreamActionInterval ごとに 1 つずつ実行する（判断は IAutoPlayStrategy）:
///  - 在庫切れの補充ポップアップ: 補充数を決めて「購入」/「やめる」（出ている間は戦闘が止まるので最優先）
///  - 在庫 → 売り場のドラッグ入れ替え（売り場の空き・売り切れを埋める）
///  - 介入カード: 勇者側（回復/スキル/必殺）・ダンジョン側（罠/呪い/増援/ボス強化）。必要ならカードをめくる。
///    スキル/必殺の前には狙う敵をタップする
///  - 売り場の商品の値動きグラフを開いて閉じる（見た目用）
/// お金は既存の Presenter が「利用可能残高」で管理し、戦闘終了時に BattleSceneStarter が一括精算する。
/// ここはボタン・スロットを押すだけで、精算・配信終了処理には触れない。
/// </summary>
public sealed partial class AutoPlayController
{
    private const float FlipWaitSeconds = 0.45f;
    private const float DragSeconds = 0.5f;

    private bool _streamStarted;
    private int _streamStartBalance;
    private int _streamSpent;
    private int _streamActions;
    private float _nextStreamActionAt;
    private float _restockSeenAt = -1f;

    private InterventionCardView _card;
    private BattleControlView _controlView;
    private InterventionPresenter _interventions;
    private BattleSequencer _sequencer;
    private StreamingSalesController _sales;

    // BuildStreamContext で集めた操作対象（ctx の番号と対応）
    private readonly List<CharacterPresenter> _streamEnemies = new();
    private List<ItemSlotView> _sellSlotViews;
    private List<ItemSlotView> _inventorySlotViews;

    private void ResetStreamState()
    {
        // 結果パネルを見る前にシーンが変わった場合も、配信が終わったことを人格に伝える
        if (_streamStarted) (AutoPlay.Strategy as IAutoPlayPrefetchStrategy)?.OnStreamEnded();
        _streamStarted = false;
        _streamSpent = 0;
        _streamActions = 0;
        _nextStreamActionAt = 0f;
        _restockSeenAt = -1f;
        _card = null;
        _controlView = null;
        _interventions = null;
        _sequencer = null;
        _sales = null;
        _streamEnemies.Clear();
        _sellSlotViews = null;
        _inventorySlotViews = null;
    }

    /// <summary>配信中の操作を 1 つ行ったら true。</summary>
    private async UniTask<bool> StepStreamingAsync(CancellationToken ct)
    {
        var s = AutoPlay.Settings;
        if (!s.StreamActionsEnabled) return false;

        if (_battlePanels == null) _battlePanels = FindFirstObjectByType<BattlePanelManager>();
        bool streaming = AutoPlayUi.IsActive(_battlePanels, "streamingPanel")
                         && !AutoPlayUi.IsActive(_battlePanels, "streamingResultPanel");
        if (!streaming)
        {
            if (_streamStarted)
            {
                AutoPlay.AddLog($"配信: 自動操作を終了（操作 {_streamActions} 回 / 介入 {_streamSpent:N0}G）");
                (AutoPlay.Strategy as IAutoPlayPrefetchStrategy)?.OnStreamEnded();
            }
            _streamStarted = false;
            return false;
        }

        ResolveStreamRefs();

        // 1) 在庫切れの補充ポップアップ（表示中は戦闘が一時停止している）
        if (await HandleRestockPopupAsync(ct)) return true;

        if (Now < _nextStreamActionAt) return false;
        _nextStreamActionAt = Now + ScaledWait(Mathf.Max(s.StreamActionInterval, s.ActionWait));

        if (IsBattlePaused()) return false;

        bool firstStep = !_streamStarted;
        var ctx = BuildStreamContext();
        var strategy = AutoPlay.Strategy;
        // 人格プレイ: 配信の判断を裏でモデルに聞く（戦闘は止めない。答えが来るまで戦略は介入を控える）
        if (firstStep && strategy is IAutoPlayPrefetchStrategy prefetch) prefetch.OnStreamStarted(ctx);

        // 2) 売り場の空き・売り切れを在庫で埋める（ドラッグ）
        if (strategy.ChooseShelfSwap(ctx, out int inventoryIndex, out int sellIndex)
            && await SwapShelfAsync(inventoryIndex, sellIndex, ct))
            return CountStreamAction();

        // 3) 介入（スパチャ指示）
        var intervention = strategy.ChooseStreamIntervention(ctx);
        if (intervention.HasValue && await InterveneAsync(ctx, intervention, ct))
            return CountStreamAction();

        // 4) 値動きグラフを確認
        if (strategy.ChoosePriceGraphPeek(ctx, out int peekIndex) && await PeekPriceGraphAsync(peekIndex, ct))
            return CountStreamAction();

        // 5) 介入カードが裏のままなら表（チャット欄）へ戻す
        if (_card != null && _card.CurrentFace == InterventionFace.Dungeon)
        {
            var tab = AutoPlayUi.GetField<Button>(_card, "tabButton");
            if (await PressAsync(tab, "配信: 介入カードを表に戻す", ct)) return true;
        }
        return false;
    }

    private bool CountStreamAction()
    {
        _streamActions++;
        return true;
    }

    private void ResolveStreamRefs()
    {
        if (_card == null) _card = FindFirstObjectByType<InterventionCardView>();
        if (_controlView == null) _controlView = FindFirstObjectByType<BattleControlView>();
        _interventions ??= AutoPlayUi.Resolve<InterventionPresenter>();
        if (_sequencer == null) _sequencer = AutoPlayUi.Resolve<BattleSequencer>();
        if (_sales == null) _sales = AutoPlayUi.Resolve<StreamingSalesController>();
    }

    private bool IsBattlePaused()
    {
        var pc = _sequencer != null ? _sequencer.PauseController : null;
        return pc != null && pc.IsPaused;
    }

    // =====================================================
    // 判断材料
    // =====================================================

    private AutoPlayStreamContext BuildStreamContext()
    {
        var s = AutoPlay.Settings;
        var toms = AutoPlayUi.Resolve<TomsModel>();
        int balance = _interventions != null ? _interventions.AvailableBalance : toms != null ? toms.PlayerMoney.Value : 0;

        if (!_streamStarted)
        {
            _streamStarted = true;
            _streamStartBalance = Mathf.Max(0, balance);
            _streamSpent = 0;
            _streamActions = 0;
            int cap = Mathf.FloorToInt(_streamStartBalance * Mathf.Clamp01(s.StreamSpendRatio));
            AutoPlay.AddLog($"配信: 自動操作を開始（残高 {_streamStartBalance:N0}G / 介入に使う上限 {cap:N0}G）");
        }

        var ctx = new AutoPlayStreamContext
        {
            AvailableBalance = balance,
            StartBalance = _streamStartBalance,
            SpentThisStream = _streamSpent,
            SpendBudgetLeft = Mathf.Max(0, Mathf.FloorToInt(_streamStartBalance * Mathf.Clamp01(s.StreamSpendRatio)) - _streamSpent),
            ActionCount = _streamActions,
            Random = AutoPlay.Random,
        };

        // 勇者・敵
        _streamEnemies.Clear();
        var battle = _sequencer != null ? _sequencer.Context : null;
        if (battle != null)
        {
            var hero = battle.HeroPresenter?.GetModel();
            if (hero != null && hero.MaxHp > 0)
                ctx.HeroHp01 = Mathf.Clamp01((float)hero.CurrentHp.CurrentValue / hero.MaxHp);

            foreach (var p in battle.EnemyPresenters)
            {
                var m = p?.GetModel();
                if (m == null || m.IsDead) continue;
                float hp01 = m.MaxHp > 0 ? Mathf.Clamp01((float)m.CurrentHp.CurrentValue / m.MaxHp) : 0f;
                ctx.Enemies.Add(new AutoPlayEnemyInfo(_streamEnemies.Count, hp01, m.IsBoss));
                _streamEnemies.Add(p);
            }
        }

        // 介入ボタン
        if (_interventions != null)
        {
            foreach (HeroInterventionType t in Enum.GetValues(typeof(HeroInterventionType)))
                ctx.HeroButtons[t] = _interventions.GetState(t);
            foreach (DungeonInterventionType t in Enum.GetValues(typeof(DungeonInterventionType)))
                ctx.DungeonButtons[t] = _interventions.GetState(t);
        }

        // 棚（売り場 / 在庫）
        var salesView = AutoPlayUi.GetField<StreamingSalesView>(_sales, "view");
        _sellSlotViews = AutoPlayUi.GetField<List<ItemSlotView>>(salesView, "itemSlots");
        _inventorySlotViews = AutoPlayUi.GetField<List<ItemSlotView>>(_sales, "inventorySlotViews");
        FillShelf(_sellSlotViews, ctx.SellSlots);
        FillShelf(_inventorySlotViews, ctx.InventorySlots);
        return ctx;
    }

    private static void FillShelf(List<ItemSlotView> views, List<AutoPlayShelfSlot> into)
    {
        if (views == null) return;
        for (int i = 0; i < views.Count; i++)
        {
            var v = views[i];
            if (v == null || !v.gameObject.activeInHierarchy) continue;
            var item = v.CurrentItem;
            into.Add(item == null
                ? new AutoPlayShelfSlot(i, null, null, 0, 0, 0f)
                : new AutoPlayShelfSlot(i, item.ItemId, item.ItemName, item.Stock.Value, item.CurrentPrice.Value, item.Demand.Value));
        }
    }

    // =====================================================
    // 操作
    // =====================================================

    /// <summary>在庫切れの補充ポップアップ（RestockQuantityPopup）に答える。</summary>
    private async UniTask<bool> HandleRestockPopupAsync(CancellationToken ct)
    {
        var popup = AutoPlayUi.GetField<RestockQuantityPopup>(_controlView, "restockQuantityPopup");
        if (!AutoPlayUi.IsActive(popup, "panel"))
        {
            _restockSeenAt = -1f;
            return false;
        }

        // 出た瞬間に押すと見えないので、少し見せてから答える（ポップアップ中は戦闘が止まっているので実時間で待つ）
        if (_restockSeenAt < 0f) _restockSeenAt = Now;
        if (Now - _restockSeenAt < Mathf.Max(0.3f, AutoPlay.Settings.ActionWait))
        {
            AutoPlay.Status = "配信: 補充ポップアップを確認中";
            return false;
        }

        int max = AutoPlayUi.GetFieldValue(popup, "_maxQuantity") is int m ? m : 0;
        int def = AutoPlayUi.GetFieldValue(popup, "_quantity") is int q0 ? q0 : 0;
        string title = AutoPlayUi.GetField<TextMeshProUGUI>(popup, "titleText")?.text ?? "補充";

        var ctx = BuildStreamContext();
        int quantity = Mathf.Clamp(AutoPlay.Strategy.ChooseRestockQuantity(ctx, title, max, def), 0, Mathf.Max(0, max));

        var detail = AutoPlayUi.GetField<ItemDetailPanel>(popup, "detailPanel");
        var purchase = AutoPlayUi.GetField<Button>(detail, "purchaseButton");
        if (quantity <= 0 || !AutoPlayUi.IsClickable(purchase))
        {
            _restockSeenAt = -1f;
            return await PressAsync(AutoPlayUi.GetField<Button>(popup, "cancelButton"), $"配信: {title} → 補充しない", ct);
        }

        if (quantity != def)
        {
            var slider = AutoPlayUi.GetField<Slider>(detail, "quantitySlider");
            if (slider != null)
            {
                slider.value = quantity; // onValueChanged → ItemDetailPanel → RestockQuantityPopup の数量に反映
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            }
            else
            {
                quantity = def; // スライダーが無い構成では既定数のまま
            }
        }

        _restockSeenAt = -1f;
        bool pressed = await PressAsync(purchase, $"配信: {title} → {quantity}個 補充", ct);
        return pressed && CountStreamAction();
    }

    /// <summary>在庫スロットを売り場スロットへドラッグする（入れ替え）。</summary>
    private async UniTask<bool> SwapShelfAsync(int inventoryIndex, int sellIndex, CancellationToken ct)
    {
        if (_inventorySlotViews == null || _sellSlotViews == null) return false;
        if (inventoryIndex < 0 || inventoryIndex >= _inventorySlotViews.Count) return false;
        if (sellIndex < 0 || sellIndex >= _sellSlotViews.Count) return false;

        var from = _inventorySlotViews[inventoryIndex];
        var to = _sellSlotViews[sellIndex];
        if (from == null || to == null || from.CurrentItem == null) return false;

        string name = from.CurrentItem.ItemName;
        AutoPlay.Status = $"配信: 「{name}」を売り場へ";
        bool done = await AutoPlayUi.DragAsync(from.gameObject, to.gameObject, DragSeconds, ct);
        if (done) AutoPlay.AddLog($"配信: 在庫の「{name}」を売り場{sellIndex + 1}へ入れ替え");
        return done;
    }

    /// <summary>介入ボタンを押す（必要ならカードをめくる・狙う敵をタップする）。</summary>
    private async UniTask<bool> InterveneAsync(AutoPlayStreamContext ctx, AutoPlayStreamIntervention iv, CancellationToken ct)
    {
        if (_card == null || _interventions == null) return false;

        var face = iv.IsHeroSide ? InterventionFace.Hero : InterventionFace.Dungeon;
        if (_card.CurrentFace != face)
        {
            var tab = AutoPlayUi.GetField<Button>(_card, "tabButton");
            if (!await PressAsync(tab, "配信: 介入カードをめくる", ct)) return false;
            await UniTask.Delay(TimeSpan.FromSeconds(FlipWaitSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);
            if (_card == null || _card.CurrentFace != face) return true; // めくりだけで 1 操作
        }

        // スキル・必殺は狙う敵を先にタップ（敵スプライトのクリックと同じ通知）
        if (iv.IsHeroSide && iv.Hero != HeroInterventionType.Heal)
        {
            int target = AutoPlay.Strategy.ChooseEnemyTarget(ctx, iv);
            if (target >= 0 && target < _streamEnemies.Count)
            {
                var view = _streamEnemies[target]?.GetView();
                if (view != null)
                {
                    view.OnClicked.OnNext(Unit.Default);
                    AutoPlay.AddLog($"配信: 敵をタップして狙う（{(ctx.Enemies[target].IsBoss ? "ボス" : $"HP{ctx.Enemies[target].Hp01:P0}")}）");
                    await UniTask.Delay(TimeSpan.FromSeconds(0.25f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
                }
            }
        }

        string field = iv.IsHeroSide
            ? iv.Hero switch
            {
                HeroInterventionType.Heal => "healButton",
                HeroInterventionType.Skill => "skillButton",
                _ => "specialButton",
            }
            : iv.Dungeon switch
            {
                DungeonInterventionType.Trap => "trapButton",
                DungeonInterventionType.Curse => "curseButton",
                DungeonInterventionType.Reinforce => "reinforceButton",
                _ => "bossBuffButton",
            };
        var widget = AutoPlayUi.GetField<InterventionButtonWidget>(_card, field);
        var button = widget != null ? widget.Button : null;
        if (!AutoPlayUi.IsClickable(button)) return false;

        int before = _interventions.TotalSpent;
        string label = InterventionLabel(iv);
        bool pressed = await PressAsync(button, $"配信: 介入「{label}」", ct);
        if (!pressed) return false;

        int spent = Mathf.Max(0, _interventions.TotalSpent - before);
        _streamSpent += spent;
        AutoPlay.AddLog(spent > 0
            ? $"配信: 介入「{label}」 {spent:N0}G（残高 {_interventions.AvailableBalance:N0}G）"
            : $"配信: 介入「{label}」を押したが受け付けられなかった（クールダウン・指示待ちなど）");
        return true;
    }

    private static string InterventionLabel(AutoPlayStreamIntervention iv) => iv.IsHeroSide
        ? iv.Hero switch
        {
            HeroInterventionType.Heal => "回復（緑スパ）",
            HeroInterventionType.Skill => "スキル（青スパ）",
            _ => "必殺技（赤スパ）",
        }
        : iv.Dungeon switch
        {
            DungeonInterventionType.Trap => "罠",
            DungeonInterventionType.Curse => "呪い",
            DungeonInterventionType.Reinforce => "増援",
            _ => "ボス強化",
        };

    /// <summary>売り場の商品をクリックして値動きグラフを開き、少し見せて閉じる。</summary>
    private async UniTask<bool> PeekPriceGraphAsync(int sellIndex, CancellationToken ct)
    {
        if (_sellSlotViews == null || sellIndex < 0 || sellIndex >= _sellSlotViews.Count) return false;
        var slot = _sellSlotViews[sellIndex];
        // 在庫切れのスロットをクリックすると補充ポップアップになるので避ける
        if (slot == null || slot.CurrentItem == null || slot.CurrentItem.Stock.Value <= 0) return false;

        var graph = AutoPlayUi.GetField<ItemPriceGraphView>(_sales, "priceGraphView");
        if (graph == null) return false;

        string name = slot.CurrentItem.ItemName;
        AutoPlay.Status = $"配信: 「{name}」の値動きを確認";
        if (!await AutoPlayUi.PointerClickAsync(slot.gameObject, AutoPlay.Settings.ButtonHoldSeconds, ct)) return false;
        AutoPlay.AddLog($"配信: 「{name}」の値動きを確認");

        await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(0.2f, AutoPlay.Settings.PriceGraphPeekSeconds)),
            DelayType.UnscaledDeltaTime, cancellationToken: ct);

        var close = AutoPlayUi.GetField<Button>(graph, "closeButton");
        if (AutoPlayUi.IsClickable(close))
            await AutoPlayUi.PressAsync(close, AutoPlay.Settings.ButtonHoldSeconds, ct);
        return true;
    }
}
#endif
