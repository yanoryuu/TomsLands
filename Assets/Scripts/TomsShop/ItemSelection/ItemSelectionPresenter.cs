using System;
using System.Collections.Generic;
using R3;
using VContainer.Unity;

public class ItemSelectionPresenter : IDisposable, IStartable
{
    private readonly ItemSelectionModel selectionModel;
    private readonly ItemSelectionView selectionView;
    private readonly ItemModel itemModel;
    private readonly TomsModel tomsModel;
    private readonly CompositeDisposable disposables = new();
    private readonly Dictionary<string, CompositeDisposable> displaySlotDisposables = new();

    private CompositeDisposable panelDisposables = new();
    private ItemTypeData.ItemType currentItemType = ItemTypeData.ItemType.Weapon;

    public Subject<Unit> OnClosed { get; } = new();

    public ItemSelectionPresenter(
        ItemSelectionModel selectionModel,
        ItemSelectionView selectionView,
        ItemModel itemModel,
        TomsModel tomsModel)
    {
        this.selectionModel = selectionModel;
        this.selectionView = selectionView;
        this.itemModel = itemModel;
        this.tomsModel = tomsModel;
    }

    public void Start()
    {
        Bind();
    }

    public void OnOpenSelectionPanel()
    {
        selectionView.Show();
        SetRuntimeItems();
    }

    public void OnCloseSelectionPanel()
    {
        selectionView.Hide();
        itemModel.SaveData();
        OnClosed.OnNext(Unit.Default);
    }

    public void Initialize() { }

    private void Bind()
    {
        selectionView.OnCloseRequested
            .Subscribe(_ => OnCloseSelectionPanel())
            .AddTo(disposables);

        selectionView.OnArmorPanelRequested
            .Subscribe(_ => ChangeSelectionPanel(selectionModel.AarmorRuntimeItems, ItemTypeData.ItemType.Armor))
            .AddTo(disposables);

        selectionView.OnWeaponPanelRequested
            .Subscribe(_ => ChangeSelectionPanel(selectionModel.WeaponRuntimeItems, ItemTypeData.ItemType.Weapon))
            .AddTo(disposables);

        selectionView.OnAutoDisplayRequested
            .Subscribe(_ =>
            {
                itemModel.AutoSetDisplay(tomsModel.BlacksmithLevel.Value);
                RefreshRuntimeItems();
                RefreshCurrentSelectionPanel();
                RebuildDisplaySlots();
            })
            .AddTo(disposables);
    }

    private void ChangeSelectionPanel(List<RuntimeItemData> items, ItemTypeData.ItemType itemType)
    {
        currentItemType = itemType;

        panelDisposables.Dispose();
        panelDisposables = new CompositeDisposable();

        var itemSlots = selectionView.PopulateItemList(items);

        foreach (var slot in itemSlots)
        {
            var itemdata = itemModel.GetRuntimeItem(slot.itemId);
            if (itemdata == null) continue;

            itemdata.Stock.Subscribe(x =>
                {
                    slot.SetMaxDisplayQuantity(x);
                    slot.SetStock(x);
                })
                .AddTo(panelDisposables);

            itemdata.DisplayStock.Subscribe(x => slot.SetDisplayQuantity(x))
                .AddTo(panelDisposables);

            slot.OnDisplayQuantityChanged.Subscribe(x => itemdata.UpdateDisplayStock(x))
                .AddTo(panelDisposables);

            slot.OnToggleChanged.Subscribe(x => itemdata.UpdateIsDisplay(x))
                .AddTo(panelDisposables);

            itemdata.IsDisplay.Subscribe(x =>
                {
                    slot.SetSelectToggle(x);
                    if (x)
                    {
                        EnsureDisplaySlotSubscription(itemdata);
                    }
                    else
                    {
                        RemoveDisplaySlotSubscription(itemdata.ItemId);
                    }
                })
                .AddTo(panelDisposables);

            itemdata.CurrentPrice.Subscribe(x => slot.SetPrice(x))
                .AddTo(panelDisposables);

            slot.OnInfoRequested.Subscribe(id =>
                {
                    var rt = itemModel.GetRuntimeItem(id);
                    if (rt != null)
                    {
                        selectionView.SetDescription(rt.ItemDescription, rt.ItemIcon);
                    }
                })
                .AddTo(panelDisposables);
        }

        selectionView.SortItemTab(itemType);
    }

    private void SetRuntimeItems()
    {
        RefreshRuntimeItems();
        ChangeSelectionPanel(selectionModel.WeaponRuntimeItems, ItemTypeData.ItemType.Weapon);
        RebuildDisplaySlots();
    }

    private void RefreshRuntimeItems()
    {
        var stockItems = itemModel.PickItemRuntimeListForStock(itemModel.RuntimeItems, minStock: 1);

        var armorItems = itemModel.PickItemRuntimeList(
            stockItems,
            ItemTypeData.ItemType.Armor,
            tomsModel.BlacksmithLevel.Value);

        var weaponItems = itemModel.PickItemRuntimeList(
            stockItems,
            ItemTypeData.ItemType.Weapon,
            tomsModel.BlacksmithLevel.Value);

        selectionModel.SetRuntimeItems(armorItems, weaponItems);
    }

    private void RefreshCurrentSelectionPanel()
    {
        var items = currentItemType == ItemTypeData.ItemType.Armor
            ? selectionModel.AarmorRuntimeItems
            : selectionModel.WeaponRuntimeItems;

        ChangeSelectionPanel(items, currentItemType);
    }

    private void RebuildDisplaySlots()
    {
        foreach (var d in displaySlotDisposables.Values)
        {
            d.Dispose();
        }

        displaySlotDisposables.Clear();
        selectionView.ClearDisplaySlots();

        foreach (var item in itemModel.RuntimeItems)
        {
            if (item.IsDisplay.Value)
            {
                EnsureDisplaySlotSubscription(item);
            }
        }
    }

    private void EnsureDisplaySlotSubscription(RuntimeItemData itemdata)
    {
        if (displaySlotDisposables.ContainsKey(itemdata.ItemId))
            return;

        var d = new CompositeDisposable();
        displaySlotDisposables[itemdata.ItemId] = d;

        var displaySlot = selectionView.EnsureDisplaySlot(
            itemdata.ItemId,
            itemdata.ItemIcon,
            itemdata.DisplayStock.Value);

        itemdata.DisplayStock
            .Subscribe(count => displaySlot.SetQuantity(count))
            .AddTo(d);

        displaySlot.OnRemoveRequested
            .Subscribe(_ =>
            {
                itemdata.UpdateIsDisplay(false);
                RemoveDisplaySlotSubscription(itemdata.ItemId);
            })
            .AddTo(d);
    }

    private void RemoveDisplaySlotSubscription(string itemId)
    {
        if (displaySlotDisposables.TryGetValue(itemId, out var d))
        {
            d.Dispose();
            displaySlotDisposables.Remove(itemId);
        }

        selectionView.RemoveDisplaySlot(itemId);
    }

    public void Dispose()
    {
        panelDisposables.Dispose();
        foreach (var d in displaySlotDisposables.Values)
        {
            d.Dispose();
        }

        displaySlotDisposables.Clear();
        disposables.Dispose();
    }
}
