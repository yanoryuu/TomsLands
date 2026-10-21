using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「店」画面（改装／設備）のタブ。改装画面（ShopUpgradeView）と設備画面（ShopMachineView）の両方に同じものを置き、
/// 自分の画面のタブを選択中の見た目にする（鍛冶屋・情報屋のタブと同じ: 枠の上辺の後ろから出ていて、選択中だけ少し持ち上がる）。
/// もう一方のタブ・LB/RB で OnOtherTabRequested を流し、Presenter が画面（TomsShopGamePhase）を切り替える。
/// 参照は未配線（null）でも動作する。
/// </summary>
public class ShopFacilityTabsView : MonoBehaviour
{
    public enum Tab { Upgrade, Machine }

    [Tooltip("この画面のタブ（改装画面なら Upgrade、設備画面なら Machine）")]
    [SerializeField] private Tab selfTab;
    [SerializeField] private Button upgradeTab;
    [SerializeField] private Button machineTab;

    [Header("見た目（鍛冶屋のタブと同じ）")]
    [Tooltip("選択中のタブを持ち上げる量（親の座標）。鍛冶屋は 10×1.4")]
    [SerializeField] private float selectedRaise = 14f;
    [Tooltip("空なら絵は替えない（鍛冶屋のタブは絵を替えず、持ち上げるだけ）")]
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite selectedSprite;
    [SerializeField] private Color normalLabelColor = Color.white;
    [SerializeField] private Color selectedLabelColor = Color.white;

    private readonly System.Collections.Generic.Dictionary<Button, float> baseY = new();

    /// <summary>もう一方のタブへ切り替えたい（クリック・LB/RB）。</summary>
    public Subject<Unit> OnOtherTabRequested { get; } = new();

    private void Awake()
    {
        if (upgradeTab != null) upgradeTab.onClick.AddListener(() => Request(Tab.Upgrade));
        if (machineTab != null) machineTab.onClick.AddListener(() => Request(Tab.Machine));
        ApplyLook();

        // パッド: LB/RB でタブを切り替える（2つしかないのでどちらも「もう一方」）
        var scope = UIFocusScope.GetOrAdd(gameObject);
        if (scope != null)
            scope.OnButton
                .Subscribe(b => { if (b == ControllerButton.LB || b == ControllerButton.RB) OnOtherTabRequested.OnNext(Unit.Default); })
                .AddTo(this);
    }

    private void OnEnable() => ApplyLook();

    private void Request(Tab tab)
    {
        if (tab == selfTab) return;
        OnOtherTabRequested.OnNext(Unit.Default);
    }

    private void ApplyLook()
    {
        Style(upgradeTab, selfTab == Tab.Upgrade);
        Style(machineTab, selfTab == Tab.Machine);
    }

    private void Style(Button button, bool selected)
    {
        if (button == null) return;
        var rt = (RectTransform)button.transform;
        if (!baseY.TryGetValue(button, out float y0)) baseY[button] = y0 = rt.anchoredPosition.y;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, selected ? y0 + selectedRaise : y0);
        var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
        var sprite = selected ? selectedSprite : normalSprite;
        if (image != null && sprite != null) image.sprite = sprite;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = selected ? selectedLabelColor : normalLabelColor;
    }
}
