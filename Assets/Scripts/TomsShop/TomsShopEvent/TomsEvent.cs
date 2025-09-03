using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TomsEvent
{
    public string id;
    public string title;
    public string description;
    public List<TomsEventCommand> commands = new();
}

[System.Serializable]
public class TomsEventCommand
{
    public string command;
    public Dictionary<string, string> parameters = new();
}