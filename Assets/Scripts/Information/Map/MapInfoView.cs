using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class MapInfoView : MonoBehaviour
{
    [Header("ScrollView")] [SerializeField]
    private ScrollRect scrollRect;

    [SerializeField] private GameObject mapInfoContent;

    [Header("Prefabs & Buttons")] [SerializeField]
    private GameObject mapPurchaseSlotPrefab;
    
    private List<MapPurchaseSlot> activeSlots = new();
    
    /// <summary>
    /// 商品リストを表示（スクロール位置を保存・復元）
    /// </summary>
    public List<MapPurchaseSlot> PopulateMapList(List<DungeonData> runtimeItems)
    {
        // 既存スロット破棄
        foreach (var slot in activeSlots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        activeSlots.Clear();

        // 再生成
        List<MapPurchaseSlot> slots = new();
        
        foreach (var item in runtimeItems)
        {
            var slotObj = Instantiate(mapPurchaseSlotPrefab, mapInfoContent.transform);
            var slot = slotObj.GetComponent<MapPurchaseSlot>();
            slot.SetMap(item);
            slots.Add(slot);
            activeSlots.Add(slot);
        }

        return slots;
    }
}