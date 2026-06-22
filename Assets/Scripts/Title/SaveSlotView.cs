using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// タイトルのロードパネル内の1スロット分のUI。プレハブにアタッチして動的生成する。
/// 選択（ロード/上書き先指定）と削除のイベントを発行する。
/// </summary>
public sealed class SaveSlotView : MonoBehaviour
{
    [Header("操作")]
    [Tooltip("スロット本体の選択ボタン（ロード／上書き先の指定）")]
    [SerializeField] private Button selectButton;
    [Tooltip("このスロットを削除するボタン")]
    [SerializeField] private Button deleteButton;

    [Header("表示")]
    [Tooltip("「スロット 1」などのラベル")]
    [SerializeField] private TextMeshProUGUI slotNumberText;
    [Tooltip("「ふつう  Day 5  1,200 G」などのサマリ")]
    [SerializeField] private TextMeshProUGUI summaryText;
    [Tooltip("空きスロット時に表示するオブジェクト")]
    [SerializeField] private GameObject emptyLabel;
    [Tooltip("データあり時に表示するまとまり（サマリ＋削除ボタンなど）")]
    [SerializeField] private GameObject filledGroup;

    /// <summary>スロット本体が選択されたとき（引数=スロット番号）。</summary>
    public Subject<int> OnSelect { get; } = new();

    /// <summary>削除が要求されたとき（引数=スロット番号）。</summary>
    public Subject<int> OnDelete { get; } = new();

    private int _slotIndex;

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() => OnSelect.OnNext(_slotIndex));
        if (deleteButton != null)
            deleteButton.onClick.AddListener(() => OnDelete.OnNext(_slotIndex));
    }

    private void OnDestroy()
    {
        OnSelect.Dispose();
        OnDelete.Dispose();
    }

    /// <summary>スロット情報を反映する。</summary>
    public void Bind(SaveSlotInfo info)
    {
        _slotIndex = info.SlotIndex;

        if (slotNumberText != null)
            slotNumberText.text = $"スロット {info.SlotIndex + 1}";

        if (emptyLabel != null) emptyLabel.SetActive(!info.Exists);
        if (filledGroup != null) filledGroup.SetActive(info.Exists);

        if (summaryText != null)
        {
            summaryText.text = info.Exists
                ? $"{GetModeLabel(info.Mode)}　Day {info.Day}　{info.Gold:N0} G"
                : string.Empty;
        }

        // 削除はデータがあるときだけ可能
        if (deleteButton != null)
            deleteButton.interactable = info.Exists;
    }

    /// <summary>
    /// 本体ボタンの選択可否を設定する。
    /// ロードモードでは空きスロットを選択不可、ニューゲームのスロット選択では全て選択可。
    /// </summary>
    public void SetSelectable(bool selectable)
    {
        if (selectButton != null)
            selectButton.interactable = selectable;
    }

    private static string GetModeLabel(GameModeId mode) => mode switch
    {
        GameModeId.Short => "かんたん",
        GameModeId.Medium => "ふつう",
        GameModeId.Long => "むずかしい",
        _ => mode.ToString()
    };
}
