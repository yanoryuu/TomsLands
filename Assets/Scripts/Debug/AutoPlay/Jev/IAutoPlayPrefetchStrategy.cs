#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// 判断を先にまとめて取りに行く戦略（人格プレイ。外部のモデルに問い合わせるので非同期）。
/// IAutoPlayStrategy のメソッドは同期なので、AutoPlayController が節目でここを呼び、戦略はその結果をキャッシュして使う。
/// </summary>
public interface IAutoPlayPrefetchStrategy
{
    /// <summary>
    /// その日の仕入れの前（DebugAutoPlayer の ExternalBeforeProcurement、店は ExternalBusy で止まっている）に1回。
    /// prep は次の配信が近ければその判断材料（遠ければ null）。完了まで店の操作は待つ。
    /// </summary>
    UniTask PrefetchShopDayAsync(AutoPlayContext ctx, AutoPlayStreamPrepContext prep, CancellationToken ct);

    /// <summary>配信の自動操作を始めたとき（戦闘は止めないので、戦略は裏で問い合わせる）。</summary>
    void OnStreamStarted(AutoPlayStreamContext ctx);

    /// <summary>配信の画面が終わったとき（リザルトパネルへ移った）。</summary>
    void OnStreamEnded();

    /// <summary>ラン終了の画面に来たとき（bankrupt = ゲームオーバー）。1 シーンにつき 1 回。</summary>
    void OnRunEnded(bool bankrupt);

    /// <summary>
    /// 村に着いて施設を見て回る前に 1 回（施設への投資の判断を先にまとめて聞く）。完了まで村の操作は待つ。
    /// 判断は IAutoPlayJikkyoExtras.ShouldInvestFacility で使う。
    /// </summary>
    UniTask PrefetchVillageAsync(CancellationToken ct);

    /// <summary>村の施設を見て回り終えたとき（投資の結果をログ・学習に残す）。</summary>
    void OnVillageTourEnded();

    /// <summary>
    /// 出店準備でスターターレリックを選ぶ前に 1 回（候補は画面の並び順）。完了まで出店準備の操作は待つ。
    /// 判断は IAutoPlayStrategy.ChooseStarterRelic で使う。
    /// </summary>
    UniTask PrefetchStarterRelicAsync(IReadOnlyList<AutoPlayStarterRelicChoice> choices, CancellationToken ct);

    /// <summary>問い合わせ中か（詰まり検知から外すため）。</summary>
    bool IsThinking { get; }
}
#endif
