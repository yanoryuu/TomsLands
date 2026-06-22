using System;
using R3;
using UnityEngine;

/// <summary>
/// 通常フェーズ(TomsShop)内のターン進行（イベント→仕入れ→商品陳列→営業）を管理する。
/// 画面パネルの切替は行わず、フェーズ状態のみを保持する（UI出し分けは TurnPhaseView が購読して行う）。
/// StateManager（GamePhase/TomsShopGamePhase）とは関心を分離したマクロフェーズ層。
/// </summary>
public class TurnPhaseManager : IDisposable
{
    /// <summary>現在のターン進行フェーズ。</summary>
    public ReactiveProperty<TurnPhase> CurrentTurnPhase { get; } = new(TurnPhase.Event);

    /// <summary>
    /// 新しいターンの先頭（イベントフェーズ）から開始する。
    /// 既にイベントなら ForceNotify で再発火する。
    /// </summary>
    public void BeginTurnPhases(bool hasPendingEvent)
    {
        // 保留イベントがあれば Event から、無ければ最初から仕入れ(Procurement)へ。
        // ※ 購読ハンドラ内での再帰 Advance を避けるため、開始フェーズはここで直接決める。
        var target = hasPendingEvent ? TurnPhase.Event : TurnPhase.Procurement;
        if (CurrentTurnPhase.Value == target)
            CurrentTurnPhase.ForceNotify();
        else
            CurrentTurnPhase.Value = target;

        Debug.Log($"[TurnPhaseManager] BeginTurnPhases → {target}");
    }

    /// <summary>
    /// 次のフェーズへ進む。Sales が終端で、それ以上は進めない（巻き戻し防止）。
    /// </summary>
    public void AdvanceTurnPhase()
    {
        switch (CurrentTurnPhase.Value)
        {
            case TurnPhase.Event:
                CurrentTurnPhase.Value = TurnPhase.Procurement;
                break;
            case TurnPhase.Procurement:
                CurrentTurnPhase.Value = TurnPhase.Display;
                break;
            case TurnPhase.Display:
                CurrentTurnPhase.Value = TurnPhase.Sales;
                break;
            case TurnPhase.Sales:
                // 終端：営業開始→TurnEndSummary→NextTurn の流れで次ターンへ進むため、ここでは何もしない
                break;
        }
        Debug.Log($"[TurnPhaseManager] AdvanceTurnPhase → {CurrentTurnPhase.Value}");
    }

    /// <summary>スキップは「次へ」と同義（そのフェーズで何もせず前進）。</summary>
    public void SkipCurrent() => AdvanceTurnPhase();

    /// <summary>
    /// 詳細画面(TomsShopGamePhase)がどのフェーズに属するかを返す。
    /// ヒント絞り込みやゲート判定に使う。Shop(ホーム)は現フェーズに依存するため null を返す。
    /// </summary>
    public TurnPhase? GetPhaseForScreen(TomsShopGamePhase screen)
    {
        switch (screen)
        {
            case TomsShopGamePhase.Broker:
            case TomsShopGamePhase.BlackSmith:
            case TomsShopGamePhase.ToolShop:
            case TomsShopGamePhase.DungeonLevelUp:
            case TomsShopGamePhase.Advertisement:
            case TomsShopGamePhase.Hero:
            case TomsShopGamePhase.Map:
                return TurnPhase.Procurement;
            case TomsShopGamePhase.Prophet:
                return TurnPhase.Display;
            case TomsShopGamePhase.TurnEndSummary:
                return TurnPhase.Sales;
            default:
                return null; // Shop（ホーム）
        }
    }

    public void Dispose()
    {
        CurrentTurnPhase.Dispose();
    }
}
