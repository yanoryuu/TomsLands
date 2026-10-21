#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
#define TOMS_STEAM
#endif

using System;
using R3;
using UnityEngine;
#if TOMS_STEAM
using Steamworks;
#endif

/// <summary>
/// 実績の解除と進捗（Stats）を受け持つ。シーンを跨いで1つ（<see cref="Shared"/>）。
///
/// ・解除はまず手元の記録（AchievementStore）に残し、Steam が使えれば SetAchievement → StoreStats する。
///   Steam が無い（エディタ・Steam 外の起動）ときは「解除したつもり」をログに出すだけで、次に Steam につながったときに送る。
/// ・Stats は「これまでの最大値」だけを持つ（下がらない）。StoreStats は解除時と一定間隔・終了時にまとめて送る。
/// ・エディタの自動テストプレイ（SaveSlotManager.DevRootOverride）中は隔離フォルダに記録し、Steam には一切触れない。
///
/// 条件の判定は AchievementTracker（各シーンの EntryPoint）と、他の画面から来る AchievementEvents が行い、
/// ここには「解除」「数値の更新」だけが来る。
/// </summary>
public sealed class AchievementService
{
    private const float StoreIntervalSec = 30f;

    private static AchievementService shared;

    /// <summary>シーンを跨いで共有する実体。Boot で先に作るが、Boot を通らない起動（エディタで途中のシーンから再生）でも使える。</summary>
    public static AchievementService Shared => shared ??= new AchievementService();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => shared = null;

    private readonly Subject<AchievementId> unlocked = new();
    /// <summary>新しく解除された（デバッグメニュー用）。</summary>
    public Observable<AchievementId> OnUnlocked => unlocked;

    private AchievementGlobalData data;
    private string dataPath;
    private bool steamSynced;
    private bool storePending;
    private float lastStoreTime;

    private AchievementService()
    {
        SteamManager.Ticked += OnSteamTick;
        SteamManager.ShuttingDown += FlushToSteam;
    }

    /// <summary>Steam へ送れる状態か（隔離中は常に false）。</summary>
    /// 体験版（DemoBuild）は本編の実績を入れないので常に false。
    public static bool SteamAvailable => !DemoBuild.IsDemo && SteamManager.Initialized && !AchievementStore.IsSandboxed;

    private AchievementGlobalData Data
    {
        get
        {
            // 自動テストプレイの開始・終了で置き場所が変わるので、場所が変わったら読み直す
            var path = AchievementStore.GlobalPath;
            if (data == null || dataPath != path)
            {
                data = AchievementStore.LoadGlobal();
                dataPath = path;
                steamSynced = false;
            }
            return data;
        }
    }

    // ========================================
    // 参照
    // ========================================

    public bool IsUnlocked(AchievementId id) => Data.unlocked.Contains(AchievementDefinitions.Get(id).ApiName);

    public int GetStat(AchievementStat stat) => Data.GetStat(AchievementDefinitions.StatApiName(stat));

    /// <summary>全紙購読: これまでに購読した社の数。</summary>
    public int SubscribedCompanyCount => Data.subscribedCompanies.Count;

    // ========================================
    // 更新
    // ========================================

    /// <summary>実績を解除する。解除済みなら何もしない。</summary>
    public void Unlock(AchievementId id, string reason = null)
    {
        if (DemoBuild.IsDemo) return; // 体験版は本編の実績を解除しない
        var def = AchievementDefinitions.Get(id);
        var d = Data;
        if (d.unlocked.Contains(def.ApiName)) return;

        d.unlocked.Add(def.ApiName);
        AchievementStore.SaveGlobal(d);

        bool sent = false;
        if (SteamAvailable)
        {
            SyncIfNeeded();
            sent = SendUnlock(def.ApiName);
            storePending = true;
            FlushToSteam();
        }

        string where = AchievementStore.IsSandboxed ? "隔離中（Steam へは送らない）"
            : sent ? "Steam へ送信" : "Steam なし（解除したつもり。次に Steam につながったら送る）";
        Debug.Log($"[Achievement] 解除: {def.ApiName}「{def.DisplayName}」{(string.IsNullOrEmpty(reason) ? "" : $" ← {reason}")} / {where}");
        unlocked.OnNext(id);
    }

    /// <summary>Stat をこれまでの最大値として更新する（小さい値では下げない）。</summary>
    public void ReportMax(AchievementStat stat, int value)
    {
        if (DemoBuild.IsDemo) return;
        var name = AchievementDefinitions.StatApiName(stat);
        var d = Data;
        if (value <= d.GetStat(name)) return;

        d.SetStat(name, value);
        AchievementStore.SaveGlobal(d);

        if (SteamAvailable)
        {
            SyncIfNeeded();
            SendStat(name, value);
            storePending = true;
        }
    }

    /// <summary>Stat に 1 足す（クリア回数など）。</summary>
    public int Increment(AchievementStat stat)
    {
        int next = GetStat(stat) + 1;
        ReportMax(stat, next);
        return next;
    }

    /// <summary>購読した社を覚える。新しい社なら true。</summary>
    public bool AddSubscribedCompany(string companyId)
    {
        if (DemoBuild.IsDemo) return false;
        if (string.IsNullOrEmpty(companyId)) return false;
        var d = Data;
        if (d.subscribedCompanies.Contains(companyId)) return false;
        d.subscribedCompanies.Add(companyId);
        AchievementStore.SaveGlobal(d);
        ReportMax(AchievementStat.PapersSubscribed, d.subscribedCompanies.Count);
        return true;
    }

