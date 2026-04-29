using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ヒント1件のUI。ボタンをタップすると対象画面へ誘導する。
/// </summary>
public class TurnActionHintItemUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Button actionButton;

    public Subject<ActionTarget> OnTapped { get; } = new();

    private ActionTarget _target;

    private static readonly Color ColorCritical    = new(0.85f, 0.15f, 0.15f, 0.90f);
    private static readonly Color ColorWarning     = new(0.90f, 0.65f, 0.05f, 0.90f);
    private static readonly Color ColorInfo        = new(0.15f, 0.50f, 0.85f, 0.90f);

    private void Awake()
    {
        if (actionButton != null)
            actionButton.onClick.AddListener(() => OnTapped.OnNext(_target));
    }

    public void Setup(TurnActionHint hint)
    {
        _target = hint.Target;

        if (messageText != null)
            messageText.text = hint.Message;

        if (backgroundImage != null)
        {
            backgroundImage.color = hint.Priority switch
            {
                HintPriority.Critical => ColorCritical,
                HintPriority.Warning  => ColorWarning,
                _                     => ColorInfo,
            };
        }
    }
}
