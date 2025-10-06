using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventDialogView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _bodyText;
    [SerializeField] private Button _confirmButton; // 「はい」ボタン
    [SerializeField] private Button _cancelButton;  // 「いいえ」ボタン

    public Subject<Unit> OnConfirmClicked { get; private set; } = new();
    public Subject<Unit> OnCancelClicked { get; private set; } = new();

    private void Awake()
    {
        // ButtonのクリックイベントとSubjectを結びつける
        _confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
        _cancelButton.onClick.AddListener(() => OnCancelClicked.OnNext(Unit.Default));
    }

    // このViewが破棄される時にSubjectを閉じる（メモリリーク防止）
    private void OnDestroy()
    {
        OnConfirmClicked.OnCompleted();
        OnCancelClicked.OnCompleted();
    }

    // Presenterから呼ばれて表示を更新する
    public void SetContent(string body)
    {
        _bodyText.text = body;
    }

    public void Show() => gameObject.SetActive(true);
    public void Hide() => gameObject.SetActive(false);
}