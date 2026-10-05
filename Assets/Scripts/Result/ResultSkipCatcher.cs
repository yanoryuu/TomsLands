using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 演出中の画面クリックを受けてスキップを通知する透明な全面パネル。
/// 演出が終わったら非アクティブにして、ボタン操作を邪魔しないようにする。
/// </summary>
public class ResultSkipCatcher : MonoBehaviour, IPointerClickHandler
{
    public event Action Clicked;

    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
}
