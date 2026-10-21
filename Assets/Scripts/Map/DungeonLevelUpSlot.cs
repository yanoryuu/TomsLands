using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DungeonLevelUpSlot : MonoBehaviour
{
    [SerializeField] private Image             dungeonIconImage;
    [SerializeField] private TextMeshProUGUI   dungeonNameText;
    [SerializeField] private TextMeshProUGUI   currentLevelText;
    [SerializeField] private TextMeshProUGUI   costText;
    [SerializeField] private Button            selectButton;
    [SerializeField] private Button            levelUpButton;
    [SerializeField] private TextMeshProUGUI   levelUpButtonText;
    [Tooltip("次の配信先の印（地図の NEXT リボンと同じ見た目）。未配線なら名前の前に NEXT と付ける")]
    [SerializeField] private GameObject        nextBadge;

    public DungeonName DungeonKey { get; private set; }
    private bool isMaxLevel;

    public Subject<DungeonName> OnLevelUpClicked { get; } = new();
    public Subject<DungeonName> OnSlotSelected   { get; } = new();

    private void Awake()
    {
        levelUpButton.onClick.AddListener(() => OnLevelUpClicked.OnNext(DungeonKey));
        if (selectButton != null)
            selectButton.onClick.AddListener(() => OnSlotSelected.OnNext(DungeonKey));

        // パッド/キー: 行（アイコン or レベルアップ）にフォーカスが来たら詳細を表示（クリック選択と同じ）
        if (selectButton != null)
            ControllerSelectRelay.GetOrAdd(selectButton).Selected += NotifySelectedByNav;
        if (levelUpButton != null)
            ControllerSelectRelay.GetOrAdd(levelUpButton).Selected += NotifySelectedByNav;
    }

    private void NotifySelectedByNav() => OnSlotSelected.OnNext(DungeonKey);

    /// <summary>パッド/キー用のフォーカス先（行の選択ボタン）。</summary>
    public Selectable SelectButton => selectButton;

    /// <summary>パッド/キー用のフォーカス先（レベルアップボタン）。</summary>
    public Selectable LevelUpButton => levelUpButton;

    public void SetSlot(DungeonLevelUpSlotData data)
    {
        DungeonKey = data.DungeonKey;

        if (dungeonIconImage) dungeonIconImage.sprite = data.Icon;
        if (nextBadge) nextBadge.SetActive(data.IsNext);
        if (dungeonNameText)
            dungeonNameText.text = data.IsNext && nextBadge == null
                ? $"<color=#E8A33D>次</color> {data.DungeonName}"
                : data.DungeonName;
        if (costText)         costText.text           = data.IsMaxLevel ? "MAX" : $"{data.Cost:N0}G";

        UpdateLevel(data.CurrentLevel, data.IsMaxLevel);
        SetAffordable(data.CanAfford);
    }

    public void UpdateLevel(int currentLevel, bool maxLevel)
    {
        isMaxLevel = maxLevel;

        if (currentLevelText)
            currentLevelText.text = DungeonLevels.Label(currentLevel);

        if (isMaxLevel)
        {
            if (levelUpButtonText) levelUpButtonText.text = "MAX";
            if (levelUpButton)     levelUpButton.interactable = false;
        }
        else
        {
            if (levelUpButtonText) levelUpButtonText.text = "レベルアップ";
            if (levelUpButton)     levelUpButton.interactable = true;
        }
    }

    public void SetAffordable(bool canAfford)
    {
        if (levelUpButton == null || isMaxLevel) return;
        levelUpButton.interactable = canAfford;
    }
}

public class DungeonLevelUpSlotData
{
    public DungeonName DungeonKey;
    public string      DungeonName;
    public Sprite      Icon;
    public int         CurrentLevel;
    public int         NextLevel;
    public int         Cost;
    public int         Shortage;
    public bool        CanAfford;
    public bool        IsMaxLevel;
    /// <summary>次の配信先か（一覧の先頭に置き、NEXT の印を付ける）。</summary>
    public bool        IsNext;
}
