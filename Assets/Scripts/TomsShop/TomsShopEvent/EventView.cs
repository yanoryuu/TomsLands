using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TomsShop内でイベントをポップアップ表示するView。
/// descriptionは「@」で分割し、ボタンクリックで順次表示。
/// すべて表示後にeffectTextを表示し、最後の確認で完了通知を発行する。
/// パネル制御はGamePanelManagerで一元管理する。
/// </summary>
public class EventView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI eventTitleText;
    [SerializeField] private TextMeshProUGUI eventDescriptionText;
    [SerializeField] private Button confirmButton;

    private GamePanelManager gamePanelManager;

    /// <summary>
    /// すべてのテキスト表示が完了し、最後の確認ボタンが押された時に発行される
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
        // 自動取得を試みる
        gamePanelManager = FindObjectOfType<GamePanelManager>();
    }

    /// <summary>
    /// GamePanelManagerを注入する（VContainer経由）
    /// </summary>
    public void Initialize(GamePanelManager panelManager)
    {
        gamePanelManager = panelManager;
    }

    /// <summary>
    /// イベントポップアップを表示する。descriptionは「@」で分割して順次表示する。
    /// </summary>
    public void ShowEvent(string title, string description, string effectText)
    {
        if (eventTitleText != null) eventTitleText.text = title;

        _descriptionPages = description.Split('@');
        _effectText = effectText;
        _currentPageIndex = 0;
        _isShowingEffect = false;

        ShowCurrentPage();

        // GamePanelManager経由でパネルを表示
        if (gamePanelManager != null)
        {
            gamePanelManager.ShowEventPanel();
        }
    }

    /// <summary>
    /// ポップアップを非表示にする
    /// </summary>
    public void Hide()
    {
        // GamePanelManager経由でパネルを非表示
        if (gamePanelManager != null)
        {
            gamePanelManager.HideEventPanel();
        }
    }

    private void OnClickConfirm()
    {
        if (_isShowingEffect)
        {
            // エフェクト表示中にクリック → 完了通知を発行してパネルを閉じる
            Hide();
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
            // すべてのdescriptionを表示し終えたらエフェクトテキストを表示
            _isShowingEffect = true;
            if (eventDescriptionText != null)
            {
                eventDescriptionText.text = string.IsNullOrEmpty(_effectText) ? "（効果なし）" : _effectText;
            }
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
