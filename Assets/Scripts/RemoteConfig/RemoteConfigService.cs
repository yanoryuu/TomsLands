using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// version 比較 → 取得 → キャッシュ → GameConst 適用 を制御する。
/// 順序（2026-10-20〜 キャッシュ優先）: ローカルキャッシュ（あれば即適用し、最新は裏で取ってキャッシュだけ更新）
/// → キャッシュが無ければサーバー取得（短いタイムアウト）→ ベイク済みデフォルト。
/// 値の範囲バリデーションはここ（適用判定）で行い、GameConst ファサードは
/// 「来た値を信じて入れるだけ」に責務を保つ。
/// </summary>
public sealed class RemoteConfigService
{
    private readonly IRemoteConfigSource _source;
    private readonly RemoteConfigCache _cache;
    private readonly IRemoteConfigSource _backgroundSource;

    /// <param name="backgroundSource">キャッシュで起動したあと裏で最新を取る取得元（長めのタイムアウト）。null なら source を使う。</param>
    public RemoteConfigService(IRemoteConfigSource source, RemoteConfigCache cache, IRemoteConfigSource backgroundSource = null)
    {
        _source = source;
        _cache = cache;
        _backgroundSource = backgroundSource ?? source;
    }

    /// <summary>
    /// 起動時に1回呼ぶ。GameConst の初回アクセスより前に await 完了させること。
    /// </summary>
    public async UniTask InitializeAsync(CancellationToken ct)
    {
        // 1) ローカルキャッシュ（前回成功分）があれば待たずに使う。最新は裏で取り、次の起動から使う
        var cached = _cache.LoadJson();
        if (TryApply(cached, save: false))
        {
            Debug.Log("[RemoteConfig] キャッシュを適用しました（最新は裏で取得します）。");
            RemoteConfigRefresh.RefreshCacheInBackground(_backgroundSource, _cache, ValidateOnly, "RemoteConfig");
            return;
        }

        // 2) キャッシュが無い（初回起動など）ときだけサーバー取得を待つ（成功時のみキャッシュ更新）
        var json = await _source.FetchEnvelopeJsonAsync(ct);
        if (TryApply(json, save: true))
            return;

        // 3) ベイク済みデフォルト（GameConstSettings）
        GameConst.ResetToDefault();
        Debug.Log("[RemoteConfig] ベイク済みデフォルトを使用します。");
    }

    /// <summary>
    /// JSON を適用し、バリデーションに通れば true。
    /// 解析失敗 / schemaVersion 不一致 / 範囲外 のいずれでも false（次のフォールバックへ）。
    /// </summary>
    private bool TryApply(string json, bool save)
    {
        if (string.IsNullOrEmpty(json)) return false;

        int appliedVersion = GameConst.OverrideFromEnvelope(json);
        if (appliedVersion < 0) return false; // 解析失敗 or schemaVersion 不一致

        if (!Validate(GameConst.Data, out var reason))
        {
            // ここで _data は不正値が入った状態だが、呼び出し側が次段で上書きするか、
            // 最終的に ResetToDefault() で必ず正常値へ戻すため、gameplay には漏れない。
            Debug.LogWarning($"[RemoteConfig] バリデーション失格 (version {appliedVersion}): {reason}");
            return false;
        }

        if (save) _cache.Save(appliedVersion, json);
        return true;
    }

    /// <summary>GameConst を書き換えずに検証だけする（裏取り用）。通れば version、不採用なら -1。</summary>
    private static int ValidateOnly(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;
        var env = new GameConstEnvelope { data = new GameConstData() };
        try { JsonUtility.FromJsonOverwrite(json, env); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RemoteConfig] 裏取りの解析失敗: {e.Message}");
            return -1;
        }
        if (env.schemaVersion != GameConst.ExpectedSchemaVersion || env.data == null) return -1;
        return Validate(env.data, out _) ? env.version : -1;
    }

    /// <summary>適用前ガード。0除算・負値・空配列など破綻を招く値を弾く。</summary>
    private static bool Validate(GameConstData d, out string reason)
    {
        reason = null;
        if (d == null) { reason = "data が null"; return false; }
        if (d.debtMultiplier <= 0f) { reason = "debtMultiplier <= 0"; return false; }
        if (d.initMoney < 0) { reason = "initMoney < 0"; return false; }
        if (d.debtPaymentInterval <= 0) { reason = "debtPaymentInterval <= 0"; return false; }
        if (d.debtBaseAmount < 0) { reason = "debtBaseAmount < 0"; return false; }
        if (d.blackSmithLevelUpCosts == null || d.blackSmithLevelUpCosts.Length == 0)
        {
            reason = "blackSmithLevelUpCosts が空";
            return false;
        }
        if (d.maxDungeonLevel <= 0 || d.maxBlackSmithLevel <= 0 ||
            d.maxToolShopLevel <= 0 || d.maxInfoBrokerLevel <= 0)
        {
            reason = "maxXxxLevel <= 0";
            return false;
        }
        return true;
    }
}
