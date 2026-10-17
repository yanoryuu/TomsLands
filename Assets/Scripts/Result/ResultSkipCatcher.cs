using System;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 演出中の画面クリックを受けてスキップを通知する透明な全面パネル。
/// 演出が終わったら非アクティブにして、ボタン操作を邪魔しないようにする。
/// パッド/キーの A・B（Enter/Esc）でもスキップする（有効な間だけ）。
/// </summary>
public class ResultSkipCatcher : MonoBehaviour, IPointerClickHandler
{
    public event Action Clicked;

    private IDisposable _padSub;

    private void OnEnable()
    {
        _padSub = ControllerInput.OnButtonPressed
            .Where(b => b == ControllerButton.A || b == ControllerButton.B)
            .Where(_ => !UIFocusManager.IsScopeInputBlocked)
            .Subscribe(_ => Clicked?.Invoke());
    }

    private void OnDisable()
    {
        _padSub?.Dispose();
        _padSub = null;
    }

    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
}
