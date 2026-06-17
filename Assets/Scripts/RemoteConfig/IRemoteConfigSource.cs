using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>リモートコンフィグの取得元の抽象。HTTP / Mock を差し替え可能にする。</summary>
public interface IRemoteConfigSource
{
    /// <summary>軽量な version だけを取得する（差分検知用）。失敗時は -1。</summary>
    UniTask<int> FetchVersionAsync(CancellationToken ct);

    /// <summary>エンベロープ JSON 本体を取得する。失敗時は null。</summary>
    UniTask<string> FetchEnvelopeJsonAsync(CancellationToken ct);
}
