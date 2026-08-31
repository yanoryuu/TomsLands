using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 店の改装（店レベルアップ）画面の View。
/// 現在レベル・解放内容のプレビュー・費用を表示し、改装ボタンを提供する。
/// 各参照は未配線（null）でも動作する。
/// </summary>
public class ShopUpgradeView : MonoBehaviour
{
    [Header("レベル表示")]
    [SerializeField] private TextMeshProUGUI levelText;

    [Header("解放内容プレビュー（現在 → 次レベル）")]
    [SerializeField] private TextMeshProUGUI displayKindsText;
    [SerializeField] private TextMeshProUGUI displayStockText;
    [SerializeField] private TextMeshProUGUI machineSlotsText;

    [Header("費用と操作")]
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Button upgradeButton;
    [SerializeField] private Button closeButton;

    [Header("メッセージ")]
    [SerializeField] private TextMeshProUGUI messageText;

    public Subject<Unit> OnUpgradeClicked { get; } = new();
    public Subject<Unit> OnCloseRequested { get; } = new();

    private void Awake()
    {
        if (upgradeButton != null)
            upgradeButton.onClick.AddListener(() => OnUpgradeClicked.OnNext(Unit.Default));
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
    }

    public void UpdateContent(
        int currentLevel, int maxLevel,
        ShopLevelSettings.ShopLevelEntry current,
        ShopLevelSettings.ShopLevelEntry next,
        int cost, bool canAfford)
    {
        bool isMax = next == null;

        if (levelText != null)
            levelText.text = isMax ? $"店レベル {currentLevel}（MAX）" : $"店レベル {currentLevel} → {currentLevel + 1}";

        if (displayKindsText != null)
            displayKindsText.text = isMax
                ? $"同時陳列 {current.maxDisplayKinds}銘柄"
                : $"同時陳列 {current.maxDisplayKinds} → {next.maxDisplayKinds}銘柄";

        if (displayStockText != null)
            displayStockText.text = isMax
                ? $"陳列個数 {current.maxDisplayStockPerItem}個/銘柄"
                : $"陳列個数 {current.maxDisplayStockPerItem} → {next.maxDisplayStockPerItem}個/銘柄";

        if (machineSlotsText != null)
            machineSlotsText.text = isMax
                ? $"設置枠 {current.machineSlots}"
                : $"設置枠 {current.machineSlots} → {next.machineSlots}";

        if (costText != null)
            costText.text = isMax ? "-" : $"{cost:N0}G";

        if (upgradeButton != null)
            upgradeButton.interactable = !isMax && canAfford;
    }

    public void ShowMessage(string message)
    {
        if (messageText != null)
            messageText.text = message;
    }
}
