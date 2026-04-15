using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// ItemPopupScene 用の LifetimeScope。
/// Additive ロードされるため、呼び出し元シーンの LifetimeScope を親として使用する。
/// 親コンテナから ItemPopUpManager を解決し、ItemPopUpView に注入する。
/// </summary>
public class ItemPopupLifetimeScope : LifetimeScope
{
    [Header("Scene References")]
    [SerializeField] private ItemPopUpView itemPopUpView;

    protected override void Configure(IContainerBuilder builder)
    {
        // View の登録
        if (itemPopUpView != null)
        {
            builder.RegisterComponent(itemPopUpView);
        }
        else
        {
            Debug.LogError("[ItemPopupLifetimeScope] itemPopUpView が Inspector で未設定です！");
        }

        Debug.Log("[ItemPopupLifetimeScope] Configured.");
    }
}

