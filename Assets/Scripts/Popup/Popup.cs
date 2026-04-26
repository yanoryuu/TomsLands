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
    [SerializeField] private Button closeButton;
    [SerializeField] private TextMeshProUGUI closeButtonText;
    public PopupSizeEnum popupSize;

    public void SetData(PopUpData data)
    {
        gameObject.SetActive(true);
        if (titleText != null) titleText.text = data.Title ?? "";
        if (messageText != null) messageText.text = data.Message ?? "";

        if (data.IsCloseOnly)
        {
            if (confirmButton != null) confirmButton.gameObject.SetActive(false);
            if (cancelButton != null) cancelButton.gameObject.SetActive(false);
            if (closeButtonText != null) closeButtonText.text = data.ConfirmButtonText ?? "閉じる";
            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(true);
                closeButton.onClick.AddListener(() => data.OnConfirm?.Invoke());
            }
        }
        else
        {
            if (closeButton != null) closeButton.gameObject.SetActive(false);
            if (confirmButtonText != null) confirmButtonText.text = data.ConfirmButtonText ?? "OK";
            if (cancelButtonText != null) cancelButtonText.text = data.CancelButtonText ?? "キャンセル";
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(() => data.OnConfirm?.Invoke());
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(() => data.OnCancel?.Invoke());
            }
        }
    }
}

