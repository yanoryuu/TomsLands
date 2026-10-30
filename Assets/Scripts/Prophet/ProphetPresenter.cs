using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 占い師の画面「上げる枠・下げる枠」（Docs/Prophet_Intervention.md）の Presenter。画面の部分だけを持つ。
/// 仕組み（力・枠・効果の日数・代金・保存・需要への効き方）は IProphetIntervention が持ち、ここはそれを読んで描き、
/// 「この品をこの枠へ」を TryPlace に渡して、答え（ProphetPlaceResult）を占い師のセリフにするだけ。
/// 品の一覧は ItemModel（解放済みの全品）から、新聞の向きはプレイヤーのスクラップの予想メモから取る。
/// 以前の「3 日先を占う」と 3 件だけの一覧（荒れ相場・高値・おすすめ）は廃止した（仕組みは ProphetInterventionModel）。
/// </summary>
public class ProphetPresenter : IDisposable, IStartable
{
    private readonly ProphetView prophetView;
    private readonly ItemModel itemModel;
    private readonly GameFlowManager gameFlowManager;
    private readonly StateManager stateManager;
    private readonly TomsModel tomsModel;
    private readonly IProphetIntervention intervention;
    private readonly ScrapbookModel scrapbook;
    private readonly NewsModel newsModel;
    private readonly NewspaperSubscriptionModel subscriptions;
    private readonly CompositeDisposable disposables = new();
    private string currentDialogue = string.Empty;
    private int characterTalkIndex;
    private int lastPower = -1;

    public ProphetPresenter(
        ProphetView prophetView,
        ItemModel itemModel,
        GameFlowManager gameFlowManager,
        StateManager stateManager,
        TomsModel tomsModel,
        IProphetIntervention intervention,
        ScrapbookModel scrapbook,
        NewsModel newsModel,
        NewspaperSubscriptionModel subscriptions)
    {
        this.prophetView = prophetView;
        this.itemModel = itemModel;
        this.gameFlowManager = gameFlowManager;
        this.stateManager = stateManager;
        this.tomsModel = tomsModel;
        this.intervention = intervention;
        this.scrapbook = scrapbook;
        this.newsModel = newsModel;
        this.subscriptions = subscriptions;

        stateManager.RegisterOnEnter(TomsShopGamePhase.Prophet, Entry);
    }

    public void Start()
    {
        prophetView.PreviewProvider = Preview;

        prophetView.OnCloseClicked
            .Subscribe(_ => stateManager.ChangeTomsShopPhase(TomsShopGamePhase.Shop))
            .AddTo(disposables);

        prophetView.OnCharacterClicked
            .Subscribe(_ => ShowCharacterTalk())
            .AddTo(disposables);

        prophetView.OnPlaceRequested
            .Subscribe(r => Place(r.itemId, r.direction))
            .AddTo(disposables);

        prophetView.OnInUseItemPressed
            .Subscribe(_ => Say(Line("item_in_use")))
            .AddTo(disposables);

        prophetView.OnSlotPressedWithoutItem
            .Subscribe(d => Say(intervention.GetSlot(d).HasValue ? Line("place_slot_in_use") : Line("slot_pick_first")))
            .AddTo(disposables);

        prophetView.OnHint
            .Subscribe(line => prophetView.ShowDialogue(string.IsNullOrEmpty(line) ? currentDialogue : line))
            .AddTo(disposables);

        // 仕組みの状態が変わったら（入れた・日が進んだ・配信で力が戻った）開いていれば描き直す
        intervention.Changed
            .Subscribe(_ => { if (prophetView.IsOpen) Refresh(animate: false); })
            .AddTo(disposables);
    }

    private void Entry()
    {
        characterTalkIndex = 0;
        prophetView.ClearSelection();
        lastPower = -1;
        Refresh(animate: true);
        currentDialogue = GreetingLine();
        prophetView.ShowDialogue(currentDialogue);
    }

    // =====================================================================
    // 表示
    // =====================================================================

