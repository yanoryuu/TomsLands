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

    public DungeonName DungeonKey { get; private set; }
    private bool isMaxLevel;

    public Subject<DungeonName> OnLevelUpClicked { get; } = new();
    public Subject<DungeonName> OnSlotSelected   { get; } = new();

    private void Awake()
    {
        levelUpButton.onClick.AddListener(() => OnLevelUpClicked.OnNext(DungeonKey));
        if (selectButton != null)
            selectButton.onClick.AddListener(() => OnSlotSelected.OnNext(DungeonKey));
    }

    public void SetSlot(DungeonLevelUpSlotData data)
    {
        DungeonKey = data.DungeonKey;

        if (dungeonIconImage) dungeonIconImage.sprite = data.Icon;
        if (dungeonNameText)  dungeonNameText.text    = data.DungeonName;
        if (costText)         costText.text           = data.IsMaxLevel ? "MAX" : $"{data.Cost:N0}G";

        UpdateLevel(data.CurrentLevel, data.IsMaxLevel);
        SetAffordable(data.CanAfford);
    }

    public void UpdateLevel(int currentLevel, bool maxLevel)
    {
        isMaxLevel = maxLevel;

        if (currentLevelText)
            currentLevelText.text = $"Lv.{currentLevel}";

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
}
