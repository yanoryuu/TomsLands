using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 準備シーン（PreparationScene.unity）用の LifetimeScope。
/// タイトルの「ニューゲーム」からのみ遷移してくる（続きからは TomsShop 直行）。
/// メタ進行（metaData.json）と RunSetupData（TomsShop への受け渡し）を扱う。
/// </summary>
public class PreparationLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private PreparationView preparationView;

    protected override void Configure(IContainerBuilder builder)
    {
        // アイテムマスター（持ち込みカタログ用）
        var masterItems = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
        builder.RegisterInstance(masterItems);

        // レリックマスター（スターター選択用）
        var relicDefinitions = AddressableLoader.LoadAll<RelicDefinition>("RelicData");
        relicDefinitions = RemoteBalance.ApplyList("relics", relicDefinitions, r => r.relicId);
        builder.RegisterInstance(relicDefinitions);

        // シーン間受け渡しデータ
        var startModeData = AddressableLoader.Load<StartModeData>("SceneData/StartModeData");
        if (startModeData == null)
        {
            startModeData = ScriptableObject.CreateInstance<StartModeData>();
            Debug.LogWarning("[PreparationLifetimeScope] StartModeData.asset が見つかりません。");
        }
        builder.RegisterInstance(startModeData);

        var runSetupData = AddressableLoader.Load<RunSetupData>("SceneData/RunSetupData")
                           ?? RunSetupData.GetOrCreateFallback();
        builder.RegisterInstance(runSetupData);

        // Models
        builder.Register<MetaProgressModel>(Lifetime.Singleton);
        builder.Register<PreparationModel>(Lifetime.Singleton);

        // View
        if (preparationView != null)
        {
            builder.RegisterComponent(preparationView);
        }
        else
        {
            Debug.LogError("[PreparationLifetimeScope] preparationView が Inspector で未設定です！");
        }

        // Presenter
        builder.RegisterEntryPoint<PreparationPresenter>();

        Debug.Log("[PreparationLifetimeScope] Configured.");
    }
}
