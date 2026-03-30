using Cysharp.Threading.Tasks;
using R3;
using System;
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

    public EventScenePresenter(
        EventInputData inputData,
        EventOutputData outputData,
        EventSceneView view,
        SceneTransitionService sceneTransition,
        TomsModel tomsModel)
    {
        _inputData = inputData;
        _outputData = outputData;
        _view = view;
        _sceneTransition = sceneTransition;
        _tomsModel = tomsModel;
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

        // コマンドを実行（TomsModel に直接適用）
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
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr))
                    {
                        int amount = int.Parse(moneyStr);
                        _tomsModel.PlayerMoney.Value += amount;
                        _tomsModel.SavePlayerMoney();
                        Debug.Log($"[EventScenePresenter] ChangeMoney: {amount}");
                    }
                    break;

                case "ChangeTrust":
                    if (cmd.parameters.TryGetValue("amount", out var trustStr))
                    {
                        float trustAmount = float.Parse(trustStr);
                        _tomsModel.Trust.Value += trustAmount;
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
                    if (cmd.parameters.TryGetValue("amount", out var moneyStr))
                    {
                        int amount = int.Parse(moneyStr);
                        if (amount >= 0)
                            sb.AppendLine($"所持金 +{amount}G");
                        else
                            sb.AppendLine($"所持金 {amount}G");
                    }
                    break;

                case "ChangeTrust":
                    if (cmd.parameters.TryGetValue("amount", out var trustStr))
                    {
                        float trustAmount = float.Parse(trustStr);
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

