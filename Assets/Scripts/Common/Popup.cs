using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Popup : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private TextMeshProUGUI cancelButtonText;
    public PopupSizeEnum popupSize;

    public void SetData(PopUpData data)
    {
        if (titleText != null) titleText.text = data.Title ?? "";
        if (messageText != null) messageText.text = data.Message ?? "";
        if (confirmButtonText != null) confirmButtonText.text = data.ConfirmButtonText ?? "OK";
        if (cancelButtonText != null) cancelButtonText.text = data.CancelButtonText ?? "キャンセル";
        // ボタンイベントの登録
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(() =>
            {
                data.OnConfirm?.Invoke();
                Destroy(gameObject); // ポップアップを閉じる
            });
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(() =>
            {
                data.OnCancel?.Invoke();
                Destroy(gameObject); // ポップアップを閉じる
            });
        }
    }
}

