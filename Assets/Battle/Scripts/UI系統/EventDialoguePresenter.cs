using Cysharp.Threading.Tasks;
using R3;
using System;

public enum DialogResult
{
    Confirm, // はい
    Cancel   // いいえ
}

public class EventDialogPresenter : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly UniTaskCompletionSource<DialogResult> _uts = new();

    public EventDialogPresenter(EventDialogView view, EventDialogModel model)
    {
        // 1. Modelのデータを使ってViewの表示を更新
        view.SetContent(model.Body);

        // 2. ViewのボタンクリックをSubscribe(購読)する
        view.OnConfirmClicked
            .Subscribe(_ =>
            {
                // UniTaskに「はいが押された」という結果を設定
                _uts.TrySetResult(DialogResult.Confirm);
            })
            .AddTo(_disposables);

        view.OnCancelClicked
            .Subscribe(_ =>
            {
                // UniTaskに「いいえが押された」という結果を設定
                _uts.TrySetResult(DialogResult.Cancel);
            })
            .AddTo(_disposables);

        // 3. 画面を表示する
        view.Show();
    }

    // 外部からユーザーの選択を待つためのUniTask
    public UniTask<DialogResult> WaitForResultAsync() => _uts.Task;

    public void Dispose()
    {
        // 購読をすべて解除
        _disposables.Dispose();
    }
}