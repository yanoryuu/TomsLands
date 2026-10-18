using Cysharp.Threading.Tasks;
using R3;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// EventScene の EntryPoint。
/// EventInputData からイベントを読み取り → 表示 → コマンド実行 → TomsShop へ遷移する。
/// イベントはターンを進めない。
/// </summary>
public class EventScenePresenter : IAsyncStartable
{
    private readonly EventInputData _inputData;
    private readonly EventOutputData _outputData;
    private readonly EventSceneView _view;
    private readonly SceneTransitionService _sceneTransition;
    private readonly TomsModel _tomsModel;
    private readonly ShopStatusModel _shopStatusModel;
    private readonly RelicEffectResolver _relicResolver;

    public EventScenePresenter(
        EventInputData inputData,
        EventOutputData outputData,
        EventSceneView view,
        SceneTransitionService sceneTransition,
        TomsModel tomsModel,
        ShopStatusModel shopStatusModel,
        RelicEffectResolver relicResolver)
    {
        _inputData = inputData;
        _outputData = outputData;
        _view = view;
        _sceneTransition = sceneTransition;
        _tomsModel = tomsModel;
        _shopStatusModel = shopStatusModel;
        _relicResolver = relicResolver;
    }

    public async UniTask StartAsync(CancellationToken cancellation)
    {
        Debug.Log("[EventScenePresenter] EventScene started.");

        if (_inputData == null || string.IsNullOrEmpty(_inputData.EventId))
        {
            Debug.LogError("[EventScenePresenter] EventInputData is null or empty!");
            _sceneTransition.ReturnToTomsShop();
            return;
        }

        // CSVからイベントデータを検索
        var tomsEvent = EventDataLoader.FindById(_inputData.EventId);
        if (tomsEvent == null)
        {
            Debug.LogError($"[EventScenePresenter] Event not found: {_inputData.EventId}");
            _sceneTransition.ReturnToTomsShop();
            return;
        }

        // エフェクトテキストを構築（コマンドの効果を表示）
        string effectText = BuildEffectText(tomsEvent.commands);

        // イベントを表示
        _view.ShowEvent(tomsEvent.title, tomsEvent.description, effectText);

        // 確認ボタンが押されるのを待つ
        await _view.OnConfirmClicked.FirstAsync(cancellation);

        Debug.Log($"[EventScenePresenter] Event confirmed: {tomsEvent.title}");

        // コマンドを実行（インライン経路の TomsEventExecutor と同じ結果になるように適用する）
        ExecuteCommands(tomsEvent);

        // 結果を書き込み
        _outputData.SetResult(_inputData.GameFlowIndex);

        // TomsShop へ遷移
        _sceneTransition.ReturnToTomsShop();
    }

    /// <summary>
    /// イベントコマンドを実行する
    /// </summary>
    private void ExecuteCommands(TomsEvent e)
    {
        foreach (var cmd in e.commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    // 不正なマスターデータ（数値でないamount）でも落とさない
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr) && int.TryParse(moneyStr, out var amount))
                    {
                        // レリック補正・0 下限クランプ・保存は TomsEventExecutor（インライン経路）と共通
                        TomsEventExecutor.ApplyChangeMoney(_tomsModel, _relicResolver, e.id, amount);
                        Debug.Log($"[EventScenePresenter] ChangeMoney: {amount}");
                    }
                    break;

                case "ChangeTrust":
                    // 店ステータス（ShopStatusModel）の信頼度を変更する。Executor と同じく整数で扱う
                    // （旧実装は廃止済みの TomsModel.Trust を変更しており、保存もされていなかった）
                    if (cmd.parameters.TryGetValue("amount", out var trustStr) && int.TryParse(trustStr, out var trustAmount))
                    {
                        _shopStatusModel.ChangeTrust(trustAmount);
                        _shopStatusModel.SaveData();
                        Debug.Log($"[EventScenePresenter] ChangeTrust: {trustAmount}");
                    }
                    break;

                case "ShowMessageOnly":
                    // メッセージ表示のみ（特に何もしない）
                    break;

                default:
                    Debug.LogWarning($"[EventScenePresenter] Unknown command: {cmd.command}");
                    break;
            }
        }
    }

    /// <summary>
    /// コマンドの効果をテキストとして構築する
    /// </summary>
    private string BuildEffectText(List<TomsEventCommand> commands)
    {
        var sb = new StringBuilder();

        foreach (var cmd in commands)
        {
            switch (cmd.command)
            {
                case "ChangeMoney":
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr) && int.TryParse(moneyStr, out var amount))
                    {
                        if (amount >= 0)
                            sb.AppendLine($"所持金 +{amount:N0}G");
                        else
                            sb.AppendLine($"所持金 {amount:N0}G");
                    }
                    break;

                case "ChangeTrust":
                    if (cmd.parameters.TryGetValue("amount", out var trustStr) && int.TryParse(trustStr, out var trustAmount))
                    {
                        if (trustAmount >= 0)
                            sb.AppendLine($"信頼度 +{trustAmount}");
                        else
                            sb.AppendLine($"信頼度 {trustAmount}");
                    }
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }
}

