using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 起動時の「キャッシュ優先」を支える共通処理（Docs/GameConst_RemoteConfig_Spec.md §7）。
/// キャッシュがあるときはそれで起動し、最新の配信は裏で取ってキャッシュだけ更新する（適用は次の起動から）。
/// こうすると回線が遅くてもローディングで待たされず、ランの途中で値が変わることもない。
/// </summary>
public static class RemoteConfigRefresh
{
    /// <summary>裏取りのタイムアウト（秒）。起動を待たせないので、起動時の取得より長めでよい。</summary>
    public const int BackgroundTimeoutSec = 15;

    /// <summary>
    /// 裏で取得し、検証に通ったらキャッシュへ保存する。ゲームの値には触らない。
    /// </summary>
    /// <param name="validate">JSON を検証して version を返す。不採用なら -1。ゲームの値を書き換えないこと。</param>
    public static void RefreshCacheInBackground(IRemoteConfigSource source, RemoteConfigCache cache,
        Func<string, int> validate, string tag)
    {
        RunAsync(source, cache, validate, tag).Forget();
    }

    private static async UniTaskVoid RunAsync(IRemoteConfigSource source, RemoteConfigCache cache,
        Func<string, int> validate, string tag)
    {
        try
        {
            // Boot シーンの CancellationToken は TitleScene へ移ると切れるので使わない（タイムアウトで必ず終わる）
            string json = await source.FetchEnvelopeJsonAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(json)) return;
            int version = validate(json);
            if (version < 0)
            {
                Debug.LogWarning($"[{tag}] 裏で取得した配信は検証に通らなかったため、キャッシュを更新しません。");
                return;
            }
            cache.Save(version, json);
            Debug.Log($"[{tag}] 最新の配信（version {version}）をキャッシュしました。次の起動から使います。");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
