using Cysharp.Threading.Tasks;
using UnityEngine;

public class EventTestRunner : MonoBehaviour
{
    [SerializeField] private EventDialogView _dialogPrefab;
    [SerializeField] private Transform _canvasTransform; // UIの親となるCanvas

    private bool _isDialogActive;

    private void Update()
    {
        if (!_isDialogActive && Input.GetKeyDown(KeyCode.Return))
        {
            ShowDialogFlowAsync().Forget();
        }
    }

    private async UniTask ShowDialogFlowAsync()
    {
        _isDialogActive = true;
        Debug.Log("【テスト】エンターキーが押されました。ダイアログを表示します...");

        var view = Instantiate(_dialogPrefab, _canvasTransform);

        var model = new EventDialogModel("エンターキーでイベントが発生しました。\nどちらかのボタンを押してください。");
        var presenter = new EventDialogPresenter(view, model);

        var result = await presenter.WaitForResultAsync();
        if (result == DialogResult.Confirm)
        {
            Debug.Log("【結果】「はい」が選択されました！");
        }
        else
        {
            Debug.Log("【結果】「いいえ」が選択されました。");
        }
        presenter.Dispose();
        Destroy(view.gameObject);
        _isDialogActive = false;
    }
}