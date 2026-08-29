using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// マシンショップのカタログ1行（AdvertiseSlot と同型）。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class ShopMachineSlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI effectText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI stateText;    // 設置中 / 店Lv不足 など
    [SerializeField] private Button selectButton;

    public Subject<string> OnSelected { get; } = new();

    public string MachineId { get; private set; }

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(MachineId))
                    OnSelected.OnNext(MachineId);
            });
    }

    public void Setup(ShopMachineData machine, bool placed, bool levelLocked)
    {
        MachineId = machine.machineId;

        if (iconImage != null)
        {
            iconImage.sprite = machine.icon;
            iconImage.enabled = machine.icon != null;
        }
        if (nameText != null) nameText.text = machine.machineName;
        if (effectText != null) effectText.text = machine.EffectSummary;
        if (costText != null) costText.text = $"{machine.cost:N0}G";
        if (stateText != null)
        {
            stateText.text = placed ? "設置中"
                : levelLocked ? $"店Lv{machine.requiredShopLevel}で解禁"
                : "";
        }
    }
}
