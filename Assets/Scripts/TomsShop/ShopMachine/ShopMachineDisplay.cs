using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 店ホームに設置済みマシンの見た目を反映する（ShopDeskDisplay と同型）。
/// TomsShop.prefab に MachineSlot（SpriteRenderer）を手置きして itemSlots 配列に登録する。
/// 未配線でも動作する（何も表示されないだけ）。
/// </summary>
public class ShopMachineDisplay : MonoBehaviour
{
    [SerializeField] private SpriteRenderer[] machineSlots;

    public void RefreshDisplay(ShopMachineModel model)
    {
        if (machineSlots == null || model == null) return;

        var placed = new List<ShopMachineData>();
        foreach (var id in model.PlacedMachineIds)
        {
            var machine = model.GetMachine(id);
            if (machine != null) placed.Add(machine);
        }

        for (int i = 0; i < machineSlots.Length; i++)
        {
            if (machineSlots[i] == null) continue;

            if (i < placed.Count)
            {
                var sprite = placed[i].shopSprite != null ? placed[i].shopSprite : placed[i].icon;
                machineSlots[i].sprite = sprite;
                machineSlots[i].gameObject.SetActive(sprite != null);
            }
            else
            {
                machineSlots[i].gameObject.SetActive(false);
            }
        }
    }
}
