#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 占い師の「上げる枠・下げる枠」の UI 操作（Docs/Prophet_Intervention.md「AI が押すための名前」）。
/// 陳列フェーズの頭（OnBeforeDisplay）で ProphetAutoRule に聞き、入れるなら
/// 店の ProphetButton → ProphetView.ItemCardButton(itemId) → 枠のボタン（UpSlotButton / DownSlotButton）→ closeButton を実際に押す。
/// どこで失敗しても（ボタンが押せない・画面が開かない・時間切れ・例外）画面を閉じて店ホームへ戻す（マラソンが止まらないように）。
/// 判断と結果は AutoPlay のログに書き、戦略が IAutoPlayProphetUser なら（人格プレイ）そちらにも知らせる。
/// </summary>
public sealed partial class AutoPlayController
{
    /// <summary>新聞を何日前の号まで読み返すか（効き終わっていない「上がる」報道を拾う）。</summary>
    private const int ProphetNewsLookbackDays = 5;
    private const float ProphetWaitSeconds = 5f;

    /// <summary>今日、占い師の枠に入れるかを決める（入れないなら None。理由はログへ）。</summary>
    private ProphetAutoPick PlanProphet()
    {
        try
        {
            var strategy = AutoPlay.Strategy;
            var ctx = CreateContext();
            if (strategy == null || !strategy.ShouldDisplay(ctx)) return ProphetAutoPick.None("店を回さない戦略");
            var intervention = AutoPlayUi.Resolve<IProphetIntervention>();
            var toms = AutoPlayUi.Resolve<TomsModel>();
            var items = AutoPlayUi.Resolve<ItemModel>();
            if (intervention == null || toms == null || items == null) return ProphetAutoPick.None("占い師の仕組みが見つからない");
            // 満タンでない日は品の一覧を作るまでもない（ログも出さない）
            if (intervention.Power < intervention.MaxPower) return ProphetAutoPick.None("力が満タンでない");

            var pick = ProphetAutoRule.Choose(intervention, toms.PlayerMoney.Value, BuildProphetCandidates(items, toms));
            if (!pick.IsNone && strategy is IAutoPlayProphetUser user && !user.ShouldUseProphet(ctx, pick))
                pick = ProphetAutoPick.None("戦略が見送った");
            AutoPlay.AddLog(pick.IsNone
                ? $"占い師: 使わない（{pick.Reason}）"
                : $"占い師: {DirectionLabel(pick.Direction)}枠に「{items.GetRuntimeItem(pick.ItemId)?.ItemName ?? pick.ItemId}」を入れる（{pick.Reason}）");
            if (pick.IsNone && strategy is IAutoPlayProphetUser u) u.OnProphetDone(ctx, pick, null, pick.Reason);
            return pick;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return ProphetAutoPick.None($"判断で例外: {e.Message}");
        }
    }

    /// <summary>解放済みの品を ProphetAutoRule の候補にする（新聞▲＝紙面で読める上がる報道・予想メモ、仕入れたい品＝分かっている次の配信の弱点の武具）。</summary>
    private List<ProphetAutoCandidate> BuildProphetCandidates(ItemModel items, TomsModel toms)
    {
        var upReports = ReadUpReports(toms);
        var memos = ActiveScrapMemos(toms);
        var prep = BuildStreamPrepContext(0, int.MaxValue);
        ItemTypeData.ItemAttribute? weakness = prep != null && prep.WeaknessKnown ? prep.Weakness : null;
        int turn = AutoPlay.CurrentTurn;

        var list = new List<ProphetAutoCandidate>();
        foreach (var r in items.RuntimeItems)
        {
            if (r == null || r.RequiredLevel.Value > toms.BlacksmithLevel.Value) continue;
            bool newsUp = false, risesLater = false;
            foreach (var (ev, effectTurn) in upReports)
            {
                if (!ev.Matches(r)) continue;
                newsUp = true;
                if (effectTurn - turn >= 2) risesLater = true;
            }
            foreach (var m in memos)
                if (ScrapPrediction.Matches(m, r)) newsUp = true;
            bool gear = r.ItemType == ItemTypeData.ItemType.Weapon || r.ItemType == ItemTypeData.ItemType.Armor;
            list.Add(new ProphetAutoCandidate
            {
                ItemId = r.ItemId,
                Price = r.CurrentPrice.Value,
                Stock = r.Stock.Value,
                NewsUp = newsUp,
                WantToBuy = risesLater || (weakness.HasValue && gear && r.ItemAttribute == weakness.Value),
            });
        }
        return list;
    }

