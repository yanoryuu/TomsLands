using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class LogManager : MonoBehaviour
{
    [SerializeField] private TMP_Text logText;
    [SerializeField] private int maxLogLines = 16;

    private readonly Queue<string> _logMessages = new Queue<string>();
    private BattleManager _battleManager;

    void Start()
    {
        _battleManager = FindObjectOfType<BattleManager>();
        if (_battleManager != null)
        {
            _battleManager.OnLogMessage += AddMessage;
        }
    }

    void OnDestroy()
    {
        if (_battleManager != null)
        {
            _battleManager.OnLogMessage -= AddMessage;
        }
    }

    private void AddMessage(string message)
    {
        _logMessages.Enqueue(message);
        if (_logMessages.Count > maxLogLines)
        {
            _logMessages.Dequeue();
        }
        logText.text = string.Join("\n", _logMessages);
    }
}