    private int Turn => gameFlowManager != null ? gameFlowManager.CurrentTurn.Value : tomsModel.CurrentTurn.Value;

    private void Refresh(bool animate)
    {
        int power = intervention.Power;
        prophetView.SetPower(power, intervention.MaxPower, animateUse: lastPower >= 0 && power < lastPower);
        lastPower = power;
        prophetView.SetSlots(SlotInfo(ProphetDirection.Up), SlotInfo(ProphetDirection.Down), intervention.EffectTurns, power <= 0);
        prophetView.SetItems(BuildItems(), animate);
    }

    private ProphetSlotInfo? SlotInfo(ProphetDirection d)
    {
        var s = intervention.GetSlot(d);
        if (!s.HasValue) return null;
        var r = itemModel.GetRuntimeItem(s.Value.ItemId);
        return new ProphetSlotInfo
        {
            ItemId = s.Value.ItemId,
            Name = r != null ? r.ItemName : s.Value.ItemId,
            Icon = r?.ItemIcon,
            Price = r != null ? r.CurrentPrice.Value : 0,
            Stock = r != null ? r.Stock.Value : 0,
            TurnsLeft = s.Value.TurnsLeft,
            TotalTurns = s.Value.TotalTurns > 0 ? s.Value.TotalTurns : intervention.EffectTurns,
        };
    }

    /// <summary>解放済みの全品（並び・絞り込みは View）。</summary>
    private List<ProphetItemInfo> BuildItems()
    {
        int level = tomsModel.BlacksmithLevel.Value;
        var up = intervention.GetSlot(ProphetDirection.Up);
        var down = intervention.GetSlot(ProphetDirection.Down);
        var memos = ActiveMemos();
        return itemModel.RuntimeItems
            .Where(r => r != null && r.RequiredLevel.Value <= level)
            .Select(r =>
            {
                int prev = r.PreviousPrice;
                int inSlot = up.HasValue && up.Value.ItemId == r.ItemId ? 1
                    : down.HasValue && down.Value.ItemId == r.ItemId ? -1 : 0;
                return new ProphetItemInfo
                {
                    ItemId = r.ItemId, Name = r.ItemName, Icon = r.ItemIcon, Type = r.ItemType,
                    Price = r.CurrentPrice.Value,
                    Change1d = prev > 0 ? (r.CurrentPrice.Value - prev) / (float)prev : 0f,
                    Stock = r.Stock.Value,
                    NewsDirection = memos.Any(m => ScrapPrediction.Matches(m, r)) ? 1 : 0,
                    InSlot = inSlot,
                };
            })
            .ToList();
    }

    /// <summary>
    /// まだ答えの出ていないスクラップに書いた予想メモ（「この品が上がる」）。品のカードの「新聞 ▲」に使う。
    /// 記事から品へのリンクはゲームが作らない（News_Spec §9.3）ので、プレイヤー自身の予想だけを出す。
    /// </summary>
    private List<string> ActiveMemos()
    {
        var list = new List<string>();
        if (scrapbook == null || newsModel == null) return list;
        try
        {
            int seed = tomsModel.FlowSeed;
            newsModel.Build(seed);
            NewspaperSaveStore.EnsureLoaded(seed, subscriptions, scrapbook, newsModel);
            int turn = Turn;
            foreach (var s in scrapbook.Pinned)
            {
                if (string.IsNullOrEmpty(s.prediction)) continue;
                var st = ScrapbookModel.StateOf(s, newsModel.FindEntry(s.entryKey), turn);
                if (st == ScrapState.Pending || st == ScrapState.Unconfirmed) list.Add(s.prediction);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProphetPresenter] 予想メモを読めなかった: {e.Message}");
        }
        return list;
    }

    // =====================================================================
    // 入れる
    // =====================================================================