    /// <summary>
    /// 紙面で読める「上がる」報道（直近 ProphetNewsLookbackDays 日の、購読中の社の号・購読なしなら壁新聞に載った報道で、まだ効き終わっていないもの。
    /// 訂正記事が載ったものは外す）。ヘッドレスの reader ボット（ReadNewsBias）と同じ読み方。
    /// </summary>
    private static List<(NewsEventInstance ev, int effectTurn)> ReadUpReports(TomsModel toms)
    {
        var result = new List<(NewsEventInstance, int)>();
        var news = AutoPlayUi.Resolve<NewsModel>();
        var subs = AutoPlayUi.Resolve<NewspaperSubscriptionModel>();
        if (news == null || subs == null) return result;
        int turn = AutoPlay.CurrentTurn;
        news.Build(toms.FlowSeed);
        var read = new Dictionary<string, (NewsEventInstance ev, int effectTurn)>();
        for (int day = Mathf.Max(1, turn - ProphetNewsLookbackDays); day <= turn; day++)
        {
            var issue = news.IssueOf(day);
            var subscribed = new HashSet<string>();
            foreach (var c in subs.ActiveOn(day)) subscribed.Add(c.companyId);
            var visible = subscribed.Count > 0 ? issue.FindAll(e => subscribed.Contains(e.companyId)) : NewspaperPresenter.WallPaperEntries(issue);
            foreach (var e in visible)
            {
                var ev = e.eventInstance;
                if (ev == null) continue;
                if (e.kind == NewsEntryKind.Correction) { read.Remove(ev.key); continue; }
                if (e.kind != NewsEntryKind.Report || !ev.HasTarget || ev.trendDelta <= 0f) continue;
                int eff = e.effectTurn > 0 ? e.effectTurn : ev.effectTurn;
                if (read.TryGetValue(ev.key, out var known)) eff = Mathf.Min(eff, known.effectTurn);
                read[ev.key] = (ev, eff);
            }
        }
        foreach (var kv in read.Values)
            if (turn < kv.effectTurn + Mathf.Max(1, kv.ev.durationTurns)) result.Add((kv.ev, kv.effectTurn));
        return result;
    }

    /// <summary>まだ答えの出ていないスクラップの予想メモ（占い師の画面のカードの「新聞 ▲」と同じ）。</summary>
    private static List<string> ActiveScrapMemos(TomsModel toms)
    {
        var list = new List<string>();
        var scrapbook = AutoPlayUi.Resolve<ScrapbookModel>();
        var news = AutoPlayUi.Resolve<NewsModel>();
        var subs = AutoPlayUi.Resolve<NewspaperSubscriptionModel>();
        if (scrapbook == null || news == null || subs == null) return list;
        NewspaperSaveStore.EnsureLoaded(toms.FlowSeed, subs, scrapbook, news);
        int turn = AutoPlay.CurrentTurn;
        foreach (var s in scrapbook.Pinned)
        {
            if (string.IsNullOrEmpty(s.prediction)) continue;
            var st = ScrapbookModel.StateOf(s, news.FindEntry(s.entryKey), turn);
            if (st == ScrapState.Pending || st == ScrapState.Unconfirmed) list.Add(s.prediction);
        }
        return list;
    }

