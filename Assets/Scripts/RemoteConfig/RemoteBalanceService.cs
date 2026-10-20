using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 結合配信 balance.json の取得→適用→キャッシュを制御する（RemoteConfigService の balance 版）。
/// 順序（2026-10-20〜 キャッシュ優先）: ローカルキャッシュ（最新は裏で取ってキャッシュだけ更新）→ サーバー取得 → 上書きなし（各SOの既定値）。
/// </summary>
public sealed class RemoteBalanceService
{
    private readonly IRemoteConfigSource _source;
    private readonly RemoteConfigCache _cache;
    private readonly IRemoteConfigSource _backgroundSource;

    /// <param name="backgroundSource">キャッシュで起動したあと裏で最新を取る取得元（長めのタイムアウト）。null なら source を使う。</param>
    public RemoteBalanceService(IRemoteConfigSource source, RemoteConfigCache cache, IRemoteConfigSource backgroundSource = null)
    {
        _source = source;
        _cache = cache;
        _backgroundSource = backgroundSource ?? source;
    }

    /// <summary>起動時に1回。各シーンの設定/マスター適用より前に await 完了させること。</summary>
    public async UniTask InitializeAsync(CancellationToken ct)
    {
        // 1) ローカルキャッシュ（前回成功分）があれば待たずに使う。最新は裏で取り、次の起動から使う
        var cached = _cache.LoadJson();
        if (TryApply(cached, save: false))
        {
            Debug.Log("[RemoteBalance] キャッシュを適用しました（最新は裏で取得します）。");
            RemoteConfigRefresh.RefreshCacheInBackground(_backgroundSource, _cache, ValidateOnly, "RemoteBalance");
            return;
        }

        // 2) キャッシュが無いときだけサーバー取得を待つ（成功時のみキャッシュ更新）
        var json = await _source.FetchEnvelopeJsonAsync(ct);
        if (TryApply(json, save: true))
            return;

        RemoteBalance.ResetToDefault();
        Debug.Log("[RemoteBalance] 上書きなし。SO 既定値を使用します。");
    }

    /// <summary>RemoteBalance を書き換えずに検証だけする（裏取り用）。通れば version、不採用なら -1。</summary>
    private static int ValidateOnly(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;
        try
        {
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            if ((root.Value<int?>("schemaVersion") ?? -1) != RemoteBalance.ExpectedSchemaVersion) return -1;
            return root.Value<int?>("version") ?? 0;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RemoteBalance] 裏取りの解析失敗: {e.Message}");
            return -1;
        }
    }

    /// <summary>schemaVersion 検証込みで取り込む。失敗（解析不可/不一致）は false。</summary>
    private bool TryApply(string json, bool save)
    {
        if (string.IsNullOrEmpty(json)) return false;

        int appliedVersion = RemoteBalance.OverrideFromBundle(json);
        if (appliedVersion < 0) return false;

        if (save) _cache.Save(appliedVersion, json);
        return true;
    }
}
