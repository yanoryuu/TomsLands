using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// アイテムマスター上書きの取得→検証→適用→キャッシュを制御する（RemoteConfigService のアイテム版）。
/// 順序（2026-10-20〜 キャッシュ優先）: ローカルキャッシュ（最新は裏で取ってキャッシュだけ更新）→ サーバー取得 → 上書きなし（SO 既定値）。
/// </summary>
public sealed class ItemMasterService
{
    private readonly IRemoteConfigSource _source;
    private readonly RemoteConfigCache _cache;
    private readonly IRemoteConfigSource _backgroundSource;

    /// <param name="backgroundSource">キャッシュで起動したあと裏で最新を取る取得元（長めのタイムアウト）。null なら source を使う。</param>
    public ItemMasterService(IRemoteConfigSource source, RemoteConfigCache cache, IRemoteConfigSource backgroundSource = null)
    {
        _source = source;
        _cache = cache;
        _backgroundSource = backgroundSource ?? source;
    }

    /// <summary>起動時に1回。masterItems ロードより前に await 完了させること。</summary>
    public async UniTask InitializeAsync(CancellationToken ct)
    {
        // 1) ローカルキャッシュ（前回成功分）があれば待たずに使う。最新は裏で取り、次の起動から使う
        var cached = _cache.LoadJson();
        if (TryApply(cached, save: false))
        {
            Debug.Log("[ItemMaster] キャッシュを適用しました（最新は裏で取得します）。");
            RemoteConfigRefresh.RefreshCacheInBackground(_backgroundSource, _cache, ValidateOnly, "ItemMaster");
            return;
        }

        // 2) キャッシュが無いときだけサーバー取得を待つ（成功時のみキャッシュ更新）
        var json = await _source.FetchEnvelopeJsonAsync(ct);
        if (TryApply(json, save: true))
            return;

        // 3) 上書きなし（SO 既定値）
        ItemMaster.ResetToDefault();
        Debug.Log("[ItemMaster] 上書きなし。SO 既定値を使用します。");
    }

    /// <summary>検証に通れば適用して true。解析失敗 / schemaVersion 不一致 / 不正行 で false。</summary>
    private bool TryApply(string json, bool save)
    {
        if (string.IsNullOrEmpty(json)) return false;

        // 適用前に payload 全体を検証（1行でも不正なら不採用）
        ItemMasterEnvelope env;
        try { env = JsonUtility.FromJson<ItemMasterEnvelope>(json); }
        catch (Exception e)
        {
            Debug.LogWarning($"[ItemMaster] 解析失敗: {e.Message}");
            return false;
        }
        if (env == null || env.items == null) return false;

        if (!Validate(env, out var reason))
        {
            Debug.LogWarning($"[ItemMaster] バリデーション失格: {reason}。適用しない。");
            return false;
        }

        int appliedVersion = ItemMaster.OverrideFromEnvelope(json);
        if (appliedVersion < 0) return false; // schemaVersion 不一致など

        if (save) _cache.Save(appliedVersion, json);
        return true;
    }

    /// <summary>ItemMaster を書き換えずに検証だけする（裏取り用）。通れば version、不採用なら -1。</summary>
    private static int ValidateOnly(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;
        ItemMasterEnvelope env;
        try { env = JsonUtility.FromJson<ItemMasterEnvelope>(json); }
        catch (Exception e)
        {
            Debug.LogWarning($"[ItemMaster] 裏取りの解析失敗: {e.Message}");
            return -1;
        }
        if (env == null || env.items == null || env.schemaVersion != ItemMaster.ExpectedSchemaVersion) return -1;
        return Validate(env, out _) ? env.version : -1;
    }

    /// <summary>各行の値域・enum パース可否を検査。1行でも不正なら全体不採用。</summary>
    private static bool Validate(ItemMasterEnvelope env, out string reason)
    {
        reason = null;
        foreach (var ov in env.items)
        {
            if (ov == null) { reason = "null 行"; return false; }
            if (string.IsNullOrEmpty(ov.itemId)) { reason = "itemId 空"; return false; }
            if (ov.basePrice < 0) { reason = $"{ov.itemId}: basePrice < 0"; return false; }
            if (ov.maxStock <= 0) { reason = $"{ov.itemId}: maxStock <= 0"; return false; }
            if (ov.initialStock < 0 || ov.initialDisplayStock < 0) { reason = $"{ov.itemId}: stock < 0"; return false; }
            if (ov.requiredLevel < 0) { reason = $"{ov.itemId}: requiredLevel < 0"; return false; }
            if (ov.salesRate < 0.1f || ov.salesRate > 5.0f) { reason = $"{ov.itemId}: salesRate 範囲外"; return false; }
            if (!Enum.TryParse<ItemTypeData.ItemType>(ov.itemType, true, out _))
            { reason = $"{ov.itemId}: itemType 不正 '{ov.itemType}'"; return false; }
            if (!ItemTypeData.TryParseAttribute(ov.itemAttribute, out _))
            { reason = $"{ov.itemId}: itemAttribute 不正 '{ov.itemAttribute}'"; return false; }
        }
        return true;
    }
}
