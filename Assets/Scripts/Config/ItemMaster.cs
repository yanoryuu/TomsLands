using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スプレッドシート由来のアイテムマスター上書きを保持する静的ファサード（GameConst と対構造）。
/// 起動時(Boot)に <see cref="OverrideFromEnvelope"/> で適用し、各シーンの masterItems ロード直後に
/// <see cref="ApplyOverrides"/> を通すことで、以降の master 参照すべてが上書き値になる。
/// 上書きが無ければ SO 既定値のまま（Boot を通さないエディタ直接再生でも安全）。
/// </summary>
public static class ItemMaster
{
    /// <summary>受け付ける schemaVersion。構造を破壊的に変えた時のみ上げる。</summary>
    public const int ExpectedSchemaVersion = 1;

    private static readonly Dictionary<string, ItemOverride> _overrides = new();

    public static int OverrideCount => _overrides.Count;

    /// <summary>
    /// 配信エンベロープから上書きを取り込む。schemaVersion 不一致/解析失敗時は -1（不採用）。
    /// </summary>
    /// <returns>適用した version。未適用なら -1。</returns>
    public static int OverrideFromEnvelope(string json)
    {
        if (string.IsNullOrEmpty(json)) return -1;

        ItemMasterEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<ItemMasterEnvelope>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ItemMaster] エンベロープ解析失敗。{e.Message}");
            return -1;
        }

        if (envelope == null) return -1;

        if (envelope.schemaVersion != ExpectedSchemaVersion)
        {
            Debug.LogWarning($"[ItemMaster] schemaVersion 不一致 (expected {ExpectedSchemaVersion}, got {envelope.schemaVersion})。不採用。");
            return -1;
        }

        _overrides.Clear();
        if (envelope.items != null)
        {
            foreach (var ov in envelope.items)
            {
                if (ov == null || string.IsNullOrEmpty(ov.itemId)) continue;
                _overrides[ov.itemId] = ov;
            }
        }

        Debug.Log($"[ItemMaster] version {envelope.version} を適用（{_overrides.Count}件）。");
        return envelope.version;
    }

    public static ItemOverride GetOverride(string itemId)
        => (!string.IsNullOrEmpty(itemId) && _overrides.TryGetValue(itemId, out var ov)) ? ov : null;

    /// <summary>
    /// master リストへ上書きを適用したリストを返す。
    /// 上書き対象のみ Instantiate でクローンして書き換え、非対象は元参照のまま（SO本体は汚さない）。
    /// </summary>
    public static List<ItemData> ApplyOverrides(List<ItemData> masters)
    {
        if (masters == null) return new List<ItemData>();
        if (_overrides.Count == 0) return masters; // 上書き無し → 元のまま

        var result = new List<ItemData>(masters.Count);
        int applied = 0;
        foreach (var master in masters)
        {
            if (master == null) continue;

            if (_overrides.TryGetValue(master.itemId, out var ov))
            {
                var clone = Object.Instantiate(master); // 実行時コピー（アセット非汚染）
                clone.name = master.name;                // (Clone) サフィックス回避
                ov.ApplyTo(clone);
                result.Add(clone);
                applied++;
            }
            else
            {
                result.Add(master);
            }
        }
        Debug.Log($"[ItemMaster] ApplyOverrides: masters={masters.Count} / overrides={_overrides.Count} / 適用={applied}件");
        return result;
    }

    /// <summary>上書きを破棄して SO 既定値に戻す（フォールバック用）。</summary>
    public static void ResetToDefault() => _overrides.Clear();
}
