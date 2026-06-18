using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ブート(ローディング)シーン用の LifetimeScope。
/// リモートコンフィグを取得して GameConst に適用し、完了後に TitleScene へ遷移する。
/// このシーンを Build Settings の先頭（index 0）に置くこと。
/// </summary>
public class BootLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private LoadingView loadingView;

    [Header("リモートコンフィグ")]
    [Tooltip("OFFにするとサーバー取得をスキップし、キャッシュ/ベイク済みデフォルトで起動する")]
    [SerializeField] private bool enableRemoteConfig = true;
    [Tooltip("配信エンベロープJSONの公開URL")]
    [SerializeField] private string remoteConfigUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/gameconst.json";
    [Tooltip("取得タイムアウト（秒）")]
    [SerializeField] private int remoteConfigTimeoutSec = 10;

    protected override void Configure(IContainerBuilder builder)
    {
        // 取得元（有効/無効で切替）
        if (enableRemoteConfig && !string.IsNullOrEmpty(remoteConfigUrl))
        {
            string url = remoteConfigUrl;
            int timeout = remoteConfigTimeoutSec;
            builder.Register<IRemoteConfigSource>(_ => new HttpRemoteConfigSource(url, timeout), Lifetime.Singleton);
        }
        else
        {
            builder.Register<IRemoteConfigSource>(_ => new NullRemoteConfigSource(), Lifetime.Singleton);
            Debug.Log("[BootLifetimeScope] リモートコンフィグ無効。キャッシュ/デフォルトで起動します。");
        }

        builder.Register<RemoteConfigCache>(Lifetime.Singleton);
        builder.Register<RemoteConfigService>(Lifetime.Singleton);

        // LoadingView は任意。未設定(null)でも遷移できるよう WithParameter で渡す（RegisterComponentはnull不可のため）。
        if (loadingView == null)
            Debug.LogWarning("[BootLifetimeScope] loadingView 未設定。演出なしで TitleScene へ遷移します。");

        builder.RegisterEntryPoint<LoadingPresenter>()
            .WithParameter(typeof(LoadingView), loadingView);

        Debug.Log("[BootLifetimeScope] Configured.");
    }
}
