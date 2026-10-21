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
        var battleInputData = AddressableLoader.Load<BattleInputData>("SceneData/BattleInputData");
        if (battleInputData == null) battleInputData = ScriptableObject.CreateInstance<BattleInputData>();
        var battleOutputData = AddressableLoader.Load<BattleOutputData>("SceneData/BattleOutputData");
        if (battleOutputData == null) battleOutputData = ScriptableObject.CreateInstance<BattleOutputData>();
        builder.RegisterInstance(battleInputData);
        builder.RegisterInstance(battleOutputData);

        // 金融資産（債券・ファンド）を純資産に算入するための PortfolioModel 依存
        var financeSettings = AddressableLoader.Load<FinanceSettings>("FinanceSettings");
        if (financeSettings == null) financeSettings = ScriptableObject.CreateInstance<FinanceSettings>();
        financeSettings = RemoteBalance.ApplyOverwrite("finance", financeSettings);
        builder.RegisterInstance(financeSettings);

        var financialProducts = AddressableLoader.LoadAll<FinancialProductData>("FinancialProductData");
        financialProducts = RemoteBalance.ApplyList("financialProducts", financialProducts, p => p.productId);
        builder.RegisterInstance(financialProducts);

        // --- 2. Models ---
        // TomsModel / ItemModel / PortfolioModel はコンストラクタでセーブデータを自動ロードする
        builder.Register<TomsModel>(Lifetime.Singleton);
        builder.Register<ItemModel>(Lifetime.Singleton);
        builder.Register<PortfolioModel>(Lifetime.Singleton);
        builder.Register<ResultModel>(Lifetime.Singleton);
        builder.Register<MetaProgressModel>(Lifetime.Singleton);

        // --- 3. Services ---
        builder.Register<SceneTransitionService>(Lifetime.Singleton);
        // ポップアップ（体験版の「製品版へ」で使う。製品版では呼ばない）
        builder.Register<PopUpManager>(Lifetime.Singleton)
            .WithParameter(typeof(LifetimeScope), this);

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // デバッグメニュー（F11で開閉、リリースビルドには含まれない）
        builder.RegisterComponentOnNewGameObject<DebugMenuView>(Lifetime.Singleton, "DebugMenu");
        builder.RegisterBuildCallback(container => container.Resolve<DebugMenuView>());
#endif

        Debug.Log("[ResultLifetimeScope] Configured.");
    }
}