    /// <summary>枠の上に持ってきたときの確かめ（入れられるなら「需要 ▲ 3 ターン／力を 1 つ使う」、だめなら理由）。</summary>
    private (string text, bool ok) Preview(string itemId, ProphetDirection d)
    {
        var r = intervention.CanPlace(itemId, d);
        if (r != ProphetPlaceResult.Ok) return (ShortReason(r), false);
        string arrow = d == ProphetDirection.Up ? "▲" : "▼";
        string cost = intervention.Cost > 0 ? $"{intervention.Cost:N0}G・" : string.Empty;
        return ($"需要 {arrow} {intervention.EffectTurns} ターン／力を 1 つ使う\n<size=80%>{cost}離すと入る</size>", true);
    }

    private void Place(string itemId, ProphetDirection d)
    {
        var r = intervention.TryPlace(itemId, d);
        if (r != ProphetPlaceResult.Ok)
        {
            prophetView.PlayRejected(d);
            Say(ReasonLine(r));
            return;
        }
        var sound = SoundManager.Instance;
        if (sound != null) sound.PlaySE("営業/SE_仕入れ完了");
        Refresh(animate: false); // 仕組みが Changed を流さなくても描き直す
        prophetView.PlayPlaced(d);
        var item = itemModel.GetRuntimeItem(itemId);
        currentDialogue = Line(d == ProphetDirection.Up ? "placed_up" : "placed_down").Replace("{item}", item != null ? item.ItemName : itemId);
        if (intervention.Power <= 0) currentDialogue += "\n" + Line("power_empty");
        prophetView.ShowDialogue(currentDialogue);
    }

    // =====================================================================
    // セリフ
    // =====================================================================

    /// <summary>開いたときの一言: 満タンなら「あふれる前に使おう」、0 なら「次の配信まで休む」。</summary>
    private string GreetingLine()
    {
        int p = intervention.Power;
        if (p >= intervention.MaxPower) return Line("power_full");
        if (p <= 0) return Line("power_empty");
        return Line("default");
    }

    private string ReasonLine(ProphetPlaceResult r) => r switch
    {
        ProphetPlaceResult.NoPower => Line("power_empty"),
        ProphetPlaceResult.NotEnoughMoney => Line("place_no_money"),
        ProphetPlaceResult.SlotInUse => Line("place_slot_in_use"),
        ProphetPlaceResult.SameItemInOtherSlot => Line("place_same_item"),
        ProphetPlaceResult.NotUnlocked => Line("place_not_unlocked"),
        _ => Line("default"),
    };

    private static string ShortReason(ProphetPlaceResult r) => r switch
    {
        ProphetPlaceResult.NoPower => "力が残っていない\n<size=80%>配信 1 回で 1 つ回復</size>",
        ProphetPlaceResult.NotEnoughMoney => "代金が足りない",
        ProphetPlaceResult.SlotInUse => "効果が切れるまで出し入れ不可",
        ProphetPlaceResult.SameItemInOtherSlot => "同じ品は両方の枠に入れられない",
        ProphetPlaceResult.NotUnlocked => "まだ扱えない品",
        _ => "入れられない",
    };

    /// <summary>一時的なセリフ（ホバーを外すと元の currentDialogue に戻る）。</summary>
    private void Say(string line) => prophetView.ShowDialogue(line);

    private void ShowCharacterTalk()
    {
        characterTalkIndex = (characterTalkIndex % 3) + 1;
        currentDialogue = Line($"character_talk_{characterTalkIndex}");
        prophetView.ShowDialogue(currentDialogue);
    }

    /// <summary>台詞 CSV の 1 行。{turns}（効く日数）・{cost}（代金）・{max}（力の最大）を埋める。行が無ければ default。</summary>
    private string Line(string id)
    {
        var s = ProphetDialogueLoader.Get(id);
        if (string.IsNullOrEmpty(s)) s = ProphetDialogueLoader.GetDefault();
        return s.Replace("{turns}", intervention.EffectTurns.ToString())
            .Replace("{cost}", intervention.Cost.ToString("N0"))
            .Replace("{max}", intervention.MaxPower.ToString());
    }

    public void Dispose() => disposables.Dispose();
}
