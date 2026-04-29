using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// アイテム市場分析ポップアップの表示・非表示を管理する。
/// PopUpManager と同じ Additive シーンロード方式。
/// GameLifetimeScope で Singleton 登録して使う。
/// </summary>
public class ItemPopUpManager
{
    private readonly LifetimeScope parentScope;
    private System.IDisposable parentScopeRegistration;

    public ItemPopUpManager(LifetimeScope parentScope)
    {
        this.parentScope = parentScope;
    }
    /// <summary>
    /// ポップアップシーンに渡すデータを一時保持するプロパティ。
    /// シーンロード完了後に ItemPopUpView がここからデータを読み取る。
    /// </summary>
    public ItemPopUpData CurrentData { get; private set; }

    private const string SceneName = "ItemPopupScene";
    private bool isLoading;

    /// <summary>
    /// アイテム市場分析ポップアップを表示する。
    /// </summary>
    public void Show(ItemPopUpData data)
    {
        if (isLoading) return;

        // 確認ボタンのコールバックをラップし、実行後に自動で Close() を呼ぶ
        var originalOnConfirm = data.OnConfirm;
        data.OnConfirm = () =>
        {
            originalOnConfirm?.Invoke();
            Close();
        };

        CurrentData = data;
        isLoading = true;

        // 子シーンの LifetimeScope が親コンテナを参照できるようにする
        parentScopeRegistration?.Dispose();
        parentScopeRegistration = LifetimeScope.EnqueueParent(parentScope);

        var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        if (op != null)
        {
            op.completed += _ =>
            {
                parentScopeRegistration?.Dispose();
                parentScopeRegistration = null;
                isLoading = false;
            };
        }
        else
        {
            parentScopeRegistration?.Dispose();
            parentScopeRegistration = null;
            isLoading = false;
        }
    }

    /// <summary>
    /// ポップアップを閉じる（シーンをアンロード）。
    /// </summary>
    public void Close()
    {
        parentScopeRegistration?.Dispose();
        parentScopeRegistration = null;
        isLoading = false;
        CurrentData = null;
        SceneManager.UnloadSceneAsync(SceneName);
    }
}
