using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 結合配信 balance.json の取得→適用→キャッシュを制御する（RemoteConfigService の balance 版）。
/// フォールバック順序: サーバー成功 → ローカルキャッシュ → 上書きなし（各SOの既定値）。
/// </summary>
public sealed class RemoteBalanceService
{
    private readonly IRemoteConfigSource _source;
    private readonly RemoteConfigCache _cache;

    public RemoteBalanceService(IRemoteConfigSource source, RemoteConfigCache cache)
    {
        _source = source;
        _cache = cache;
    }

    /// <summary>起動時に1回。各シーンの設定/マスター適用より前に await 完了させること。</summary>
    public async UniTask InitializeAsync(CancellationToken ct)
    {
        var json = await _source.FetchEnvelopeJsonAsync(ct);
        if (TryApply(json, save: true))
            return;

        var cached = _cache.LoadJson();
        if (TryApply(cached, save: false))
        {
            Debug.Log("[RemoteBalance] キャッシュを適用しました。");
            return;
        }

        RemoteBalance.ResetToDefault();
        Debug.Log("[RemoteBalance] 上書きなし。SO 既定値を使用します。");
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
