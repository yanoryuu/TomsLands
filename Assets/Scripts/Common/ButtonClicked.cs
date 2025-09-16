using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonClicked : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Image buttonImage;    // ボタンのImage
    [SerializeField] private Sprite normalSprite;  // 通常時
    [SerializeField] private Sprite pressedSprite; // 押下時

    private void Reset()
    {
        // 自動でアタッチされたImageをセット（ボタンにこのスクリプトを付けた場合）
        buttonImage = GetComponent<Image>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (buttonImage != null && pressedSprite != null)
        {
            buttonImage.sprite = pressedSprite;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (buttonImage != null && normalSprite != null)
        {
            buttonImage.sprite = normalSprite;
        }
    }
}