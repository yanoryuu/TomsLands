using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Inspector で設定した説明文を、ポインターホバー時にイベントとして通知するトリガー。
/// HeroPanel の各 UI セクションに Attach して使用する。
/// </summary>
public class HoverDescriptionTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField, TextArea(2, 5)] private string description;

    public event Action<string> OnHoverEnter;
    public event Action OnHoverExit;

    /// <summary>
    /// 動的に説明文を変更したい場合（作戦ドロップダウンなど）に使用する。
    /// </summary>
    public void SetDescription(string desc) => description = desc;

    public void OnPointerEnter(PointerEventData eventData) => OnHoverEnter?.Invoke(description);
    public void OnPointerExit(PointerEventData eventData) => OnHoverExit?.Invoke();
}

