using System.Linq;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ResultScene 用の LifetimeScope。
/// セーブデータから TomsModel / ItemModel を復元し、リザルト画面を表示する。
/// </summary>
public class ResultLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private ResultView resultView;

    protected override void Configure(IContainerBuilder builder)
    {
        // --- 1. Infrastructure / Data Setup ---

        // マスターデータのロードと登録（ItemModel 構築に必要）。スプレッドシート由来の上書きを適用。
        var masterItems = ItemMaster.ApplyOverrides(AddressableLoader.LoadAll<ItemData>("ItemData"));
        builder.RegisterInstance(masterItems);

        // ItemVisualSettings のロードと登録
        var itemVisualSettings = AddressableLoader.Load<ItemVisualSettings>("ItemVisualSettings");
        if (itemVisualSettings == null)
        {
            itemVisualSettings = ScriptableObject.CreateInstance<ItemVisualSettings>();
            Debug.LogWarning("[ResultLifetimeScope] Resources/ItemVisualSettings.asset が見つかりません。");
        }
        builder.RegisterInstance(itemVisualSettings);

        // BattleInputData / BattleOutputData（SceneTransitionService の依存解決に必要）
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData")
                              ?? ScriptableObject.CreateInstance<BattleInputData>();
        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData")
                               ?? ScriptableObject.CreateInstance<BattleOutputData>();
        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // --- 2. Models ---
        // TomsModel / ItemModel はコンストラクタでセーブデータを自動ロードする
        builder.Register<TomsModel>(Lifetime.Singleton);
        builder.Register<ItemModel>(Lifetime.Singleton);
        builder.Register<ResultModel>(Lifetime.Singleton);
        builder.Register<MetaProgressModel>(Lifetime.Singleton);

        // --- 3. Services ---
        builder.Register<SceneTransitionService>(Lifetime.Singleton);

        // --- 4. Views ---
        if (resultView != null)
        {
            builder.RegisterComponent(resultView);
        }
        else
        {
            Debug.LogError("[ResultLifetimeScope] resultView が Inspector で未設定です！");
        }

        // --- 5. Presenters (EntryPoints) ---
        builder.RegisterEntryPoint<ResultPresenter>();

        Debug.Log("[ResultLifetimeScope] Configured.");
    }
}

