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
    [Tooltip("「ふつう　5日目　1,200G」などのサマリ")]
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

    /// <summary>スロット本体の選択ボタン（パッド/キーのフォーカス先）。</summary>
    public Button SelectButton => selectButton;

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() => OnSelect.OnNext(_slotIndex));
        if (deleteButton != null)
            deleteButton.onClick.AddListener(() => OnDelete.OnNext(_slotIndex));
        // パッド/キー: スロットの内側にある「削除」へ右で移れるようにする
        if (selectButton != null && deleteButton != null) InnerSelectableNavigation.Attach(selectButton, deleteButton);
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

        // 表示は4通り: 進行中のラン / 村の進行だけ（ラン終了後）/ 壊れている / 空き。
        // 「データあり」の判定は HasProfile（村の進行も含む）。SaveSlotInfo.Exists は「進行中のラン」の意味のまま
        bool hasData = info.HasProfile || info.IsCorrupted;
        if (emptyLabel != null) emptyLabel.SetActive(!hasData);
        if (filledGroup != null) filledGroup.SetActive(hasData);

        if (summaryText != null)
            summaryText.text = BuildSummary(info);

        // 削除はデータがあるとき（村の進行だけ・壊れている場合を含む）に可能
        if (deleteButton != null)
            deleteButton.interactable = hasData;
    }

    private static string BuildSummary(SaveSlotInfo info)
    {
        if (info.Exists)
            return $"{GetModeLabel(info.Mode)}　{DayText.DayOf(info.Day)}　{info.Gold:N0}G";
        if (info.HasProfile)
        {
            // 進行中のランの行（難易度・日数・所持金）と同じくらいの長さに収める
            return $"村の進行あり　村資金 {info.VillageFunds:N0}G　{info.TotalRuns}周";
        }
        if (info.IsCorrupted)
            return "データが壊れています（削除できます）";
        return string.Empty;
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

    public static string GetModeLabel(GameModeId mode) => mode switch
    {
        GameModeId.Short => "かんたん",
        GameModeId.Medium => "ふつう",
        GameModeId.Long => "むずかしい",
        _ => mode.ToString()
    };
}
