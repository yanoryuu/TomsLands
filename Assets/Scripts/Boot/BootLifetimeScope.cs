using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ブート(ローディング)シーン用の LifetimeScope。
/// リモートコンフィグ（GameConst調整値・アイテムマスター上書き）を取得して適用し、
/// 完了後に TitleScene へ遷移する。このシーンを Build Settings の先頭（index 0）に置くこと。
/// </summary>
public class BootLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private LoadingView loadingView;

    [Header("リモートコンフィグ")]
    [Tooltip("OFFにするとサーバー取得をスキップし、キャッシュ/ベイク済みデフォルトで起動する")]
    [SerializeField] private bool enableRemoteConfig = true;
    [Tooltip("GameConst調整値の配信URL")]
    [SerializeField] private string remoteConfigUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/gameconst.json";
    [Tooltip("アイテムマスター上書きの配信URL")]
    [SerializeField] private string itemMasterUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/items.json";
    [Tooltip("バランス調整（結合）の配信URL")]
    [SerializeField] private string balanceUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/balance.json";
    [Tooltip("取得タイムアウト（秒）")]
    [SerializeField] private int remoteConfigTimeoutSec = 10;

    protected override void Configure(IContainerBuilder builder)
    {
        bool enabled = enableRemoteConfig;
        int timeout = remoteConfigTimeoutSec;
        string gcUrl = remoteConfigUrl;
        string itemUrl = itemMasterUrl;
        string balUrl = balanceUrl;

        if (!enabled)
            Debug.Log("[BootLifetimeScope] リモートコンフィグ無効。キャッシュ/デフォルトで起動します。");

        // GameConst 調整値
        builder.Register<RemoteConfigService>(_ =>
        {
            IRemoteConfigSource src = (enabled && !string.IsNullOrEmpty(gcUrl))
                ? new HttpRemoteConfigSource(gcUrl, timeout)
                : new NullRemoteConfigSource();
            return new RemoteConfigService(src, new RemoteConfigCache());
        }, Lifetime.Singleton);

        // アイテムマスター上書き
        builder.Register<ItemMasterService>(_ =>
        {
            IRemoteConfigSource src = (enabled && !string.IsNullOrEmpty(itemUrl))
                ? new HttpRemoteConfigSource(itemUrl, timeout)
                : new NullRemoteConfigSource();
            return new ItemMasterService(src, new RemoteConfigCache("items"));
        }, Lifetime.Singleton);

        // バランス調整（結合）
        builder.Register<RemoteBalanceService>(_ =>
        {
            IRemoteConfigSource src = (enabled && !string.IsNullOrEmpty(balUrl))
                ? new HttpRemoteConfigSource(balUrl, timeout)
                : new NullRemoteConfigSource();
            return new RemoteBalanceService(src, new RemoteConfigCache("balance"));
        }, Lifetime.Singleton);

        // LoadingView は任意。未設定(null)でも遷移できるよう WithParameter で渡す（RegisterComponentはnull不可のため）。
        if (loadingView == null)
            Debug.LogWarning("[BootLifetimeScope] loadingView 未設定。演出なしで TitleScene へ遷移します。");

        builder.RegisterEntryPoint<LoadingPresenter>()
            .WithParameter(typeof(LoadingView), loadingView);

        Debug.Log("[BootLifetimeScope] Configured.");
    }
}
