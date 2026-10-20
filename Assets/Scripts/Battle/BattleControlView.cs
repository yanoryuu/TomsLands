using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

public class BattleControlView : MonoBehaviour
{
    [Header("Pause")]
    [SerializeField] private Button pauseButton;
    [SerializeField] private TextMeshProUGUI pauseButtonText;

    [Header("End Battle")]
    [SerializeField] private Button endBattleButton;

    [Header("在庫補充（数量選択）")]
    [Tooltip("数量を選べる補充ポップアップ。未設定の場合は従来の10個固定ポップアップにフォールバックする")]
    [SerializeField] private RestockQuantityPopup restockQuantityPopup;

    [Header("配信開始カウントダウン")]
    [Tooltip("配信開始前の3秒カウントダウン。未設定の場合はカウントダウンなしで即開始する")]
    [SerializeField] private StreamingCountdownView countdownView;

    /// <summary>一時停止ボタン（パッドの Start から押すため）。未配線なら null。</summary>
    public Button PauseButton => pauseButton;

    public Subject<Unit> OnPauseToggled { get; } = new();
    public Subject<Unit> OnEndBattleRequested { get; } = new();

    private PopUpManager popUpManager;

    [Inject]
    public void Construct(PopUpManager popUpManager) => this.popUpManager = popUpManager;

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

    /// <summary>
    /// 配信開始前のカウントダウンを再生する（未配線なら即座に戻る）。
    /// </summary>
    public UniTask PlayCountdownAsync(int seconds, CancellationToken token)
        => countdownView != null ? countdownView.PlayAsync(seconds, token) : UniTask.CompletedTask;

    /// <summary>
    /// 在庫補充の数量選択ポップアップ（鍛冶屋の購入UIと同じ ItemDetailPanel）を表示し、
    /// 選ばれた数量を返す（キャンセル時は 0）。
    /// 未配線の場合は従来のはい/いいえポップアップ（10個固定）で代替する。
    /// </summary>
    public async UniTask<int> ShowRestockQuantityPopupAsync(
        RuntimeItemData runtime, int unitCost, int maxQuantity, float recommendScore, CancellationToken token)
    {
        if (restockQuantityPopup != null)
            return await restockQuantityPopup.ShowAsync(runtime, unitCost, maxQuantity, recommendScore, token);

        // フォールバック: 従来の10個固定確認
        bool canAfford = maxQuantity >= 10;
        bool confirmed = await ShowRestockPopupAsync(runtime.ItemName, unitCost * 10, canAfford, token);
        return confirmed && canAfford ? 10 : 0;
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
            $"\u8cbb\u7528: {totalCost:N0}G";

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
        BringPopupToFrontAsync(token).Forget();

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

    /// <summary>
    /// 「配信終了」の確認ポップアップ。終了するなら true。
    /// token がキャンセルされた（確認中に決着した）ときはポップアップを閉じて false を返す。
    /// </summary>
    public async UniTask<bool> ShowEndStreamingConfirmAsync(CancellationToken token)
    {
        if (popUpManager == null)
        {
            // 確認を出せないまま途中終了すると防衛報酬などを失うので、押し間違いとみなして終了しない
            Debug.LogError("[BattleControlView] PopUpManager is not injected.");
            return false;
        }

        var tcs = new UniTaskCompletionSource<bool>();
        bool popupClosedByCallback = false;

        popUpManager.Show(new PopUpData
        {
            Title = "配信を終了しますか？",
            Message = "ここまでの売上は受け取れます。\n途中で終えると、防衛報酬・経験値・\nレベルアップ・レリックはもらえません。",
            ConfirmButtonText = "終了する",
            CancelButtonText = "続ける",
            Size = PopupSizeEnum.Medium,
            OnConfirm = () =>
            {
                popupClosedByCallback = true;
                tcs.TrySetResult(true);
            },
            OnCancel = () =>
            {
                popupClosedByCallback = true;
                tcs.TrySetResult(false);
            }
        });
        BringPopupToFrontAsync(token).Forget();

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
                popUpManager.Close();
        }
    }

    private const string PopupSceneName = "PopupScene";
    // 配信画面のキャンバス（UICanvas 10 / StatusCanvas 11）より前。ツールチップ（500）よりは後ろ
    private const int PopupSortingOrderInBattle = 100;

    /// <summary>
    /// PopupScene のキャンバスは sortingOrder 3 で、配信画面の UI（10〜11）の後ろに隠れてボタンが押せない。
    /// 読み込みを待って、この画面にいる間だけ前に出す（他の画面の並びは変えない）。
    /// </summary>
    private async UniTaskVoid BringPopupToFrontAsync(CancellationToken token)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, destroyCancellationToken);
            await UniTask.WaitUntil(() => SceneManager.GetSceneByName(PopupSceneName).isLoaded, cancellationToken: linked.Token);
            foreach (var root in SceneManager.GetSceneByName(PopupSceneName).GetRootGameObjects())
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    if (canvas.isRootCanvas && canvas.sortingOrder < PopupSortingOrderInBattle)
                        canvas.sortingOrder = PopupSortingOrderInBattle;
        }
        catch (OperationCanceledException)
        {
            // 決着・シーン破棄で閉じた
        }
    }
}
