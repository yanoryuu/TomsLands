using System;
using UnityEngine;
using VContainer.Unity;

public class GameFlowManager : IDisposable, IStartable
{
    private GameFlow _gameFlow;
    
    private GameFlowGenerator _gameFlowGenerator;

    public GameFlowManager(GameFlowGenerator gameFlowGenerator)
    {
        _gameFlowGenerator = gameFlowGenerator;
    }
    
    public void Start()
    {
        _gameFlow = Resources.Load<GameFlow>("GameFlow/GameFlow");

        if (_gameFlow == null)
        {
            Debug.LogError("GameFlow not found in Resources/GameFlow folder");
        }
    }

    public void Dispose()
    {

    }
}