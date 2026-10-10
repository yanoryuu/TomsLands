using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// パッド/キーで選択された瞬間を通知するだけの小部品（ホバーで説明が出る UI の「選択＝ホバー」用）。
/// ISelectHandler のみ実装するので、ドラッグ・スクロール等のポインタイベントは横取りしない。
/// マウス操作中（ControllerInput.IsNavigating が false）は通知しない。
/// </summary>
[DisallowMultipleComponent]
public sealed class PadSelectRelay : MonoBehaviour, ISelectHandler
{
    public event Action Selected;

    public static PadSelectRelay GetOrAdd(GameObject go)
    {
        if (go == null) return null;
        var relay = go.GetComponent<PadSelectRelay>();
        return relay != null ? relay : go.AddComponent<PadSelectRelay>();
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!ControllerInput.IsNavigating.CurrentValue) return;
        Selected?.Invoke();
    }
}
