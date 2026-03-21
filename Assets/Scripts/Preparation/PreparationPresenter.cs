using System;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// 準備シーン（Preparation.unity）のPresenter。
/// 将来的に準備フェーズの機能を実装する場所。
/// 現在は機能なしで、即座にTomsShopシーンへ遷移する。
/// </summary>
public class PreparationPresenter : IStartable, IDisposable
{
    private readonly PreparationView _preparationView;
    private readonly CompositeDisposable _disposables = new();

    public PreparationPresenter(PreparationView preparationView)
    {
        _preparationView = preparationView;
    }

    public void Start()
    {
        // TODO: 将来的に準備フェーズのUI操作をBindする
        // 現在は準備機能なしのため、即座にTomsShopへ遷移
        Debug.Log("[PreparationPresenter] 準備フェーズ（機能未実装）→ TomsShopシーンへ遷移");
        SceneManager.LoadScene("TomsShop");
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

