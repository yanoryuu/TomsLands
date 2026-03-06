using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class MapInfoView : MonoBehaviour
{
    [Header("ScrollView")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private GameObject mapInfoContent;

    [SerializeField] private MapPurchaseSlot mapPurchaseSlot;

    /// <summary>
    /// いずれかのスロットで購入ボタンが押されたときに発火する
    /// </summary>
    public Subject<DungeonName> OnMapPurchaseClicked { get; } = new();

    private readonly CompositeDisposable slotDisposables = new();
    private readonly List<MapPurchaseSlot> activeSlots = new();

    public void SetMapSlot(List<DungeonData> maps, Dictionary<DungeonName, int[]> mapCosts ,int currentTurn)
    {
        // 前回のスロット購読と要素を破棄
        slotDisposables.Clear();
        foreach (var slot in activeSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        activeSlots.Clear();

        // スクロール位置をリセット
        if (scrollRect)
        {
            scrollRect.normalizedPosition = new Vector2(0, 1);
        }

        for (int i = 0; i < maps.Count; i++)
        {
            DungeonData data = maps[i];
            MapPurchaseSlot slot = Instantiate(mapPurchaseSlot, mapInfoContent.transform);

            int mapCost;
            
            if(mapCosts[data.key].Length <= currentTurn)
            {
                mapCost = mapCosts[data.key][currentTurn - 1];
            }
            else
            {
                mapCost = mapCosts[data.key][mapCosts[data.key].Length - 1];
            }
            
            slot.SetMapInfo(data.key, data.dungeonName, data.dungeonImage, mapCost);
            activeSlots.Add(slot);

            // スロットの購入イベントを集約
            slot.OnPurchaseClicked
                .Subscribe(key => OnMapPurchaseClicked.OnNext(key))
                .AddTo(slotDisposables);
        }
    }

    private void OnDestroy()
    {
        slotDisposables.Dispose();
    }
}
