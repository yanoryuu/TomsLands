using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ポップアップに渡すデータ。
/// 呼び出し側が生成して PopUpManager.Show() に渡す。
/// </summary>
public class PopUpData
{
    public string Title;
    public string Message;
    public string ConfirmButtonText;
    public string CancelButtonText;
    public Action OnConfirm;
    public Action OnCancel;
    public PopupSizeEnum Size;
    /// <summary>
    /// true のとき「閉じる」ボタンのみ表示し、キャンセルボタンを非表示にする。
    /// </summary>
    public bool IsCloseOnly;
}

public class PopUpManager
{
    /// <summary>
    /// ポップアップシーンに渡すデータを一時保持するプロパティ。
    /// シーンロード完了後に PopUpView がここからデータを読み取る。
    /// </summary>
    public PopUpData CurrentData { get; private set; }

    private const string PopUpSceneName = "PopupScene";
    private bool isLoading;


    /// <summary>
    /// ポップアップを表示する。
    /// 呼び出し側はこのメソッドに PopUpData を渡すだけでよい。
    /// </summary>
    public void Show(PopUpData data)
    {
        if (isLoading) return;

        // 元のコールバックをラップし、実行後に自動で Close() を呼ぶ
        var originalOnConfirm = data.OnConfirm;
        var originalOnCancel  = data.OnCancel;

        data.OnConfirm = () =>
        {
            originalOnConfirm?.Invoke();
            Close();
        };

        data.OnCancel = () =>
        {
            originalOnCancel?.Invoke();
            Close();
        };

        CurrentData = data;
        isLoading = true;

        var op = SceneManager.LoadSceneAsync(PopUpSceneName, LoadSceneMode.Additive);
        if (op != null)
        {
            op.completed += _ => isLoading = false;
        }
    }

    /// <summary>
    /// ポップアップを閉じる（シーンをアンロード）。
    /// </summary>
    public void Close()
    {
        CurrentData = null;
        SceneManager.UnloadSceneAsync(PopUpSceneName);
    }
}
