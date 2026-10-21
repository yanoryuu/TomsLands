using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「店」画面（改装／設備）のタブ。改装画面（ShopUpgradeView）と設備画面（ShopMachineView）の両方に同じものを置き、
/// 自分の画面のタブを選択中の見た目（押し込んだ絵＋金色の文字。設定画面のタブと同じ）にする。
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

    [Header("見た目（設定画面のタブと同じ）")]
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite selectedSprite;
    [SerializeField] private Color normalLabelColor = Color.white;
    [SerializeField] private Color selectedLabelColor = new(1f, 0.82f, 0.3f, 1f);

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
        var image = button.targetGraphic as Image ?? button.GetComponent<Image>();
        var sprite = selected ? selectedSprite : normalSprite;
        if (image != null && sprite != null) image.sprite = sprite;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.color = selected ? selectedLabelColor : normalLabelColor;
    }
}
