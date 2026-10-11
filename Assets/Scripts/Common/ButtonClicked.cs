using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 押下中だけ Image の色を少し暗くする（スプライトは差し替えない）
public class ButtonClicked : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Image buttonImage;    // ボタンのImage
    [SerializeField] private Color pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f); // 押下時の乗算色

    private Color normalColor;
    private bool isPressed;

    private void Reset()
    {
        // 自動でアタッチされたImageをセット（ボタンにこのスクリプトを付けた場合）
        buttonImage = GetComponent<Image>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // 同じ Image を Button の ColorTint が担当しているなら、二重に暗くしない
        if (buttonImage == null || isPressed || HasColorTint()) return;

        normalColor = buttonImage.color;
        buttonImage.color = normalColor * pressedColor;
        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Restore();
    }

    private void OnDisable()
    {
        Restore();
    }

    private void Restore()
    {
        if (!isPressed) return;
        if (buttonImage != null) buttonImage.color = normalColor;
        isPressed = false;
    }

    private bool HasColorTint()
    {
        var selectable = GetComponent<Selectable>();
        return selectable != null
               && selectable.transition == Selectable.Transition.ColorTint
               && selectable.targetGraphic == buttonImage;
    }
}
