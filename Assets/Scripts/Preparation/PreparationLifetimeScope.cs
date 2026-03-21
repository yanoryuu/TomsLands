using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 準備シーン（Preparation.unity）用のLifetimeScope。
/// 将来的に準備フェーズの機能を追加する場所。
/// 現在はPreparationPresenterがTomsShopへ即座に遷移する。
/// </summary>
public class PreparationLifetimeScope : LifetimeScope
{
    [Header("Views")]
    [SerializeField] private PreparationView preparationView;

    protected override void Configure(IContainerBuilder builder)
    {
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

