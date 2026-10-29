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

    /// <param name="placedLevel">置いてあればそのレベル（0 = 置いていない）。</param>
    /// <param name="broken">置いてあるマシンが故障中か。</param>
    /// <param name="purchaseCost">実際に払う額（村の割引込み）。負なら定価を出す。</param>
    public void Setup(ShopMachineData machine, int placedLevel, bool broken, bool levelLocked, int purchaseCost = -1)
    {
        MachineId = machine.machineId;

        if (iconImage != null)
        {
            iconImage.sprite = machine.icon;
            iconImage.preserveAspect = true;
            iconImage.enabled = machine.icon != null;
        }
        if (nameText != null) nameText.text = machine.machineName;
        if (effectText != null) effectText.text = machine.EffectSummaryAt(Mathf.Max(1, placedLevel));
        if (costText != null) costText.text = placedLevel > 0 ? "" : $"{(purchaseCost >= 0 ? purchaseCost : machine.BaseCost):N0}G";
        if (stateText != null)
        {
            stateText.text = placedLevel > 0 ? (broken ? "<color=#E05A4A>故障中</color>" : $"設置中 Lv{placedLevel}")
                : levelLocked ? $"店Lv{machine.requiredShopLevel}で解禁"
                : "";
        }
    }

    /// <summary>
    /// 「設置済み一覧」の行として使う（複数設置対応）。
    /// OnSelected には placementId が流れる。
    /// </summary>
    public void SetupPlacementRow(string placementId, string label, string detail, bool selected)
    {
        MachineId = placementId;

        if (iconImage != null) iconImage.enabled = false;
        if (nameText != null) nameText.text = label;
        if (effectText != null) effectText.text = detail;
        if (costText != null) costText.text = "";
        if (stateText != null) stateText.text = selected ? "選択中" : "";
    }
}
