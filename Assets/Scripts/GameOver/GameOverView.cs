using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameOverView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button goToTitleButton;

    public Subject<Unit> OnRetryClicked { get; } = new();
    public Subject<Unit> OnGoToTitleClicked { get; } = new();

    private void Awake()
    {
        retryButton.onClick.AddListener(() => OnRetryClicked.OnNext(Unit.Default));
        goToTitleButton.onClick.AddListener(() => OnGoToTitleClicked.OnNext(Unit.Default));
    }

    public void Setup(int turn)
    {
        if (titleText != null)
            titleText.text = "税金納付不能！！";

        if (messageText != null)
            messageText.text = "ト、トムさん！！\nお店が差し押さえられてしまいました！";

        if (turnText != null)
            turnText.text = $"ターン {turn} で力尽きました...";
    }
}
