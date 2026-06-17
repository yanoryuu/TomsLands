using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// タイトルシーン（Start.unity）用のLifetimeScope。
/// TitleViewとStartModeDataを登録し、TitlePresenterをエントリーポイントとして起動する。
/// </summary>
public class TitleLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private TitleView titleView;

    [Header("リモートコンフィグ")]
    [Tooltip("OFFにするとサーバー取得をスキップし、ベイク済み GameConstSettings を使う")]
    [SerializeField] private bool enableRemoteConfig = true;
    [Tooltip("配信エンベロープJSONの公開URL")]
    [SerializeField] private string remoteConfigUrl =
        "https://storage.googleapis.com/tokotomland.firebasestorage.app/config/production/gameconst.json";
    [Tooltip("取得タイムアウト（秒）")]
    [SerializeField] private int remoteConfigTimeoutSec = 10;

    protected override void Configure(IContainerBuilder builder)
    {
        // StartModeData（シーン間共有データ）のロードと登録
        var startModeData = AddressableLoader.Load<StartModeData>("SceneData/StartModeData");
        if (startModeData == null)
        {
            startModeData = ScriptableObject.CreateInstance<StartModeData>();
            Debug.LogWarning("[TitleLifetimeScope] Resources/SceneData/StartModeData.asset が見つかりません。" +
                             "Tools > Create Scene Data Assets を実行してください。");
        }
        builder.RegisterInstance(startModeData);

        // View
        if (titleView != null)
        {
            builder.RegisterComponent(titleView);
        }
        else
        {
            Debug.LogError("[TitleLifetimeScope] titleView が Inspector で未設定です！");
        }

        // Presenter
        builder.RegisterEntryPoint<TitlePresenter>();

        // リモートコンフィグ（起動時にタイトルで取得→GameConstへ適用。本編シーンの GameConst 参照前に完了させる）
        if (enableRemoteConfig && !string.IsNullOrEmpty(remoteConfigUrl))
        {
            string url = remoteConfigUrl;
            int timeout = remoteConfigTimeoutSec;
            builder.Register<IRemoteConfigSource>(_ => new HttpRemoteConfigSource(url, timeout), Lifetime.Singleton);
            builder.Register<RemoteConfigCache>(Lifetime.Singleton);
            builder.Register<RemoteConfigService>(Lifetime.Singleton);
            builder.RegisterEntryPoint<RemoteConfigBootstrap>();
        }
        else
        {
            Debug.Log("[TitleLifetimeScope] リモートコンフィグ無効。ベイク済みデフォルトを使用します。");
        }

        Debug.Log("[TitleLifetimeScope] Configured.");
    }
}

