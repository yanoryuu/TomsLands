using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EventScene のView。イベントのタイトル・説明を表示し、確認ボタンを持つ。
/// descriptionは「@」で分割し、ボタンクリックで順次表示。すべて表示後にeffectTextを表示する。
/// </summary>
public class EventSceneView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI eventTitleText;
    [SerializeField] private TextMeshProUGUI eventDescriptionText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private GameObject eventPanel;

    /// <summary>
    /// すべてのテキスト表示が完了し、最後の確認ボタンが押された
    /// </summary>
    public Subject<Unit> OnConfirmClicked { get; } = new();

    private string[] _descriptionPages;
    private string _effectText;
    private int _currentPageIndex;
    private bool _isShowingEffect;

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(OnClickConfirm);
        }
    }

    /// <summary>
    /// イベントの内容を表示する。descriptionは「@」で分割し、ボタンで順次表示する。
    /// </summary>
    public void ShowEvent(string title, string description, string effectText)
    {
        if (eventPanel != null) eventPanel.SetActive(true);
        if (eventTitleText != null) eventTitleText.text = title;

        _descriptionPages = description.Split('@');
        _effectText = effectText;
        _currentPageIndex = 0;
        _isShowingEffect = false;

        ShowCurrentPage();
    }

    /// <summary>
    /// パネルを非表示にする
    /// </summary>
    public void Hide()
    {
        if (eventPanel != null) eventPanel.SetActive(false);
    }

    private void OnClickConfirm()
    {
        if (_isShowingEffect)
        {
            // エフェクト表示中にクリック → 完了通知
            OnConfirmClicked.OnNext(Unit.Default);
            return;
        }

        _currentPageIndex++;

        if (_currentPageIndex < _descriptionPages.Length)
        {
            // 次のdescriptionページを表示
            ShowCurrentPage();
        }
        else
        {
            // すべてのdescriptionを表示し終えたらエフェクトを表示
            _isShowingEffect = true;
            if (eventDescriptionText != null) eventDescriptionText.text = _effectText;
        }
    }

    private void ShowCurrentPage()
    {
        if (eventDescriptionText != null && _descriptionPages != null && _currentPageIndex < _descriptionPages.Length)
        {
            eventDescriptionText.text = _descriptionPages[_currentPageIndex].Trim();
        }
    }
}

