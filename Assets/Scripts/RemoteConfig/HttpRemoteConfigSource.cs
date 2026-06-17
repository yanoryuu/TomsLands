using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// HTTP(S) でエンベロープ JSON を取得する <see cref="IRemoteConfigSource"/> 実装。
/// 静的JSON配信（CDN/Storage）を想定。失敗時は例外を投げずフォールバック可能な値を返す。
/// </summary>
public sealed class HttpRemoteConfigSource : IRemoteConfigSource
{
    private readonly string _envelopeUrl;
    private readonly int _timeoutSec;

    public HttpRemoteConfigSource(string envelopeUrl, int timeoutSec = 10)
    {
        _envelopeUrl = envelopeUrl;
        _timeoutSec = timeoutSec;
    }

    public async UniTask<int> FetchVersionAsync(CancellationToken ct)
    {
        // 初版は本体を取得して version を読む実装でも可。
        // ETag 運用に移行する場合はここを HEAD + If-None-Match に差し替える。
        var json = await FetchEnvelopeJsonAsync(ct);
        if (string.IsNullOrEmpty(json)) return -1;
        try
        {
            var env = JsonUtility.FromJson<GameConstEnvelope>(json);
            return env?.version ?? -1;
        }
        catch { return -1; }
    }

    public async UniTask<string> FetchEnvelopeJsonAsync(CancellationToken ct)
    {
        using var req = UnityWebRequest.Get(_envelopeUrl);
        req.timeout = _timeoutSec;
        try
        {
            await req.SendWebRequest().WithCancellation(ct);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RemoteConfig] 取得失敗: {e.Message}");
            return null;
        }
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[RemoteConfig] HTTP エラー: {req.result} / {req.error}");
            return null;
        }
        return req.downloadHandler.text;
    }
}
