using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ブート(ローディング)シーン用の LifetimeScope。
/// リモートコンフィグ（GameConst調整値・アイテムマスター上書き）を取得して適用し、
/// 完了後に TitleScene へ遷移する。このシーンを Build Settings の先頭（index 0）に置くこと。
/// ローディング画面は SceneLoader の常駐オーバーレイ（Resources/LoadingOverlay.prefab）を使うので、シーン側の配線は不要。
/// </summary>
public class BootLifetimeScope : LifetimeScope
{
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
    [Tooltip("取得タイムアウト（秒）。キャッシュが無い初回起動だけこの時間まで待つ（キャッシュがあれば待たない）")]
    [SerializeField] private int remoteConfigTimeoutSec = 3;

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
            return new RemoteConfigService(src, new RemoteConfigCache(), BackgroundSource(enabled, gcUrl));
        }, Lifetime.Singleton);

        // アイテムマスター上書き
        builder.Register<ItemMasterService>(_ =>
        {
            IRemoteConfigSource src = (enabled && !string.IsNullOrEmpty(itemUrl))
                ? new HttpRemoteConfigSource(itemUrl, timeout)
                : new NullRemoteConfigSource();
            return new ItemMasterService(src, new RemoteConfigCache("items"), BackgroundSource(enabled, itemUrl));
        }, Lifetime.Singleton);

        // バランス調整（結合）
        builder.Register<RemoteBalanceService>(_ =>
        {
            IRemoteConfigSource src = (enabled && !string.IsNullOrEmpty(balUrl))
                ? new HttpRemoteConfigSource(balUrl, timeout)
                : new NullRemoteConfigSource();
            return new RemoteBalanceService(src, new RemoteConfigCache("balance"), BackgroundSource(enabled, balUrl));
        }, Lifetime.Singleton);

        builder.RegisterEntryPoint<LoadingPresenter>();

        Debug.Log("[BootLifetimeScope] Configured.");
    }

    /// <summary>キャッシュで起動したあと裏で最新を取る取得元。起動を待たせないのでタイムアウトは長め。</summary>
    private static IRemoteConfigSource BackgroundSource(bool enabled, string url) =>
        enabled && !string.IsNullOrEmpty(url)
            ? new HttpRemoteConfigSource(url, RemoteConfigRefresh.BackgroundTimeoutSec)
            : new NullRemoteConfigSource();
}
