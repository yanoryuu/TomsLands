using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// リモートコンフィグを無効化したい時に使うソース。常に取得失敗（null/-1）を返し、
/// RemoteConfigService をキャッシュ→ベイク済みデフォルトのフォールバックへ進ませる。
/// </summary>
public sealed class NullRemoteConfigSource : IRemoteConfigSource
{
    public UniTask<int> FetchVersionAsync(CancellationToken ct) => UniTask.FromResult(-1);
    public UniTask<string> FetchEnvelopeJsonAsync(CancellationToken ct) => UniTask.FromResult<string>(null);
}