    /// <summary>
    /// 出店のクリア（リザルト集計）。同じランで2回来ても（リザルトで終了→続きから）1回だけ数える。
    /// </summary>
    public void HandleRunCleared(RunClearedInfo info)
    {
        if (DemoBuild.IsDemo) return;
        if (info == null) return;
        var run = AchievementStore.LoadRun();
        if (run.clearReported) return;
        run.clearReported = true;
        AchievementStore.SaveRun(run);

        var stat = info.Mode switch
        {
            GameModeId.Medium => AchievementStat.ClearsMedium,
            GameModeId.Long => AchievementStat.ClearsLong,
            _ => AchievementStat.ClearsShort,
        };
        Increment(stat);
        Debug.Log($"[Achievement] クリアを記録: {info.Mode} ランク{info.Rank} 日数{info.TotalTurns} 返済{info.DebtCycle}回");

        if (info.Mode == GameModeId.Short) Unlock(AchievementId.FirstClear, "Short をクリア");
        if (GetStat(AchievementStat.ClearsMedium) > 0 && GetStat(AchievementStat.ClearsLong) > 0)
            Unlock(AchievementId.LongTrader, "Normal と Long をクリア済み");
        if (info.Rank == "S") Unlock(AchievementId.RankS, "ランク S でクリア");

        // 返済は期日に払えないと破産（ゲームオーバー）なので、クリアした＝期日の返済はすべて払った。
        // 最終日は返済の判定が走らないので、その前日までに来た期日の回数と比べる。返済が1回も無いランは数えない
        int interval = Mathf.Max(1, GameConst.DebtPaymentInterval);
        int dueCount = Mathf.Max(1, (info.TotalTurns - 1) / interval);
        if (info.DebtCycle >= dueCount)
            Unlock(AchievementId.AllRepaid, $"返済 {info.DebtCycle}回 / 期日 {dueCount}回");
    }

    /// <summary>
    /// 【開発用】実績と Stats をすべて消す（手元の記録と Steam の両方）。
    /// デバッグメニューから、エディタ／開発ビルドでだけ呼ぶ。
    /// </summary>
    public void ResetAllForDevelopment()
    {
        if (!Debug.isDebugBuild)
        {
            Debug.LogWarning("[Achievement] リセットは開発ビルドでだけ使えます");
            return;
        }

        AchievementStore.DeleteGlobal();
        data = null;
        steamSynced = false;
#if TOMS_STEAM
        if (SteamAvailable)
        {
            SteamUserStats.ResetAllStats(true);
            SteamUserStats.StoreStats();
        }
#endif
        Debug.Log("[Achievement] 実績と Stats をすべてリセットしました");
    }

    // ========================================
    // Steam
    // ========================================

    /// <summary>
    /// Steam が使えるようになって最初の1回だけ、手元の記録と Steam をそろえる
    /// （Steam なしで解除したものを送る・Steam 側の大きい Stat を手元に取り込む）。
    /// </summary>
    private void SyncIfNeeded()
    {
        if (steamSynced || !SteamAvailable) return;
        steamSynced = true;
#if TOMS_STEAM
        var d = Data;
        bool changed = false;
        foreach (var def in AchievementDefinitions.All)
        {
            bool local = d.unlocked.Contains(def.ApiName);
            if (!SteamUserStats.GetAchievement(def.ApiName, out bool onSteam)) continue; // 管理画面に未登録
            if (local && !onSteam) SendUnlock(def.ApiName);
            else if (!local && onSteam) { d.unlocked.Add(def.ApiName); changed = true; }
        }
        foreach (AchievementStat stat in Enum.GetValues(typeof(AchievementStat)))
        {
            var name = AchievementDefinitions.StatApiName(stat);
            if (!SteamUserStats.GetStat(name, out int remote)) continue;
            int local = d.GetStat(name);
            if (local > remote) SendStat(name, local);
            else if (remote > local) { d.SetStat(name, remote); changed = true; }
        }
        if (changed) AchievementStore.SaveGlobal(d);
        storePending = true;
        FlushToSteam();
        Debug.Log("[Achievement] Steam と手元の記録をそろえました");
#endif
    }

    private void OnSteamTick()
    {
        if (!SteamAvailable) return;
        SyncIfNeeded();
        if (storePending && Time.unscaledTime - lastStoreTime >= StoreIntervalSec) FlushToSteam();
    }

    private void FlushToSteam()
    {
        if (!storePending || !SteamAvailable) return;
        storePending = false;
        lastStoreTime = Time.unscaledTime;
#if TOMS_STEAM
        if (!SteamUserStats.StoreStats()) Debug.LogWarning("[Achievement] StoreStats に失敗しました（次の機会に送り直す）");
#endif
    }

    private static bool SendUnlock(string apiName)
    {
#if TOMS_STEAM
        if (SteamUserStats.SetAchievement(apiName)) return true;
        Debug.LogWarning($"[Achievement] SetAchievement({apiName}) に失敗（Steamworks の管理画面に登録・公開されているか確認）");
#endif
        return false;
    }

    private static void SendStat(string name, int value)
    {
#if TOMS_STEAM
        if (!SteamUserStats.SetStat(name, value))
            Debug.LogWarning($"[Achievement] SetStat({name}) に失敗（Steamworks の管理画面に INT で登録・公開されているか確認）");
#endif
    }
}
