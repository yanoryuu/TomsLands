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
        // 開く演出（モーダルはフェード＋小さなポップで揃える）
        UIFx.PanelOpen(gameObject, 0.15f);
        UIFx.Pop(transform, 0.92f, 0.2f);
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
                closeButton.onClick.AddListener(() => { LockButtons(); data.OnConfirm?.Invoke(); });
            }
        }
        else
        {
            if (closeButton != null) closeButton.gameObject.SetActive(false);
            if (confirmButtonText != null) confirmButtonText.text = data.ConfirmButtonText ?? "OK";
            if (cancelButtonText != null) cancelButtonText.text = data.CancelButtonText ?? "キャンセル";
            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(() =>
                {
                    // 閉じない確定（体験版の「ストアページへ」）はボタンを止めない
                    if (!data.KeepOpenOnConfirm) LockButtons();
                    data.OnConfirm?.Invoke();
                });
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(() => { LockButtons(); data.OnCancel?.Invoke(); });
            }
        }

        // 確認/キャンセルは左右で互いに移る（Automatic だと背面画面のボタンを拾い、モーダルに戻されて動かなくなる）
        if (!data.IsCloseOnly && confirmButton != null && cancelButton != null)
        {
            confirmButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = cancelButton,
                selectOnRight = cancelButton,
            };
            cancelButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = confirmButton,
                selectOnRight = confirmButton,
            };
        }

        // パッド/キー操作: 確認系はキャンセル側を初期選択にする（決定の連打で確定してしまう誤爆を防ぐ）
        var focus = data.IsCloseOnly
            ? closeButton
            : (cancelButton != null && cancelButton.gameObject.activeInHierarchy ? cancelButton : confirmButton);
        if (focus != null && focus.gameObject.activeInHierarchy && ControllerInput.IsNavigating.CurrentValue)
            UIFocusManager.Select(focus.gameObject);
    }

    /// <summary>押した瞬間に全ボタンを押せなくする（閉じるまでの数フレームに2つ目の押下が通らないように）。</summary>
    private void LockButtons()
    {
        if (confirmButton != null) confirmButton.interactable = false;
        if (cancelButton != null) cancelButton.interactable = false;
        if (closeButton != null) closeButton.interactable = false;
    }
}

