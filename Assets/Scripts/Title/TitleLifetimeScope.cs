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

    protected override void Configure(IContainerBuilder builder)
    {
        // StartModeData（シーン間共有データ）のロードと登録
        var startModeData = Resources.Load<StartModeData>("SceneData/StartModeData");
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

        Debug.Log("[TitleLifetimeScope] Configured.");
    }
}

