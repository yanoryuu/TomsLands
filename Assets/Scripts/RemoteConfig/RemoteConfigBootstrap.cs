using System.Threading;
using Cysharp.Threading.Tasks;
using VContainer.Unity;

/// <summary>
/// 起動シーケンスで RemoteConfigService を駆動する。
/// IAsyncStartable により、GameConst 初回アクセス前に適用を完了させる。
///
/// 有効化するには LifetimeScope で URL を指定して登録する（実URL/環境が未確定のため
/// 既定では未登録）。例:
/// <code>
/// protected override void Configure(IContainerBuilder builder)
/// {
///     const string url = "https://&lt;your-cdn&gt;/config/production/gameconst.json";
///     builder.Register&lt;IRemoteConfigSource&gt;(_ =&gt; new HttpRemoteConfigSource(url), Lifetime.Singleton);
///     builder.Register&lt;RemoteConfigCache&gt;(Lifetime.Singleton);
///     builder.Register&lt;RemoteConfigService&gt;(Lifetime.Singleton);
///     builder.RegisterEntryPoint&lt;RemoteConfigBootstrap&gt;();
/// }
/// </code>
/// </summary>
public sealed class RemoteConfigBootstrap : IAsyncStartable
{
    private readonly RemoteConfigService _service;

    public RemoteConfigBootstrap(RemoteConfigService service) => _service = service;

    public async UniTask StartAsync(CancellationToken ct)
    {
        await _service.InitializeAsync(ct);
        // ここから先（ゲーム本編シーン遷移など）で初めて GameConst を参照する。
    }
}
