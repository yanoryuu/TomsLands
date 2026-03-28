using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// TomsShop シーン復帰時に EventOutputData を読み取り、
/// イベント結果を処理するハンドラ。
/// イベント後はターンを進めず、次のGameFlowノードへ進む。
/// </summary>
public class EventResultHandler : IStartable, IDisposable
{
    private readonly EventOutputData _outputData;
    private readonly EventInputData _inputData;
    private readonly GameFlowManager _gameFlowManager;

    public EventResultHandler(
        EventOutputData outputData,
        EventInputData inputData,
        GameFlowManager gameFlowManager)
    {
        _outputData = outputData;
        _inputData = inputData;
        _gameFlowManager = gameFlowManager;
    }

    public void Start()
    {
        // イベント結果がない場合（初回起動時など）はスキップ
        if (!_outputData.HasResult) return;

        Debug.Log($"[EventResultHandler] Processing event result. FlowIndex={_outputData.GameFlowIndex}");

        // GameFlowManager のインデックスを復元（シーン再生成で失われるため）
        _gameFlowManager.RestoreIndex(_outputData.GameFlowIndex);

        // 結果をクリア（次回のイベントまで誤発火しないように）
        _outputData.Clear();

        // イベントはターンを進めないので、NextTurn() で次のフローノードに進む
        // NextTurn() は _currentIndex++ してから次のノードを処理する
        _gameFlowManager.NextTurn();
    }

    public void Dispose() { }
}

