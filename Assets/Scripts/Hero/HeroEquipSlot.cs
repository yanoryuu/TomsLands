using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 勇者の立ち絵に重ねる装備の枠（武器は手元、防具は胴）。棚のカードをここへ置くと装備する。
/// マウスのドラッグはカードの OnEndDrag で、離した位置がこの枠（か立ち絵）の上なら置く。
/// 押す（クリック・決定）と、持ち上げ中ならここに置く／持っていなければ棚をこの種類に切り替える（HeroPanelView が決める）。
/// </summary>
public sealed class HeroEquipSlot : MonoBehaviour
{
    [SerializeField] private bool isWeapon = true;
    [SerializeField] private Button button;
    [Tooltip("置ける間に光らせる枠（DragFeel.SetSlotGlow）")]
    [SerializeField] private RectTransform glowTarget;
    [Tooltip("置いたときにつぶれて収まる部分（アイコンの枠）")]
    [SerializeField] private RectTransform iconRoot;
    [SerializeField] private TextMeshProUGUI nameText;
    [Tooltip("持ち上げ中に枠の後ろで光る柔らかい光（置き先の目印）")]
    [SerializeField] private GameObject carryGlow;

    public bool IsWeapon => isWeapon;
    public Button Button => button;
    public RectTransform GlowTarget => glowTarget != null ? glowTarget : (RectTransform)transform;
    public RectTransform IconRoot => iconRoot != null ? iconRoot : (RectTransform)transform;

    /// <summary>持ち上げ中の置き先として光らせる（DragFeel の金の縁に加えて、後ろの柔らかい光）。</summary>
    public void SetCarryTarget(bool on)
    {
        DragFeel.SetSlotGlow(GlowTarget, on);
        if (carryGlow != null) carryGlow.SetActive(on);
    }

    public void SetItemName(string itemName)
    {
        if (nameText != null) nameText.text = string.IsNullOrEmpty(itemName) ? "なし" : itemName;
    }
}
