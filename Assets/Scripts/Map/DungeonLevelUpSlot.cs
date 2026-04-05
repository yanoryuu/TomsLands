using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ダンジョンレベルアップ画面の1行スロット。
/// ダンジョン名・レベル・コスト・レベルアップボタンを表示する。
/// </summary>
public class DungeonLevelUpSlot : MonoBehaviour
{
    [SerializeField] private Image dungeonIconImage;
    [SerializeField] private TextMeshProUGUI dungeonNameText;
    [SerializeField] private TextMeshProUGUI currentLevelText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Button levelUpButton;
    [SerializeField] private TextMeshProUGUI levelUpButtonText;

    /// <summary>このスロットが保持するダンジョンキー</summary>
    public DungeonName DungeonKey { get; private set; }

    /// <summary>レベルアップボタンが押された時に発火（DungeonNameを通知）</summary>
    public Subject<DungeonName> OnLevelUpClicked { get; } = new();

    private void Awake()
    {
        levelUpButton.onClick.AddListener(() => OnLevelUpClicked.OnNext(DungeonKey));
    }

    /// <summary>
    /// スロットの表示を設定する
    /// </summary>
    public void SetSlot(DungeonName key, string dungeonName, Sprite icon, int currentLevel, int cost, bool isMaxLevel)
    {
        DungeonKey = key;

        if (dungeonIconImage) dungeonIconImage.sprite = icon;
        if (dungeonNameText) dungeonNameText.text = dungeonName;

        UpdateLevel(currentLevel, cost, isMaxLevel);
    }

    /// <summary>
    /// レベルとコスト表示を更新する
    /// </summary>
    public void UpdateLevel(int currentLevel, int cost, bool isMaxLevel)
    {
        if (currentLevelText) currentLevelText.text = $"Lv.{currentLevel}";

        if (isMaxLevel)
        {
            if (costText) costText.text = "MAX";
            if (levelUpButtonText) levelUpButtonText.text = "MAX";
            levelUpButton.interactable = false;
        }
        else
        {
            if (costText) costText.text = $"{cost}G";
            if (levelUpButtonText) levelUpButtonText.text = "レベルアップ";
            levelUpButton.interactable = true;
        }
    }

    /// <summary>
    /// 資金不足時にボタンを無効化する
    /// </summary>
    public void SetAffordable(bool canAfford)
    {
        // MAX時は常に無効
        if (!levelUpButton.interactable) return;
        levelUpButton.interactable = canAfford;
    }
}

