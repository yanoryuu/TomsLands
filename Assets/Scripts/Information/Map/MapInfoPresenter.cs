using System;
using System.Collections.Generic;
using R3;
using UnityEngine;
using VContainer.Unity;

public class MapInfoPresenter : IStartable,IDisposable
{
    private readonly MapInfoModel _mapInfoModel;
    private readonly MapInfoView _mapInfoView;
    
    private CompositeDisposable _disposables;
    
    public MapInfoPresenter(MapInfoModel mapInfoModel, MapInfoView mapInfoView)
    {
        _mapInfoModel = mapInfoModel;
        _mapInfoView = mapInfoView;
    }
    
    public void Start()
    {
        _mapInfoModel.LoadDungeonData();
        CreateSlots();
    }
    
    public void Dispose()
    {
        
    }
    
    public void CreateSlots()
    {
        var MapSlots = _mapInfoView.PopulateMapList(_mapInfoModel._availableDungeons);

        foreach (var slot in MapSlots)
        {
            slot.OnPurchaseClicked.Subscribe(id =>
                {
                    Debug.Log($"Map purchased: {id}");
                    var dungeonData = DungeonRepository.Instance.GetById(id);
                    dungeonData.SetShow(true);
                })
                .AddTo(_disposables);
        }
    }
    
    
}
