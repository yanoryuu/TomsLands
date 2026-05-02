using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using R3;
using TMPro;

/// <summary>
/// 需要一覧ダッシュボードのView。
/// 全アイテムをソート・フィルタして一覧表示する。
/// </summary>
public class DemandDashboardView : MonoBehaviour
{
    [Header("パネル")]
    [SerializeField] private GameObject dashboardPanel;
    [SerializeField] private Button closeButton;

    [Header("スクロール")]
    [SerializeField] private Transform slotParent;
    [SerializeField] private GameObject slotPrefab;

    [Header("ソートボタン")]
    [SerializeField] private Button sortByRevenueButton;
    [SerializeField] private Button sortByDemandButton;
    [SerializeField] private Button sortByPriceButton;

    [Header("フィルターボタン")]
    [SerializeField] private Button filterAllButton;
    [SerializeField] private Button filterWeaponButton;
    [SerializeField] private Button filterArmorButton;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<DashboardSortMode> OnSortRequested { get; } = new();
    public Subject<DashboardFilterMode> OnFilterRequested { get; } = new();


    private void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));

        if (sortByRevenueButton != null)
            sortByRevenueButton.onClick.AddListener(() => OnSortRequested.OnNext(DashboardSortMode.Revenue));
        if (sortByDemandButton != null)
            sortByDemandButton.onClick.AddListener(() => OnSortRequested.OnNext(DashboardSortMode.Demand));
        if (sortByPriceButton != null)
            sortByPriceButton.onClick.AddListener(() => OnSortRequested.OnNext(DashboardSortMode.Price));

        if (filterAllButton != null)
            filterAllButton.onClick.AddListener(() => OnFilterRequested.OnNext(DashboardFilterMode.All));
        if (filterWeaponButton != null)
            filterWeaponButton.onClick.AddListener(() => OnFilterRequested.OnNext(DashboardFilterMode.Weapon));
        if (filterArmorButton != null)
            filterArmorButton.onClick.AddListener(() => OnFilterRequested.OnNext(DashboardFilterMode.Armor));
    }

    public void Show() => dashboardPanel?.SetActive(true);
    public void Hide() => dashboardPanel?.SetActive(false);

    public void Populate(IEnumerable<RuntimeItemData> items)
    {
        foreach (Transform t in slotParent) Destroy(t.gameObject);

        foreach (var data in items)
        {
            if (slotPrefab == null) break;
            var go = Instantiate(slotPrefab, slotParent);
            var slot = go.GetComponent<DemandDashboardSlot>();
            if (slot == null) continue;
            slot.Setup(data);
        }
    }
}

public enum DashboardSortMode { Revenue, Demand, Price }
public enum DashboardFilterMode { All, Weapon, Armor }
