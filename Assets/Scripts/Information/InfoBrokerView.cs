using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening; // DOTween 忘れずに
using R3;

public class InfoBrokerView : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button closeButton;
    // [SerializeField] private Button refreshButton;
    [SerializeField] private GameObject heroTab;
    [SerializeField] private GameObject mapTab;
    // [SerializeField] private GameObject guessTab;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("Tab Buttons (左から 地図, 勇者, 予測)")]
    [SerializeField] private Button mapButton;
    [SerializeField] private Button heroButton;
    // [SerializeField] private Button guessButton;

    [Header("Content Panels")]
    [SerializeField] private GameObject mapPanel;
    [SerializeField] private GameObject heroPanel;
    // [SerializeField] private GameObject guessPanel;

    public Subject<Unit> OnCloseRequested { get; } = new();
    public Subject<Unit> OnRefreshRequested { get; } = new();
    public Subject<InfoBrokerTab> OnChangePanel { get; } = new();

    private readonly Dictionary<InfoBrokerTab, Vector3> initTabPos = new();
    private InfoBrokerTab _currentTab = InfoBrokerTab.Map;
    

    private readonly List<string> greetingMessages = new()
    {
        "よう！何か知りたいことがあるかい？",
        "やあ、いい情報を集めてるよ",
        "いらっしゃい！特売の話がありますよ",
        "おまえにも！勇者の動き、気になる？",
        "よう！最近の勇者の様子、知ってるだろう？",
        "旅の人！情報を色々持ってるよ"
    };

    private System.Random random = new System.Random();

    private void Awake()
    {
        closeButton.onClick.AddListener(() => OnCloseRequested.OnNext(Unit.Default));
        // refreshButton.onClick.AddListener(() => OnRefreshRequested.OnNext(Unit.Default));

        mapButton.onClick.AddListener(() => { _currentTab = InfoBrokerTab.Map; OnChangePanel.OnNext(InfoBrokerTab.Map); });
        heroButton.onClick.AddListener(() => { _currentTab = InfoBrokerTab.Hero; OnChangePanel.OnNext(InfoBrokerTab.Hero); });
        // guessButton.onClick.AddListener(() => { _currentTab = InfoBrokerTab.Guess; OnChangePanel.OnNext(InfoBrokerTab.Guess); });
        
        initTabPos[InfoBrokerTab.Hero] = heroTab.transform.localPosition;
        initTabPos[InfoBrokerTab.Map] = mapTab.transform.localPosition;
        // initTabPos[InfoBrokerTab.Guess] = guessTab.transform.localPosition;

        // brokerNameText.text = "情報屋";
        // ShowRandomGreeting();
        
        // 初期表示: 地図タブを選択
        ShowPanel(InfoBrokerTab.Map);
        SortItemTab(InfoBrokerTab.Map);
    }

    // private void ShowRandomGreeting()
    // {
    //     var greeting = greetingMessages[random.Next(greetingMessages.Count)];
    //     messageText.text = greeting;
    // }

    // /// <summary>
    // /// Model から受け取った InfoMessage 一覧を messageText に表示
    // /// </summary>
    // public void DisplayInfoMessages(List<InfoMessage> messages)
    // {
    //     if (messages == null || messages.Count == 0)
    //     {
    //         // メッセージがないときのデフォルト
    //         messageText.text = "特に変わったことはないなぁ。また来てくれ。";
    //         return;
    //     }
    //
    //     // InfoMessage.message を改行区切りで並べる
    //     var lines = messages.Select(m => m.message).ToList();
    //     messageText.text = string.Join("\n", lines);
    // }
    
    /// <summary>
    /// タブに対応するコンテンツパネルの表示を切り替え
    /// </summary>
    public void ShowPanel(InfoBrokerTab tab)
    {
        if (mapPanel) mapPanel.SetActive(tab == InfoBrokerTab.Map);
        if (heroPanel) heroPanel.SetActive(tab == InfoBrokerTab.Hero);
        // if (guessPanel) guessPanel.SetActive(tab == InfoBrokerTab.Guess);
    }
    
    public void SortItemTab(InfoBrokerTab type)
    {
        var heroSeq =  heroTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Hero].y, 0.1f);
        var mapSeq = mapTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Map].y, 0.1f);
        // var guessSeq = guessTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Guess].y, 0.1f);
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
            // case InfoBrokerTab.Guess:
            //     guessSeq.Kill();
            //     guessTab.transform.DOLocalMoveY(initTabPos[InfoBrokerTab.Guess].y + 10, 0.2f);
            //     break;
        }
    }
}

public enum InfoBrokerTab
{
    Hero,
    Map,
    Guess
}