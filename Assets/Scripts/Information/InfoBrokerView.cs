using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening; // ★ これを忘れずに
using R3;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private TextMeshProUGUI brokerNameText;
    [SerializeField] private GameObject heroTab;
    [SerializeField] private GameObject mapTab;
    [SerializeField] private GameObject guessTab;
    [SerializeField] private TextMeshProUGUI messageText;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();

    private readonly Dictionary<InfoBrokerTab, Vector3> initTabPos = new();
    

    private readonly List<string> greetingMessages = new()
    {
        "よう！何か聞きたいことあるかい？",
        "やあ、今日も情報収集してるよ",
        "おつかれさん！面白い話があるんだ",
        "こんにちは！勇者の動向、気になる？",
        "よう！最近の勇者の様子、教えてやろうか？",
        "お疲れ様！今日も色々見てきたよ"
    };

    private System.Random random = new System.Random();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        refreshButton.onClick.AddListener(() => OnRefreshRequested.OnNext(Unit.Default));
        
        initTabPos[InfoBrokerTab.Hero] = heroTab.transform.localPosition;
        initTabPos[InfoBrokerTab.Map] = mapTab.transform.localPosition;
        initTabPos[InfoBrokerTab.Guess] = guessTab.transform.localPosition;

        brokerNameText.text = "情報屋";
        ShowRandomGreeting();
    }

    private void ShowRandomGreeting()
    {
        var greeting = greetingMessages[random.Next(greetingMessages.Count)];
        messageText.text = greeting;
    }

    /// <summary>
    /// Model から受け取った InfoMessage 一覧を messageText に表示
    /// </summary>
    public void DisplayInfoMessages(List<InfoMessage> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            // メッセージがないときのデフォルト
            messageText.text = "特に変わったことはないかな。また後で来てよ。";
            return;
        }

        // InfoMessage.message を改行区切りで並べる
        var lines = messages.Select(m => m.message).ToList();
        messageText.text = string.Join("\n", lines);
    }
    
    public void SortItemTab(InfoBrokerTab type)
    {
        var heroSeq =  heroTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Hero].y, 0.1f);
        var mapSeq = mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y, 0.1f);
        var guessSeq = guessTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Guess].y, 0.1f);
        // タブを一番上に持ってくる動作はそのまま
        switch (type)
        {
            case InfoBrokerTab.Hero:
                heroSeq.Kill();
                heroTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Hero].y+10, 0.2f);
                break;
            case InfoBrokerTab.Map:
                mapSeq.Kill();
                mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y + 10, 0.2f);
                break;
            case InfoBrokerTab.Guess:
                guessSeq.Kill();
                guessTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Guess].y + 10, 0.2f);
                break;
        }
    }
}

public enum InfoBrokerTab
{
    Hero,
    Map,
    Guess
}