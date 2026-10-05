using System;
using System.Linq;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// 配信前の寄り道（鍛冶屋で仕入れてから配信へ）を受け持つプレゼンター。
///
/// 背景: 通常営業では陳列分が全部売れる（売り注文制）ため、配信日の朝には在庫が空になりやすい。
/// 配信の品出し設定(StreamingSetting)は FightScene 側にあり鍛冶屋(TomsShop側)へ直接行けないので、
/// TomsShop を離れる直前（GameFlowManager が配信日に入った時点）で一旦止め、
/// 「鍛冶屋へ寄る / このまま配信へ」を選ばせる。
///
/// 画面は既存の BlackSmith とポップアップ(PopUpManager)だけを使うので、シーン/プレハブの配線は不要。
/// 鍛冶屋が使えない（未登録）・ポップアップが無い場合は従来どおり即配信へ進む。
/// </summary>
public class PreStreamPresenter : IStartable, IDisposable
{
    private readonly GameFlowManager gameFlowManager;
    private readonly StateManager stateManager;
    private readonly PopUpManager popUpManager;
    private readonly ItemModel itemModel;
    private readonly DungeonRepository dungeonRepository;

    public PreStreamPresenter(
        GameFlowManager gameFlowManager,
        StateManager stateManager,
        PopUpManager popUpManager,
        ItemModel itemModel,
        DungeonRepository dungeonRepository)
    {
        this.gameFlowManager = gameFlowManager;
        this.stateManager = stateManager;
        this.popUpManager = popUpManager;
        this.itemModel = itemModel;
        this.dungeonRepository = dungeonRepository;
    }

    public void Start()
    {
        // NextTurn は利用者操作（サマリーの確認）からしか呼ばれないので、Start での登録で間に合う
        gameFlowManager.SetPreStreamHandler(OnPreStream);
    }

    /// <param name="fromProcurement">true=鍛冶屋を閉じたところ / false=配信日に入った直後</param>
    private void OnPreStream(bool fromProcurement)
    {
        bool canVisitBlackSmith = stateManager.HasHandler(TomsShopGamePhase.BlackSmith);
        if (popUpManager == null || !canVisitBlackSmith)
        {
            gameFlowManager.ProceedToBattle();
            return;
        }

        int kinds = 0, total = 0;
        if (itemModel?.RuntimeItems != null)
        {
            var stocked = itemModel.RuntimeItems.Where(r => r != null && r.Stock.Value > 0).ToList();
            kinds = stocked.Count;
            total = stocked.Sum(r => r.Stock.Value);
        }

        string dungeonName = null;
        var key = gameFlowManager.PendingBattleDungeon;
        if (key.HasValue && dungeonRepository != null)
            dungeonName = dungeonRepository.GetById(key.Value)?.dungeonName;

        string stockLine = total > 0
            ? $"配信に持ち込める在庫: {kinds}種 {total}個"
            : "配信に持ち込める在庫がありません！";

        if (!fromProcurement)
        {
            string where = string.IsNullOrEmpty(dungeonName) ? "" : $"（{dungeonName}）";
            popUpManager.Show(new PopUpData
            {
                Title = $"今日は配信日{where}",
                Message = $"{stockLine}\n配信の前に鍛冶屋で仕入れますか？",
                ConfirmButtonText = "鍛冶屋へ寄る",
                CancelButtonText = "このまま配信へ",
                Size = PopupSizeEnum.Medium,
                OnConfirm = GoToBlackSmith,
                OnCancel = gameFlowManager.ProceedToBattle,
            });
        }
        else
        {
            popUpManager.Show(new PopUpData
            {
                Title = "配信を始めますか？",
                Message = stockLine,
                ConfirmButtonText = "配信を始める",
                CancelButtonText = "仕入れを続ける",
                Size = PopupSizeEnum.Medium,
                OnConfirm = gameFlowManager.ProceedToBattle,
                OnCancel = null, // 鍛冶屋に留まる
            });
        }
    }

    private void GoToBlackSmith()
    {
        // ポップアップ表示中に別経路で配信へ進んでいたら何もしない
        if (!gameFlowManager.IsAwaitingStream.Value) return;
        stateManager.ChangeTomsShopPhase(TomsShopGamePhase.BlackSmith);
        Debug.Log("[PreStream] 配信前に鍛冶屋へ寄り道");
    }

    public void Dispose()
    {
        gameFlowManager?.SetPreStreamHandler(null);
    }
}
