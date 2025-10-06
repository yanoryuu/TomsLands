using Cysharp.Threading.Tasks;
using UnityEngine;

public class EventTestRunner : MonoBehaviour
{
    [SerializeField] private EventDialogView _dialogPrefab;
    
    // ↓↓↓ この1行を追加 ↓↓↓
    [SerializeField] private Transform _canvasTransform; // UIの親となるCanvas

    private bool _isDialogActive = false;

    // (Updateメソッドは変更なし)
    void Update()
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

        // ↓↓↓ Instantiateの行をこのように変更します ↓↓↓
        // 変更前：
        // var view = Instantiate(_dialogPrefab);
        // 変更後：
        var view = Instantiate(_dialogPrefab, _canvasTransform);

        var model = new EventDialogModel("エンターキーでイベントが発生しました。\nどちらかのボタンを押してください。");
        var presenter = new EventDialogPresenter(view, model);

        // (以降の処理は変更なし)
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