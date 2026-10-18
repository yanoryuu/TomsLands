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
        view.SetContent(model.Body);

        view.OnConfirmClicked
            .Subscribe(_ => _uts.TrySetResult(DialogResult.Confirm))
            .AddTo(_disposables);

        view.OnCancelClicked
            .Subscribe(_ => _uts.TrySetResult(DialogResult.Cancel))
            .AddTo(_disposables);

        view.Show();
    }

    // 外部からユーザーの選択を待つためのUniTask
    public UniTask<DialogResult> WaitForResultAsync() => _uts.Task;

    public void Dispose() => _disposables.Dispose();
}