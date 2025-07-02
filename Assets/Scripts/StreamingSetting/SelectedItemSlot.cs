using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 左パネルの選択済みアイテムスロット。数量変更と削除ボタンを持つ。
/// </summary>
public class SelectedItemSlot : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private Text nameText;
    [SerializeField] private InputField qtyInput;
    [SerializeField] private Button removeButton;

    private string _itemId;
    public event Action<string, int> OnQuantityChanged;
    public event Action<string> OnRemoved;

    /// <summary>
    /// 初期化メソッド。アイコン、表示名を設定し、数量入力を初期化。
    /// </summary>
    public void Initialize(string itemId, Sprite sprite, string displayName)
    {
        _itemId = itemId;
        icon.sprite = sprite;
        nameText.text = displayName;
        qtyInput.text = "1";
        qtyInput.onEndEdit.AddListener(OnQtyEdited);
        removeButton.onClick.AddListener(() => OnRemoved?.Invoke(_itemId));
    }

    private void OnQtyEdited(string text)
    {
        if (int.TryParse(text, out var q) && q > 0)
        {
            OnQuantityChanged?.Invoke(_itemId, q);
        }
        else
        {
            qtyInput.text = "1";
        }
    }
}