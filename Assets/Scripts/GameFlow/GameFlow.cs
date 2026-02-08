using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameFlow", menuName = "ScriptableObjects/GameFlow", order = 1)]
public class GameFlow : ScriptableObject
{
    public List<GameFlowNode> GameFlowStack = new();
}

[Serializable]
public class GameFlowNode
{
    public GameEvent EventType;
    
    [ConditionalHide("EventType", (int)GameEvent.Event)]
    public string EventData;
}

public enum GameEvent
{
    Start,
    Battle,
    Shop,
    Event,
    End
}