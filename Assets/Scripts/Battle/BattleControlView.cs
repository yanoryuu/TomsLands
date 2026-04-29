using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public class BattleControlView : MonoBehaviour
{
    [Header("Pause")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private TextMeshProUGUI pauseButtonText;

    [Header("End Battle")]
    [SerializeField] private Button endBattleButton;

    public Subject<Unit> OnPauseToggled { get; } = new();
    public Subject<Unit> OnEndBattleRequested { get; } = new();

    private PopUpManager popUpManager;

    [Inject]
    public void Construct(PopUpManager popUpManager)
    {
        this.popUpManager = popUpManager;
    }

    private void Awake()
    {
        if (pauseButton != null)
            pauseButton.onClick.AddListener(() => OnPauseToggled.OnNext(Unit.Default));
        if (endBattleButton != null)
            endBattleButton.onClick.AddListener(() => OnEndBattleRequested.OnNext(Unit.Default));
    }

    public void UpdatePauseButtonText(bool isPaused)
    {
        if (pauseButtonText != null)
            pauseButtonText.text = isPaused ? "\u518d\u958b" : "\u4e00\u6642\u505c\u6b62";
    }

    public async UniTask<bool> ShowRestockPopupAsync(
        string itemName, int totalCost, bool canAfford, CancellationToken token)
    {
        if (popUpManager == null)
        {
            Debug.LogError("[BattleControlView] PopUpManager is not injected.");
            return false;
        }

        var tcs = new UniTaskCompletionSource<bool>();
        bool popupClosedByCallback = false;

        var message =
            $"\u300c{itemName}\u300d\u306e\u5728\u5eab\u304c\u306a\u304f\u306a\u308a\u307e\u3057\u305f\u3002\n" +
            $"10\u500b\u88dc\u5145\u3057\u307e\u3059\u304b\uff1f\n\n" +
            $"\u8cbb\u7528: {totalCost}G";

        if (!canAfford)
        {
            message += "\n\u6240\u6301\u91d1\u304c\u4e0d\u8db3\u3057\u3066\u3044\u307e\u3059\u3002";
        }

        var data = new PopUpData
        {
            Title = "\u5728\u5eab\u5207\u308c",
            Message = message,
            ConfirmButtonText = canAfford ? "\u88dc\u5145\u3059\u308b" : "\u9589\u3058\u308b",
            CancelButtonText = "\u3084\u3081\u308b",
            Size = PopupSizeEnum.Medium,
            IsCloseOnly = !canAfford,
            OnConfirm = () =>
            {
                popupClosedByCallback = true;
                tcs.TrySetResult(canAfford);
            },
            OnCancel = () =>
            {
                popupClosedByCallback = true;
                tcs.TrySetResult(false);
            }
        };

        popUpManager.Show(data);

        try
        {
            return await tcs.Task.AttachExternalCancellation(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (!popupClosedByCallback)
            {
                popUpManager.Close();
            }
        }
    }
}
