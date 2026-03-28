using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EventScene のView。イベントのタイトル・説明を表示し、確認ボタンを持つ。
/// </summary>
public class EventSceneView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI eventTitleText;
    [SerializeField] private TextMeshProUGUI eventDescriptionText;
    [SerializeField] private TextMeshProUGUI eventEffectText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private GameObject eventPanel;

    /// <summary>
    /// 確認ボタンが押された
    /// </summary>
    public Subject<Unit> OnConfirmClicked { get; } = new();

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(() => OnConfirmClicked.OnNext(Unit.Default));
        }
    }

    /// <summary>
    /// イベントの内容を表示する
    /// </summary>
    public void ShowEvent(string title, string description, string effectText)
    {
        if (eventPanel != null) eventPanel.SetActive(true);
        if (eventTitleText != null) eventTitleText.text = title;
        if (eventDescriptionText != null) eventDescriptionText.text = description;
        if (eventEffectText != null) eventEffectText.text = effectText;
    }

    /// <summary>
    /// パネルを非表示にする
    /// </summary>
    public void Hide()
    {
        if (eventPanel != null) eventPanel.SetActive(false);
    }
}

