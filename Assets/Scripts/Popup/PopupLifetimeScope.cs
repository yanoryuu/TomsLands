using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// PopupScene 用の LifetimeScope。
/// Additive ロードされるため、呼び出し元シーンの LifetimeScope を親として使用する。
/// 親コンテナから PopUpManager を解決し、PopUpView に注入する。
/// </summary>
public class PopupLifetimeScope : LifetimeScope
{
    [Header("Scene References")]
    [SerializeField] private PopUpView popUpView;
    protected override void Configure(IContainerBuilder builder)
    {
        // View の登録
        if (popUpView != null)
        {
            builder.RegisterComponent(popUpView);
        }
        else
        {
            Debug.LogError("[PopupLifetimeScope] popUpView が Inspector で未設定です！");
        }

        Debug.Log("[PopupLifetimeScope] Configured.");
    }
}