    /// <summary>占い師の画面を開いて、カード → 枠 を押し、閉じる。結果は IProphetIntervention の枠で確かめる。</summary>
    private async UniTask PlaceProphetAsync(ProphetAutoPick pick, CancellationToken ct)
    {
        var state = AutoPlayUi.Resolve<StateManager>();
        var shop = AutoPlayUi.Resolve<TomsShopView>();
        var intervention = AutoPlayUi.Resolve<IProphetIntervention>();
        var toms = AutoPlayUi.Resolve<TomsModel>();
        ProphetView view = null;
        ProphetPlaceResult? result = null;
        string note = null;
        int moneyBefore = toms != null ? toms.PlayerMoney.Value : 0;
        try
        {
            if (state == null || shop == null || intervention == null) { note = "店か占い師が見つからない"; return; }
            var open = AutoPlayUi.GetField<Button>(shop, "ProphetButton");
            if (!AutoPlayUi.IsClickable(open)) { note = "占い師のボタンが押せない（まだ解禁されていない）"; return; }
            if (!await PressAsync(open, "店: 占い師", ct)) { note = "占い師のボタンを押せなかった"; return; }
            if (!await WaitUntilAsync(() =>
                {
                    view = ViewOf<ProphetView>();
                    return state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Prophet && view != null && view.IsOpen;
                }, ProphetWaitSeconds, ct))
            {
                note = "占い師の画面が開かなかった";
                return;
            }
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            view.SetFilter(ProphetItemFilter.All);
            view.ClearSelection();
            var card = view.ItemCardButton(pick.ItemId);
            if (!AutoPlayUi.IsClickable(card)) { note = "品のカードが棚に無い"; return; }
            string name = AutoPlayUi.Resolve<ItemModel>()?.GetRuntimeItem(pick.ItemId)?.ItemName ?? pick.ItemId;
            if (!await PressAsync(card, $"占い師: 「{name}」のカードを選ぶ", ct) || view.SelectedItemId != pick.ItemId)
            {
                note = "品のカードを選べなかった";
                return;
            }
            await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds), DelayType.UnscaledDeltaTime, cancellationToken: ct);

            // 押す前に答えを控えておく（入らなかったときの理由）
            var expected = intervention.CanPlace(pick.ItemId, pick.Direction);
            var slot = view.SlotButton(pick.Direction);
            if (!await PressAsync(slot, $"占い師: {DirectionLabel(pick.Direction)}枠に入れる", ct)) { note = "枠のボタンを押せなかった"; result = expected; return; }
            bool placed = await WaitUntilAsync(() => intervention.GetSlot(pick.Direction)?.ItemId == pick.ItemId, ProphetWaitSeconds, ct);
            result = placed ? ProphetPlaceResult.Ok : (expected == ProphetPlaceResult.Ok ? intervention.CanPlace(pick.ItemId, pick.Direction) : expected);
            if (placed) await UniTask.Delay(TimeSpan.FromSeconds(UiStepSeconds * 2f), DelayType.UnscaledDeltaTime, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            note = "中断";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            note = $"例外: {e.Message}";
        }
        finally
        {
            CloseProphetView(view, state);
            int paid = toms != null ? moneyBefore - toms.PlayerMoney.Value : 0;
            AutoPlay.AddLog(result == ProphetPlaceResult.Ok
                ? $"占い師: {DirectionLabel(pick.Direction)}枠に入れた（-{paid:N0}G・力 {intervention?.Power}/{intervention?.MaxPower}）"
                : $"占い師: 入れられなかった（{(result.HasValue ? result.Value.ToString() : "-")}{(note != null ? " / " + note : "")}）");
            if (AutoPlay.Strategy is IAutoPlayProphetUser user)
            {
                try { user.OnProphetDone(CreateContext(), pick, result, note); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>占い師の画面を閉じる（キャンセル済みでも動くよう待たずに押し、店ホームに戻っていなければ戻す）。</summary>
    private static void CloseProphetView(ProphetView view, StateManager state)
    {
        try
        {
            if (view != null && view.IsOpen)
            {
                view.ClearSelection();
                var close = view.CloseButton;
                if (AutoPlayUi.IsClickable(close)) close.onClick.Invoke();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        if (state != null && state.CurrentPhase.Value == GamePhase.TomsShop && state.CurrentTomsShopPhase.Value == TomsShopGamePhase.Prophet)
            state.ChangeTomsShopPhase(TomsShopGamePhase.Shop);
    }

    private static string DirectionLabel(ProphetDirection d) => d == ProphetDirection.Up ? "上げる" : "下げる";
}
#endif
