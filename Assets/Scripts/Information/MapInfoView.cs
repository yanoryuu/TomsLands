using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class MapInfoView : MonoBehaviour
{
    [Header("ScrollView")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private GameObject mapInfoContent;

    [SerializeField] private GameObject mapPurchaseSlotPrefab;

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
        for (int i = mapInfoContent.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(mapInfoContent.transform.GetChild(i).gameObject);
        }
        activeSlots.Clear();

        for (int i = 0; i < maps.Count; i++)
        {
            DungeonData data = maps[i];

            // コスト辞書にキーがなければスキップ
            if (!mapCosts.ContainsKey(data.key))
            {
                Debug.LogWarning($"[MapInfoView] mapCosts にキー {data.key} が見つかりません。スキップします。");
                continue;
            }

            GameObject slotObj = Instantiate(mapPurchaseSlotPrefab, mapInfoContent.transform);
            MapPurchaseSlot slot = slotObj.GetComponent<MapPurchaseSlot>();

            int mapCost;
            int[] costs = mapCosts[data.key];

            if (costs.Length >= currentTurn && currentTurn > 0)
            {
                mapCost = costs[currentTurn - 1];
            }
            else
            {
                mapCost = costs[costs.Length - 1];
            }
            
            slot.SetMapInfo(data.key, data.dungeonName, data.dungeonIcon, mapCost);
            activeSlots.Add(slot);

            // スロットの購入イベントを集約
            slot.OnPurchaseClicked
                .Subscribe(key => OnMapPurchaseClicked.OnNext(key))
                .AddTo(slotDisposables);
        }

        // スロット生成後にレイアウトを確定させる
        if (mapInfoContent)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(mapInfoContent.GetComponent<RectTransform>());
        }
    }

    private void OnDestroy()
    {
        slotDisposables.Dispose();
    }
}
